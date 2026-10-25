-- Append-only custody accounting. Current generation is a pointer over immutable lots and postings.
ALTER TABLE ledger.locations ADD COLUMN mixed_bulk boolean NOT NULL DEFAULT false;

CREATE TABLE ledger.mixed_bulk_policies (
    id uuid PRIMARY KEY,
    location_id uuid NOT NULL REFERENCES ledger.locations,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    commodity_id uuid NOT NULL REFERENCES ledger.commodities,
    stage text NOT NULL CHECK(stage IN (
        'RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE'
    )),
    product_identity text NOT NULL CHECK(length(trim(product_identity))>0),
    location_kind text NOT NULL CHECK(length(trim(location_kind))>0),
    effective_from timestamptz NOT NULL,
    evidence_id uuid NOT NULL REFERENCES ledger.evidence,
    approved_by uuid NOT NULL REFERENCES identity.users("Id"),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX mixed_bulk_policies_match ON ledger.mixed_bulk_policies
    (location_id,owner_id,commodity_id,stage,product_identity,effective_from DESC);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.mixed_bulk_policies
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
REVOKE ALL ON ledger.mixed_bulk_policies FROM PUBLIC;

CREATE TABLE ledger.mixed_bulk_pools (
    location_id uuid PRIMARY KEY REFERENCES ledger.locations,
    lot_id uuid UNIQUE REFERENCES ledger.lots DEFERRABLE INITIALLY DEFERRED,
    revision bigint NOT NULL DEFAULT 0 CHECK(revision>=0),
    owner_id uuid REFERENCES identity.organizations("Id"),
    commodity_id uuid REFERENCES ledger.commodities,
    stage text,
    product_identity text,
    CHECK((lot_id IS NULL) OR revision>0),
    CHECK((owner_id IS NULL AND commodity_id IS NULL AND stage IS NULL AND product_identity IS NULL)
       OR (owner_id IS NOT NULL AND commodity_id IS NOT NULL AND stage IS NOT NULL AND product_identity IS NOT NULL))
);
REVOKE ALL ON ledger.mixed_bulk_pools FROM PUBLIC;

CREATE TABLE ledger.custody_events (
    event_id uuid PRIMARY KEY REFERENCES ledger.events,
    command_id uuid NOT NULL,
    command_kind text NOT NULL,
    action text NOT NULL CHECK(action IN ('INGRESS','WITHDRAW')),
    actor_id uuid NOT NULL REFERENCES identity.users("Id"),
    location_id uuid NOT NULL REFERENCES ledger.locations,
    input_lot_id uuid REFERENCES ledger.lots,
    output_lot_id uuid NOT NULL REFERENCES ledger.lots DEFERRABLE INITIALLY DEFERRED,
    policy_id uuid REFERENCES ledger.mixed_bulk_policies,
    revision bigint NOT NULL CHECK(revision>0),
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0),
    request jsonb NOT NULL CHECK(jsonb_typeof(request)='object'),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK(action<>'INGRESS' OR policy_id IS NOT NULL)
);
CREATE UNIQUE INDEX custody_command_idempotency ON ledger.custody_events
    (command_id,command_kind,action,location_id,COALESCE(input_lot_id,'00000000-0000-0000-0000-000000000000'::uuid));
CREATE INDEX custody_events_location_revision ON ledger.custody_events(location_id,revision);
REVOKE ALL ON ledger.custody_events FROM PUBLIC;

CREATE TABLE ledger.custody_edges (
    event_id uuid NOT NULL REFERENCES ledger.custody_events(event_id),
    input_lot_id uuid NOT NULL REFERENCES ledger.lots,
    output_lot_id uuid NOT NULL REFERENCES ledger.lots,
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0),
    PRIMARY KEY(event_id,input_lot_id,output_lot_id)
);
CREATE INDEX custody_edges_output ON ledger.custody_edges(output_lot_id,event_id);
CREATE INDEX custody_edges_input ON ledger.custody_edges(input_lot_id,event_id);
REVOKE ALL ON ledger.custody_edges FROM PUBLIC;

CREATE TABLE ledger.custody_allocations (
    event_id uuid NOT NULL REFERENCES ledger.custody_events(event_id),
    input_lot_id uuid NOT NULL REFERENCES ledger.lots,
    output_lot_id uuid NOT NULL REFERENCES ledger.lots,
    source_lot_id uuid NOT NULL REFERENCES ledger.lots,
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0),
    PRIMARY KEY(event_id,input_lot_id,output_lot_id,source_lot_id)
);
CREATE INDEX custody_allocations_output ON ledger.custody_allocations(output_lot_id,source_lot_id);
CREATE INDEX custody_allocations_source ON ledger.custody_allocations(source_lot_id,output_lot_id);
REVOKE ALL ON ledger.custody_allocations FROM PUBLIC;
CREATE TABLE ledger.custody_processing_allocations (
    event_id uuid NOT NULL REFERENCES ledger.events,
    operation_id uuid NOT NULL REFERENCES ledger.operations,
    input_lot_id uuid NOT NULL REFERENCES ledger.lots,
    output_lot_id uuid NOT NULL REFERENCES ledger.lots,
    source_lot_id uuid NOT NULL REFERENCES ledger.lots,
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0),
    PRIMARY KEY(event_id,input_lot_id,output_lot_id,source_lot_id)
);
CREATE INDEX custody_processing_allocations_output
    ON ledger.custody_processing_allocations(output_lot_id,source_lot_id);
CREATE INDEX custody_processing_allocations_input
    ON ledger.custody_processing_allocations(input_lot_id,output_lot_id);
REVOKE ALL ON ledger.custody_processing_allocations FROM PUBLIC;

CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.custody_processing_allocations
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();

CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.custody_events
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.custody_edges
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.custody_allocations
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();

CREATE FUNCTION ledger.custody_allocation_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE checked_event uuid := COALESCE(NEW.event_id,OLD.event_id);
BEGIN
    IF EXISTS(
        SELECT 1 FROM ledger.custody_edges edge
        LEFT JOIN LATERAL (
            SELECT sum(allocation.quantity_kg) quantity
            FROM ledger.custody_allocations allocation
            WHERE allocation.event_id=edge.event_id AND allocation.input_lot_id=edge.input_lot_id
              AND allocation.output_lot_id=edge.output_lot_id
        ) actual ON true
        WHERE edge.event_id=checked_event AND actual.quantity IS DISTINCT FROM edge.quantity_kg
    ) THEN
        RAISE EXCEPTION 'Custody source allocations must exactly conserve each input edge' USING ERRCODE='23514';
    END IF;
    IF EXISTS(
        SELECT 1 FROM ledger.custody_edges edge
        WHERE edge.event_id=checked_event
        GROUP BY edge.output_lot_id
        HAVING sum(edge.quantity_kg) IS DISTINCT FROM (
            SELECT COALESCE(sum(contribution.quantity_kg),0)
            FROM ledger.contributions contribution WHERE contribution.lot_id=edge.output_lot_id
        )
    ) THEN
        RAISE EXCEPTION 'Custody output contribution mass must equal incoming edges' USING ERRCODE='23514';
    END IF;
    IF EXISTS(
        WITH expected AS (
            SELECT output_lot_id,source_lot_id,sum(quantity_kg) quantity
            FROM ledger.custody_allocations WHERE event_id=checked_event
            GROUP BY output_lot_id,source_lot_id
        ), actual AS (
            SELECT contribution.lot_id output_lot_id,contribution.source_lot_id,sum(contribution.quantity_kg) quantity
            FROM ledger.contributions contribution
            WHERE contribution.lot_id IN (
                SELECT edge.output_lot_id FROM ledger.custody_edges edge WHERE edge.event_id=checked_event
            )
            GROUP BY contribution.lot_id,contribution.source_lot_id
        )
        SELECT 1 FROM expected FULL JOIN actual USING(output_lot_id,source_lot_id)
        WHERE expected.quantity IS DISTINCT FROM actual.quantity
    ) THEN
        RAISE EXCEPTION 'Custody output source identity must equal persisted allocation shares' USING ERRCODE='23514';
    END IF;
    RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER custody_edge_allocation_guard AFTER INSERT ON ledger.custody_edges
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.custody_allocation_guard();
CREATE CONSTRAINT TRIGGER custody_allocation_guard AFTER INSERT ON ledger.custody_allocations
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.custody_allocation_guard();
CREATE FUNCTION ledger.custody_processing_allocation_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE checked_event uuid := COALESCE(NEW.event_id,OLD.event_id);
BEGIN
    IF EXISTS(
        SELECT 1 FROM ledger.custody_processing_allocations allocation
        JOIN ledger.events event ON event.id=allocation.event_id
        WHERE allocation.event_id=checked_event AND event.operation_id IS DISTINCT FROM allocation.operation_id
    ) THEN
        RAISE EXCEPTION 'Processing ancestry operation must match committed event' USING ERRCODE='23514';
    END IF;
    IF EXISTS(
        WITH expected AS (
            SELECT output_lot_id,source_lot_id,sum(quantity_kg) quantity
            FROM ledger.custody_processing_allocations WHERE event_id=checked_event
            GROUP BY output_lot_id,source_lot_id
        ), actual AS (
            SELECT contribution.lot_id output_lot_id,contribution.source_lot_id,sum(contribution.quantity_kg) quantity
            FROM ledger.contributions contribution
            WHERE contribution.quantity_kg>0 AND contribution.lot_id IN (
                SELECT allocation.output_lot_id FROM ledger.custody_processing_allocations allocation
                WHERE allocation.event_id=checked_event
            )
            GROUP BY contribution.lot_id,contribution.source_lot_id
        )
        SELECT 1 FROM expected FULL JOIN actual USING(output_lot_id,source_lot_id)
        WHERE expected.quantity IS DISTINCT FROM actual.quantity
    ) THEN
        RAISE EXCEPTION 'Processing ancestry root shares must equal committed output attribution' USING ERRCODE='23514';
    END IF;
    RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER custody_processing_allocation_guard AFTER INSERT ON ledger.custody_processing_allocations
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.custody_processing_allocation_guard();
REVOKE ALL ON FUNCTION ledger.custody_allocation_guard() FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.custody_processing_allocation_guard() FROM PUBLIC;
-- Existing reservation machinery, extended only for explicitly custody-derived lots.
ALTER TABLE ledger.lot_code_reservations DROP CONSTRAINT lot_code_reservations_context_kind_check;
ALTER TABLE ledger.lot_code_reservations ADD CONSTRAINT lot_code_reservations_context_kind_check
    CHECK(context_kind IN ('SOURCE','OPERATION','CUSTODY'));
CREATE OR REPLACE FUNCTION ledger.reserve_lot_code_internal(v_lot uuid,v_owner uuid,v_kind text,v_context uuid,
    v_stage text,v_product text) RETURNS text
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE old ledger.lot_code_reservations; token text; candidate text; serial bigint;
BEGIN
    PERFORM ledger.lock_writes();
    IF v_lot IS NULL OR v_lot='00000000-0000-0000-0000-000000000000' OR v_owner IS NULL
       OR v_context IS NULL OR v_kind NOT IN ('SOURCE','OPERATION','CUSTODY') THEN
        RAISE EXCEPTION 'Lot code context is incomplete' USING ERRCODE='22023';
    END IF;
    token:=ledger.lot_code_token(v_stage,v_product);
    IF token IS NULL THEN RAISE EXCEPTION 'Unsupported lot stage and product identity' USING ERRCODE='23514'; END IF;
    SELECT * INTO old FROM ledger.lot_code_reservations WHERE lot_id=v_lot;
    IF FOUND THEN
        IF old.owner_id<>v_owner OR old.context_kind<>v_kind OR old.context_id<>v_context
           OR old.stage<>v_stage OR old.product_identity<>v_product THEN
            RAISE EXCEPTION 'Lot identifier already reserved for different provenance or product' USING ERRCODE='23514';
        END IF;
        RETURN old.code;
    END IF;
    IF EXISTS(SELECT 1 FROM ledger.lots WHERE id=v_lot) THEN
        RAISE EXCEPTION 'Lot identifier already has stock history' USING ERRCODE='23514';
    END IF;
    LOOP
        serial:=nextval('ledger.lot_code_serial');
        candidate:=token||'-'||to_char(clock_timestamp() AT TIME ZONE 'Asia/Ho_Chi_Minh','YYYYMMDD')||'-'
            ||lpad(serial::text,greatest(6,length(serial::text)),'0');
        EXIT WHEN NOT EXISTS(SELECT 1 FROM ledger.lot_codes WHERE code=candidate)
              AND NOT EXISTS(SELECT 1 FROM ledger.lot_code_reservations WHERE code=candidate);
    END LOOP;
    INSERT INTO ledger.lot_code_reservations(lot_id,code,owner_id,context_kind,context_id,stage,product_identity)
    VALUES(v_lot,candidate,v_owner,v_kind,v_context,v_stage,v_product);
    RETURN candidate;
END $$;
REVOKE ALL ON FUNCTION ledger.reserve_lot_code_internal(uuid,uuid,text,uuid,text,text) FROM PUBLIC;

CREATE FUNCTION ledger.mixed_bulk_eligible(loc uuid,owner uuid,commodity uuid,stage text,product text,at_time timestamptz)
RETURNS uuid LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
    SELECT p.id FROM ledger.locations l
    JOIN ledger.mixed_bulk_policies p ON p.location_id=l.id AND p.location_kind=l.kind
      AND p.owner_id=$2 AND p.commodity_id=$3 AND p.stage=$4
      AND p.product_identity=$5 AND p.effective_from<=$6
    ORDER BY p.effective_from DESC,p.id LIMIT 1
$$;
REVOKE ALL ON FUNCTION ledger.mixed_bulk_eligible(uuid,uuid,uuid,text,text,timestamptz) FROM PUBLIC;

CREATE FUNCTION ledger.mixed_bulk_assert_location(loc uuid) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE location ledger.locations; pool ledger.mixed_bulk_pools; amount numeric; current_lot ledger.lots;
BEGIN
    SELECT * INTO STRICT location FROM ledger.locations WHERE id=loc FOR UPDATE;
    SELECT * INTO pool FROM ledger.mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    IF location.mixed_bulk AND pool.location_id IS NULL THEN
        INSERT INTO ledger.mixed_bulk_pools(location_id) VALUES(loc);
        SELECT * INTO STRICT pool FROM ledger.mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    ELSIF NOT location.mixed_bulk AND pool.location_id IS NOT NULL AND pool.lot_id IS NOT NULL THEN
        RAISE EXCEPTION 'Cannot disable mixed-bulk mode while pool is occupied' USING ERRCODE='23514';
    END IF;
    IF pool.location_id IS NOT NULL AND pool.lot_id IS NOT NULL THEN
        SELECT * INTO STRICT current_lot FROM ledger.lots WHERE id=pool.lot_id;
        SELECT quantity_kg INTO amount FROM ledger.positions WHERE lot_id=pool.lot_id AND location_id=loc;
        IF amount IS NULL OR amount<=0 OR EXISTS(
            SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>pool.lot_id
        ) THEN RAISE EXCEPTION 'Mixed-bulk location must contain only current positive generation' USING ERRCODE='23514'; END IF;
        IF current_lot.owner_id IS DISTINCT FROM location.owner_id AND location.owner_id IS NOT NULL THEN
            RAISE EXCEPTION 'Mixed-bulk pool owner differs from location owner' USING ERRCODE='23514';
        END IF;
    ELSIF pool.location_id IS NOT NULL AND EXISTS(
        SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0
    ) THEN
        RAISE EXCEPTION 'Mixed-bulk location has stock without current generation' USING ERRCODE='23514';
    END IF;
END $$;
REVOKE ALL ON FUNCTION ledger.mixed_bulk_assert_location(uuid) FROM PUBLIC;

CREATE FUNCTION ledger.mixed_bulk_location_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF TG_OP='UPDATE' AND OLD.mixed_bulk AND NEW.cleared
       AND ((NEW.owner_id IS NULL
             AND (OLD.cleared IS DISTINCT FROM NEW.cleared OR OLD.owner_id IS DISTINCT FROM NEW.owner_id))
            OR NOT NEW.mixed_bulk)
       AND NOT EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=OLD.id AND quantity_kg>0)
       AND NOT EXISTS(SELECT 1 FROM ledger.mixed_bulk_pools WHERE location_id=OLD.id AND lot_id IS NOT NULL) THEN
        UPDATE ledger.mixed_bulk_pools
        SET owner_id=NULL,commodity_id=NULL,stage=NULL,product_identity=NULL
        WHERE location_id=OLD.id;
    END IF;
    IF TG_OP='UPDATE' AND (
       NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk
       OR (OLD.mixed_bulk OR NEW.mixed_bulk) AND (
           NEW.kind IS DISTINCT FROM OLD.kind OR NEW.owner_id IS DISTINCT FROM OLD.owner_id
       )
    ) THEN
        IF EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=OLD.id AND quantity_kg>0)
           OR EXISTS(SELECT 1 FROM ledger.mixed_bulk_pools WHERE location_id=OLD.id AND lot_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Occupied mixed-bulk location requires drain and recorded clearance before identity or mode change' USING ERRCODE='23514';
        END IF;
        IF OLD.mixed_bulk AND (
            NEW.kind IS DISTINCT FROM OLD.kind OR NEW.owner_id IS DISTINCT FROM OLD.owner_id
            OR NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk
        ) THEN
            IF NOT NEW.cleared AND NOT (
                OLD.cleared AND OLD.owner_id IS NULL AND NEW.owner_id IS NOT NULL
                AND NEW.kind=OLD.kind AND NEW.mixed_bulk=OLD.mixed_bulk
            ) THEN
                RAISE EXCEPTION 'Mixed-bulk identity or mode change requires recorded clearance' USING ERRCODE='23514';
            END IF;
            IF NEW.owner_id IS NOT NULL THEN NEW.cleared:=false; END IF;
        END IF;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER mixed_bulk_location_guard BEFORE INSERT OR UPDATE ON ledger.locations
    FOR EACH ROW EXECUTE FUNCTION ledger.mixed_bulk_location_guard();
REVOKE ALL ON FUNCTION ledger.mixed_bulk_location_guard() FROM PUBLIC;

CREATE FUNCTION ledger.mixed_bulk_pool_deferred_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE loc uuid := COALESCE(NEW.location_id,OLD.location_id); current_id uuid; amount numeric;
BEGIN
    IF (SELECT mixed_bulk FROM ledger.locations WHERE id=loc) THEN
        IF NOT EXISTS(SELECT 1 FROM ledger.mixed_bulk_pools WHERE location_id=loc) THEN
            RAISE EXCEPTION 'Mixed-bulk location lacks pool state' USING ERRCODE='23514';
        END IF;
        SELECT lot_id INTO current_id FROM ledger.mixed_bulk_pools WHERE location_id=loc;
        IF current_id IS NULL THEN
            IF EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0) THEN
                RAISE EXCEPTION 'Positive mixed-bulk stock lacks current generation pointer' USING ERRCODE='23514';
            END IF;
        ELSE
            SELECT quantity_kg INTO amount FROM ledger.positions WHERE lot_id=current_id AND location_id=loc;
            IF amount IS NULL OR amount<=0 OR EXISTS(
                SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>current_id
            ) THEN RAISE EXCEPTION 'Mixed-bulk current generation does not match physical position' USING ERRCODE='23514'; END IF;
            IF EXISTS(
                SELECT 1 FROM ledger.mixed_bulk_pools pool JOIN ledger.lots lot ON lot.id=current_id
                WHERE pool.location_id=loc AND (
                    pool.owner_id IS DISTINCT FROM lot.owner_id
                    OR pool.commodity_id IS DISTINCT FROM lot.commodity_id
                    OR pool.stage IS DISTINCT FROM lot.stage
                    OR pool.product_identity IS DISTINCT FROM lot.product_identity
                )
            ) THEN RAISE EXCEPTION 'Mixed-bulk generation identity differs from retained pool identity' USING ERRCODE='23514'; END IF;
            IF (SELECT sum(quantity_kg) FROM ledger.contributions WHERE lot_id=current_id) IS DISTINCT FROM amount THEN
                RAISE EXCEPTION 'Mixed-bulk generation contribution mass does not match position' USING ERRCODE='23514';
            END IF;
        END IF;
    ELSE
        IF EXISTS(SELECT 1 FROM ledger.mixed_bulk_pools WHERE location_id=loc AND lot_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Disabled mixed-bulk location retains a current generation pointer' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER mixed_bulk_position_guard AFTER INSERT OR UPDATE OR DELETE ON ledger.positions
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.mixed_bulk_pool_deferred_guard();
CREATE CONSTRAINT TRIGGER mixed_bulk_pool_guard AFTER INSERT OR UPDATE ON ledger.mixed_bulk_pools
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.mixed_bulk_pool_deferred_guard();
REVOKE ALL ON FUNCTION ledger.mixed_bulk_pool_deferred_guard() FROM PUBLIC;
CREATE FUNCTION ledger.mixed_bulk_pool_row() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF NEW.mixed_bulk THEN
        INSERT INTO ledger.mixed_bulk_pools(location_id) VALUES(NEW.id) ON CONFLICT(location_id) DO NOTHING;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER mixed_bulk_pool_row AFTER INSERT OR UPDATE OF mixed_bulk ON ledger.locations
    FOR EACH ROW EXECUTE FUNCTION ledger.mixed_bulk_pool_row();
REVOKE ALL ON FUNCTION ledger.mixed_bulk_pool_row() FROM PUBLIC;

CREATE FUNCTION ledger.pool_ingress(a uuid,command_id uuid,command_kind text,loc uuid,incoming_lot uuid,quantity numeric)
RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
<<pool_ingress_block>>
DECLARE
    destination ledger.locations; incoming ledger.lots; old_pool ledger.mixed_bulk_pools; current_lot ledger.lots;
    policy uuid; prior numeric:=0; incoming_position numeric; stock_total numeric;
    event_id uuid:=gen_random_uuid(); output_id uuid:=gen_random_uuid(); output_code text; output_origin uuid;
    req jsonb; result_id uuid;
BEGIN
    PERFORM ledger.lock_writes();
    IF quantity<=0 OR quantity<>round(quantity,6) OR command_id IS NULL OR command_kind IS NULL THEN
        RAISE EXCEPTION 'Positive exact custody ingress quantity and command identity required' USING ERRCODE='22023';
    END IF;
    SELECT * INTO STRICT destination FROM ledger.locations WHERE id=loc FOR UPDATE;
    IF NOT destination.mixed_bulk THEN RETURN incoming_lot; END IF;
    SELECT * INTO incoming FROM ledger.lots WHERE id=incoming_lot FOR UPDATE;
    req:=jsonb_build_object('actorId',a,'commandId',command_id,'commandKind',command_kind,'locationId',loc,
        'incomingLotId',incoming_lot,'quantityKg',quantity::text);
    SELECT ce.output_lot_id INTO result_id FROM ledger.custody_events ce
    WHERE ce.command_id=$2 AND ce.command_kind=$3 AND ce.action='INGRESS'
      AND ce.location_id=$4 AND ce.input_lot_id IS NOT DISTINCT FROM $5;
    IF FOUND THEN
        IF (SELECT ce.request FROM ledger.custody_events ce WHERE ce.command_id=$2
            AND ce.command_kind=$3 AND ce.action='INGRESS' AND ce.location_id=$4
            AND ce.input_lot_id IS NOT DISTINCT FROM $5) IS DISTINCT FROM req THEN
            RAISE EXCEPTION 'Custody command retry conflicts with original payload' USING ERRCODE='23505';
        END IF;
        RETURN result_id;
    END IF;
    IF incoming.id IS NULL THEN RAISE EXCEPTION 'Incoming custody lot unavailable' USING ERRCODE='23503'; END IF;
    IF NOT ledger.assigned(a,destination.facility_id) OR ledger.role(a) NOT IN ('MillOperator','MillManager') THEN
        RAISE EXCEPTION 'Mixed-bulk custody authority denied' USING ERRCODE='42501';
    END IF;
    IF incoming.owner_id IS DISTINCT FROM destination.owner_id AND destination.owner_id IS NOT NULL THEN
        RAISE EXCEPTION 'Cross-owner mixed-bulk ingress denied' USING ERRCODE='23514';
    END IF;
    incoming_position:=COALESCE((SELECT quantity_kg FROM ledger.positions WHERE lot_id=incoming_lot AND location_id=loc),0);
    IF incoming_position<quantity THEN RAISE EXCEPTION 'Selected ingress position has insufficient stock' USING ERRCODE='23514'; END IF;
    SELECT COALESCE(sum(quantity_kg),0) INTO stock_total FROM ledger.positions WHERE lot_id=incoming_lot AND quantity_kg>0;
    IF (SELECT count(*) FROM ledger.contributions WHERE lot_id=incoming_lot)=0 THEN
        RAISE EXCEPTION 'Incoming lot lacks source attribution' USING ERRCODE='23514';
    END IF;
    IF (SELECT count(*) FROM ledger.contributions WHERE lot_id=incoming_lot)>1
       AND (quantity<stock_total OR (SELECT sum(quantity_kg) FROM ledger.contributions WHERE lot_id=incoming_lot) IS DISTINCT FROM stock_total) THEN
        RAISE EXCEPTION 'Multi-source lot position attribution is ambiguous for partial mixed-bulk ingress' USING ERRCODE='23514';
    END IF;
    IF (SELECT count(*) FROM ledger.contributions WHERE lot_id=incoming_lot)=1
       AND NOT EXISTS(SELECT 1 FROM ledger.contributions WHERE lot_id=incoming_lot AND quantity_kg>0) THEN
        RAISE EXCEPTION 'Incoming lot lacks positive source attribution' USING ERRCODE='23514';
    END IF;
    IF ledger.blocked(incoming_lot) THEN RAISE EXCEPTION 'Held or restricted stock cannot enter mixed-bulk pool' USING ERRCODE='23514'; END IF;
    policy:=ledger.mixed_bulk_eligible(loc,incoming.owner_id,incoming.commodity_id,incoming.stage,incoming.product_identity,clock_timestamp());
    IF policy IS NULL THEN RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514'; END IF;
    SELECT * INTO old_pool FROM ledger.mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    IF old_pool.location_id IS NULL THEN
        INSERT INTO ledger.mixed_bulk_pools(location_id) VALUES(loc) ON CONFLICT DO NOTHING;
        SELECT * INTO STRICT old_pool FROM ledger.mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    END IF;
    IF old_pool.lot_id IS NOT NULL AND old_pool.lot_id=incoming_lot THEN
        RAISE EXCEPTION 'Current pool generation cannot be re-ingressed as an input lot' USING ERRCODE='23514';
    END IF;
    IF old_pool.lot_id IS NOT NULL THEN
        SELECT * INTO STRICT current_lot FROM ledger.lots WHERE id=old_pool.lot_id FOR UPDATE;
        SELECT COALESCE(quantity_kg,0) INTO prior FROM ledger.positions WHERE lot_id=old_pool.lot_id AND location_id=loc;
        IF prior<=0 OR EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0
            AND lot_id<>old_pool.lot_id AND lot_id<>incoming_lot) THEN
            RAISE EXCEPTION 'Mixed-bulk current generation pointer is inconsistent' USING ERRCODE='23514';
        END IF;
        IF current_lot.owner_id<>incoming.owner_id OR current_lot.commodity_id<>incoming.commodity_id
           OR current_lot.stage<>incoming.stage OR current_lot.product_identity<>incoming.product_identity
           OR ledger.mixed_bulk_eligible(loc,current_lot.owner_id,current_lot.commodity_id,current_lot.stage,current_lot.product_identity,clock_timestamp()) IS DISTINCT FROM policy THEN
            RAISE EXCEPTION 'Mixed-bulk pool identity or approved policy mismatch' USING ERRCODE='23514';
        END IF;
        IF ledger.blocked(current_lot.id) THEN RAISE EXCEPTION 'Held or restricted pool contents cannot be topped up' USING ERRCODE='23514'; END IF;
    ELSIF EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>incoming_lot) THEN
        RAISE EXCEPTION 'Mixed-bulk location has unrelated positive stock' USING ERRCODE='23514';
    END IF;
    IF old_pool.owner_id IS NOT NULL AND (
        old_pool.owner_id IS DISTINCT FROM incoming.owner_id
        OR old_pool.commodity_id IS DISTINCT FROM incoming.commodity_id
        OR old_pool.stage IS DISTINCT FROM incoming.stage
        OR old_pool.product_identity IS DISTINCT FROM incoming.product_identity
    ) AND NOT destination.cleared THEN
        RAISE EXCEPTION 'Drained mixed-bulk identity requires recorded clearance before replacement' USING ERRCODE='23514';
    END IF;
    PERFORM ledger.credit_location(loc,incoming.owner_id);
    output_code:=ledger.reserve_lot_code_internal(output_id,incoming.owner_id,'CUSTODY',command_id,incoming.stage,incoming.product_identity);
    output_origin:=ledger.register_code(output_code,output_id);
    INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(output_id,output_code,incoming.owner_id,incoming.commodity_id,incoming.stage,incoming.product_identity,output_origin);
    INSERT INTO ledger.events(id,kind,actor_id,lot_id,details) VALUES(event_id,'POOL_INGRESS',a,output_id,
        jsonb_build_object('commandId',command_id,'commandKind',command_kind,'locationId',loc,
            'incomingLotId',incoming_lot,'quantityKg',quantity::text,'priorPoolLotId',old_pool.lot_id));
    INSERT INTO ledger.custody_events(event_id,command_id,command_kind,action,actor_id,location_id,input_lot_id,
        output_lot_id,policy_id,revision,quantity_kg,request)
    VALUES(event_id,command_id,command_kind,'INGRESS',a,loc,incoming_lot,output_id,policy,old_pool.revision+1,quantity,req);
    IF old_pool.lot_id IS NOT NULL THEN
        PERFORM ledger.post(event_id,old_pool.lot_id,loc,-prior);
        INSERT INTO ledger.custody_edges VALUES(event_id,old_pool.lot_id,output_id,prior);
        INSERT INTO ledger.custody_allocations
        SELECT event_id,old_pool.lot_id,output_id,source_lot_id,quantity_kg
        FROM ledger.contributions WHERE lot_id=old_pool.lot_id AND quantity_kg>0;
    END IF;
    PERFORM ledger.post(event_id,incoming_lot,loc,-quantity);
    INSERT INTO ledger.custody_edges VALUES(event_id,incoming_lot,output_id,quantity);
    IF (SELECT count(*) FROM ledger.contributions WHERE lot_id=incoming_lot)=1 THEN
        INSERT INTO ledger.custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,incoming_lot,output_id,source_lot_id,quantity FROM ledger.contributions WHERE lot_id=incoming_lot;
    ELSE
        INSERT INTO ledger.custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,incoming_lot,output_id,source_lot_id,quantity_kg FROM ledger.contributions WHERE lot_id=incoming_lot AND quantity_kg>0;
    END IF;
    INSERT INTO ledger.contributions(lot_id,source_lot_id,quantity_kg)
    SELECT output_id,source_lot_id,sum(quantity_kg)
    FROM ledger.custody_allocations
    WHERE ledger.custody_allocations.event_id=pool_ingress_block.event_id
    GROUP BY source_lot_id HAVING sum(quantity_kg)>0;
    PERFORM ledger.post(event_id,output_id,loc,prior+quantity);
    UPDATE ledger.mixed_bulk_pools SET lot_id=output_id,revision=revision+1,
        owner_id=incoming.owner_id,commodity_id=incoming.commodity_id,stage=incoming.stage,
        product_identity=incoming.product_identity WHERE location_id=loc;
    UPDATE ledger.locations SET cleared=false WHERE id=loc AND cleared;
    IF ledger.blocked(output_id) THEN
        RAISE EXCEPTION 'Held or restricted stock cannot enter mixed-bulk pool' USING ERRCODE='23514';
    END IF;
    RETURN output_id;
END $$;

CREATE FUNCTION ledger.pool_withdraw(a uuid,command_id uuid,command_kind text,lot uuid,loc uuid,
    quantity numeric,expected_revision bigint DEFAULT NULL) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    destination ledger.locations; source ledger.lots; pool ledger.mixed_bulk_pools; policy uuid;
    source_position numeric; source_total numeric; event_id uuid:=gen_random_uuid(); aliquot_id uuid:=gen_random_uuid();
    remainder_id uuid:=gen_random_uuid(); actual_id uuid; aliquot_code text; remainder_code text;
    aliquot_origin uuid; remainder_origin uuid; req jsonb; result_id uuid; revision_value bigint:=0;
    output_quantity numeric; source_count integer; force_extract boolean;
BEGIN
    PERFORM ledger.lock_writes();
    IF quantity<=0 OR quantity<>round(quantity,6) OR command_id IS NULL OR command_kind IS NULL THEN
        RAISE EXCEPTION 'Positive exact custody withdrawal quantity and command identity required' USING ERRCODE='22023';
    END IF;
    SELECT * INTO STRICT destination FROM ledger.locations WHERE id=loc FOR UPDATE;
    SELECT * INTO STRICT source FROM ledger.lots WHERE id=lot FOR UPDATE;
    SELECT COALESCE(quantity_kg,0) INTO source_position FROM ledger.positions WHERE lot_id=lot AND location_id=loc;
    force_extract:=command_kind='TRANSFER_TO_MIXED';
    IF NOT destination.mixed_bulk AND NOT force_extract
       AND NOT ledger.mixed_bulk_custody_derived(lot) THEN RETURN lot; END IF;
    req:=jsonb_build_object('actorId',a,'commandId',command_id,'commandKind',command_kind,'lotId',lot,
        'locationId',loc,'quantityKg',quantity::text,'expectedRevision',expected_revision);
    SELECT ce.output_lot_id INTO result_id FROM ledger.custody_events ce
    WHERE ce.command_id=$2 AND ce.command_kind=$3 AND ce.action='WITHDRAW'
      AND ce.location_id=$5 AND ce.input_lot_id=$4;
    IF FOUND THEN
        IF (SELECT ce.request FROM ledger.custody_events ce WHERE ce.command_id=$2
            AND ce.command_kind=$3 AND ce.action='WITHDRAW' AND ce.location_id=$5 AND ce.input_lot_id=$4)
            IS DISTINCT FROM req THEN RAISE EXCEPTION 'Custody command retry conflicts with original payload' USING ERRCODE='23505'; END IF;
        RETURN result_id;
    END IF;
    IF NOT ledger.assigned(a,destination.facility_id) OR ledger.role(a) NOT IN ('MillOperator','MillManager') THEN
        RAISE EXCEPTION 'Mixed-bulk custody authority denied' USING ERRCODE='42501';
    END IF;
    IF source_position<quantity THEN RAISE EXCEPTION 'Selected stock position has insufficient quantity' USING ERRCODE='23514'; END IF;
    IF ledger.receiving_restricted(lot) OR EXISTS(SELECT 1 FROM ledger.receipt_correction_blocks b JOIN ledger.lots x ON x.receipt_id=b.receipt_id WHERE x.id=lot)
       OR EXISTS(SELECT 1 FROM ledger.receipt_reversals r JOIN ledger.lots x ON x.receipt_id=r.receipt_id WHERE x.id=lot)
       OR EXISTS(SELECT 1 FROM ledger.corrections c WHERE c.status IN ('REQUESTED','RECONCILED')
          AND EXISTS(SELECT 1 FROM ledger.lots x WHERE x.operation_id=c.operation_id AND x.id=lot)) THEN
        RAISE EXCEPTION 'Restricted or correction-bound stock cannot be withdrawn' USING ERRCODE='23514';
    END IF;
    SELECT COALESCE(sum(quantity_kg),0) INTO source_total FROM ledger.positions WHERE lot_id=lot AND quantity_kg>0;
    SELECT count(*) INTO source_count FROM ledger.contributions WHERE lot_id=lot AND quantity_kg>0;
    IF source_count=0 OR (source_count>1 AND (
        (SELECT sum(quantity_kg) FROM ledger.contributions WHERE lot_id=lot) IS DISTINCT FROM source_total
        OR source_position<>source_total
    )) THEN
        RAISE EXCEPTION 'Multi-source stock withdrawal attribution is ambiguous' USING ERRCODE='23514';
    END IF;
    IF destination.mixed_bulk THEN
        SELECT * INTO STRICT pool FROM ledger.mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
        IF pool.lot_id IS DISTINCT FROM lot THEN RAISE EXCEPTION 'Selected pool generation is stale' USING ERRCODE='40001'; END IF;
        IF expected_revision IS NULL OR expected_revision<>pool.revision THEN
            RAISE EXCEPTION 'Selected pool revision is stale' USING ERRCODE='40001';
        END IF;
        revision_value:=pool.revision;
        policy:=ledger.mixed_bulk_eligible(loc,source.owner_id,source.commodity_id,source.stage,source.product_identity,clock_timestamp());
        IF policy IS NULL THEN RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514'; END IF;
        IF source_position<>source_total OR EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>lot) THEN
            RAISE EXCEPTION 'Mixed-bulk generation position is not a complete physical balance' USING ERRCODE='23514';
        END IF;
    END IF;
    output_quantity:=source_position-quantity;
    actual_id:=aliquot_id;
    INSERT INTO ledger.events(id,kind,actor_id,lot_id,details) VALUES(event_id,'POOL_WITHDRAW',a,lot,
        jsonb_build_object('commandId',command_id,'commandKind',command_kind,'locationId',loc,
            'quantityKg',quantity::text,'poolRevision',revision_value,'expectedRevision',expected_revision));
    INSERT INTO ledger.custody_events(event_id,command_id,command_kind,action,actor_id,location_id,input_lot_id,
        output_lot_id,policy_id,revision,quantity_kg,request)
    VALUES(event_id,command_id,command_kind,'WITHDRAW',a,loc,lot,aliquot_id,policy,revision_value+1,quantity,req);
    aliquot_code:=ledger.reserve_lot_code_internal(aliquot_id,source.owner_id,'CUSTODY',command_id,source.stage,source.product_identity);
    aliquot_origin:=ledger.register_code(aliquot_code,aliquot_id);
    INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(aliquot_id,aliquot_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,aliquot_origin);
    IF output_quantity>0 THEN
        remainder_code:=ledger.reserve_lot_code_internal(remainder_id,source.owner_id,'CUSTODY',command_id,source.stage,source.product_identity);
        remainder_origin:=ledger.register_code(remainder_code,remainder_id);
        INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
        VALUES(remainder_id,remainder_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,remainder_origin);
    END IF;
    PERFORM ledger.post(event_id,lot,loc,-source_position);
    PERFORM ledger.post(event_id,aliquot_id,loc,quantity);
    INSERT INTO ledger.custody_edges VALUES(event_id,lot,aliquot_id,quantity);
    IF output_quantity>0 THEN
        PERFORM ledger.post(event_id,remainder_id,loc,output_quantity);
        INSERT INTO ledger.custody_edges VALUES(event_id,lot,remainder_id,output_quantity);
    END IF;
    -- Integer micro-kg largest remainder allocation; remainder is exact source-wise complement.
    WITH source_weights AS (
        SELECT source_lot_id,
               (CASE WHEN source_count=1 THEN source_position ELSE quantity_kg END*1000000)::bigint mass
        FROM ledger.contributions WHERE lot_id=lot AND quantity_kg>0
    ), bases AS (
        SELECT source_lot_id,div(quantity*1000000*mass,sum(mass) OVER()) base,
               mod(quantity*1000000*mass,sum(mass) OVER()) rem FROM source_weights
    ), ranked AS (
        SELECT *,row_number() OVER(ORDER BY rem DESC,source_lot_id) rn,
               quantity*1000000-sum(base) OVER() left_units FROM bases
    ), shares AS (
        SELECT source_lot_id,base+CASE WHEN rn<=left_units THEN 1 ELSE 0 END units FROM ranked
    )
    INSERT INTO ledger.contributions(lot_id,source_lot_id,quantity_kg)
    SELECT aliquot_id,source_lot_id,units/1000000::numeric FROM shares WHERE units>0;
    INSERT INTO ledger.custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
    SELECT event_id,lot,aliquot_id,source_lot_id,quantity_kg FROM ledger.contributions WHERE lot_id=aliquot_id;
    IF output_quantity>0 THEN
        INSERT INTO ledger.contributions(lot_id,source_lot_id,quantity_kg)
        SELECT remainder_id,prior.source_lot_id,
               CASE WHEN source_count=1 THEN source_position ELSE prior.quantity_kg END-COALESCE(aliquot.quantity_kg,0)
        FROM ledger.contributions prior LEFT JOIN ledger.contributions aliquot
          ON aliquot.lot_id=aliquot_id AND aliquot.source_lot_id=prior.source_lot_id
        WHERE prior.lot_id=lot AND
          (CASE WHEN source_count=1 THEN source_position ELSE prior.quantity_kg END)>COALESCE(aliquot.quantity_kg,0);
        INSERT INTO ledger.custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,lot,remainder_id,source_lot_id,quantity_kg FROM ledger.contributions WHERE lot_id=remainder_id;
    END IF;
    IF destination.mixed_bulk THEN
        UPDATE ledger.mixed_bulk_pools SET lot_id=CASE WHEN output_quantity>0 THEN remainder_id ELSE NULL END,
            revision=revision+1 WHERE location_id=loc;
    END IF;
    RETURN actual_id;
END $$;
REVOKE ALL ON FUNCTION ledger.pool_ingress(uuid,uuid,text,uuid,uuid,numeric) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.pool_withdraw(uuid,uuid,text,uuid,uuid,numeric,bigint) FROM PUBLIC;
