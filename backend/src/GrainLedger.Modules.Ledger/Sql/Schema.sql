CREATE SCHEMA IF NOT EXISTS ledger;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

CREATE TABLE ledger.facilities (
    id uuid PRIMARY KEY,
    name text NOT NULL CHECK(length(name) BETWEEN 1 AND 160)
);
CREATE TABLE ledger.locations (
    id uuid PRIMARY KEY,
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    name text NOT NULL CHECK(length(name) BETWEEN 1 AND 160),
    kind text NOT NULL CHECK(kind IN ('STORE','SILO','COMPARTMENT','ISOLATION')),
    owner_id uuid REFERENCES identity.organizations("Id"),
    cleared boolean NOT NULL DEFAULT true
);
CREATE TABLE ledger.equipment (
    id uuid PRIMARY KEY,
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    name text NOT NULL CHECK(length(name) BETWEEN 1 AND 160)
);
CREATE TABLE ledger.commodities (
    id uuid PRIMARY KEY,
    name text NOT NULL UNIQUE CHECK(length(name) BETWEEN 1 AND 160)
);
CREATE TABLE ledger.assignments (
    user_id uuid NOT NULL REFERENCES identity.users("Id"),
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    PRIMARY KEY(user_id,facility_id)
);
CREATE TABLE ledger.evidence (
    id uuid PRIMARY KEY,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    file_name text NOT NULL,
    content_type text NOT NULL,
    content bytea NOT NULL CHECK(octet_length(content) BETWEEN 1 AND 10485760),
    sha256 text NOT NULL,
    actor_id uuid NOT NULL REFERENCES identity.users("Id"),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE ledger.policies (
    id uuid PRIMARY KEY,
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    kind text NOT NULL CHECK(kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')),
    tolerance_percent numeric(20,6) NOT NULL CHECK(tolerance_percent BETWEEN 0 AND 100),
    effective_from timestamptz NOT NULL,
    evidence_id uuid NOT NULL REFERENCES ledger.evidence,
    UNIQUE(facility_id,kind,effective_from)
);
CREATE TABLE ledger.compatibilities (
    id uuid PRIMARY KEY,
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    input_products text[] NOT NULL CHECK(cardinality(input_products)>0),
    output_commodity_id uuid NOT NULL REFERENCES ledger.commodities,
    effective_from timestamptz NOT NULL,
    evidence_id uuid NOT NULL REFERENCES ledger.evidence
);
CREATE TABLE ledger.lots (
    id uuid PRIMARY KEY,
    code text NOT NULL,
    code_origin_id uuid NOT NULL,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    commodity_id uuid NOT NULL REFERENCES ledger.commodities,
    stage text NOT NULL CHECK(stage IN (
        'RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE'
    )),
    product_identity text NOT NULL,
    receipt_id uuid,
    receipt_line_id uuid UNIQUE,
    operation_id uuid,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE ledger.positions (
    lot_id uuid NOT NULL REFERENCES ledger.lots,
    location_id uuid NOT NULL REFERENCES ledger.locations,
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>=0),
    PRIMARY KEY(lot_id,location_id)
);
CREATE TABLE ledger.operations (
    id uuid PRIMARY KEY,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    facility_id uuid NOT NULL REFERENCES ledger.facilities,
    kind text NOT NULL CHECK(kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')),
    occurred_at timestamptz NOT NULL,
    equipment_id uuid NOT NULL REFERENCES ledger.equipment,
    content jsonb NOT NULL,
    status text NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','COMMITTED','CORRECTED')),
    version uuid NOT NULL,
    confirmed_version uuid,
    confirmed_by uuid REFERENCES identity.users("Id"),
    confirmation_evidence_id uuid REFERENCES ledger.evidence,
    variance_kg numeric(20,6),
    allowed_variance_kg numeric(20,6),
    correction_id uuid
);
ALTER TABLE ledger.lots ADD FOREIGN KEY(operation_id) REFERENCES ledger.operations;
CREATE TABLE ledger.events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    kind text NOT NULL,
    actor_id uuid NOT NULL REFERENCES identity.users("Id"),
    operation_id uuid REFERENCES ledger.operations,
    lot_id uuid REFERENCES ledger.lots,
    details jsonb NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE ledger.postings (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id uuid NOT NULL REFERENCES ledger.events,
    lot_id uuid NOT NULL REFERENCES ledger.lots,
    location_id uuid NOT NULL REFERENCES ledger.locations,
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg<>0),
    reverses_posting_id uuid UNIQUE REFERENCES ledger.postings
);
CREATE TABLE ledger.contributions (
    lot_id uuid NOT NULL REFERENCES ledger.lots,
    source_lot_id uuid NOT NULL REFERENCES ledger.lots,
    quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>=0),
    PRIMARY KEY(lot_id,source_lot_id)
);
CREATE TABLE ledger.holds (
    id uuid PRIMARY KEY,
    lot_id uuid NOT NULL REFERENCES ledger.lots,
    reason text NOT NULL CHECK(length(reason)>0),
    status text NOT NULL DEFAULT 'ACTIVE',
    evidence_id uuid REFERENCES ledger.evidence,
    response text,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE ledger.corrections (
    id uuid PRIMARY KEY,
    operation_id uuid NOT NULL UNIQUE REFERENCES ledger.operations,
    reason text NOT NULL CHECK(length(reason)>0),
    status text NOT NULL DEFAULT 'REQUESTED' CHECK(status IN ('REQUESTED','RECONCILED','CANCELLED','COMMITTED')),
    evidence_id uuid REFERENCES ledger.evidence,
    reconciliation jsonb,
    replacement_operation_id uuid UNIQUE REFERENCES ledger.operations
);
CREATE TABLE ledger.receiving_blocks (
    discrepancy_id uuid PRIMARY KEY,
    lot_id uuid NOT NULL REFERENCES ledger.lots,
    blocked boolean NOT NULL,
    evidence_id uuid NOT NULL REFERENCES ledger.evidence,
    reason text NOT NULL,
    real_difference boolean NOT NULL DEFAULT false
);
CREATE TABLE ledger.receipt_reversals (
    receipt_id uuid PRIMARY KEY,
    correction_id uuid NOT NULL UNIQUE,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    evidence_id uuid NOT NULL REFERENCES ledger.evidence
);
CREATE TABLE ledger.notifications (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES identity.users("Id"),
    hold_id uuid NOT NULL REFERENCES ledger.holds,
    lot_id uuid NOT NULL REFERENCES ledger.lots,
    message text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    read_at timestamptz
);
CREATE TABLE ledger.receipt_correction_blocks (
    correction_id uuid PRIMARY KEY,
    receipt_id uuid NOT NULL UNIQUE,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    committed boolean NOT NULL DEFAULT false
);

CREATE FUNCTION ledger.immutable() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    RAISE EXCEPTION 'Committed history is immutable' USING ERRCODE='23514';
END $$;
DO $$
DECLARE
    table_name text;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'evidence','policies','compatibilities','lots','events','postings','contributions','receipt_reversals'
    ] LOOP
        EXECUTE format(
            'CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.%I FOR EACH ROW EXECUTE FUNCTION ledger.immutable()',
            table_name
        );
    END LOOP;
END $$;

CREATE FUNCTION ledger.kg(v text) RETURNS numeric
LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
BEGIN
    IF v IS NULL OR v !~ '^-?[0-9]{1,14}(\.[0-9]{1,6})?$' THEN
        RAISE EXCEPTION 'Unsupported decimal precision or overflow' USING ERRCODE='23514';
    END IF;
    RETURN v::numeric(20,6);
END $$;
CREATE FUNCTION ledger.role(a uuid) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,identity AS $$
    SELECT "Role" FROM identity.users WHERE "Id"=a AND "IsActive"
$$;
CREATE FUNCTION ledger.assigned(a uuid,f uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT EXISTS(SELECT 1 FROM ledger.assignments WHERE user_id=a AND facility_id=f)
$$;
CREATE FUNCTION ledger.own(a uuid,o uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,identity AS $$
    SELECT EXISTS (
        SELECT 1 FROM identity.users WHERE "Id"=a AND "IsActive" AND "OrganizationId"=o
          AND "Role" IN ('CustomerManager','ExportStaff','PurchasingStaff')
    )
$$;
CREATE FUNCTION ledger.require_staff(a uuid,f uuid,roles text[]) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF ledger.role(a) IS NULL OR NOT (ledger.role(a)=ANY(roles)) OR NOT ledger.assigned(a,f) THEN
        RAISE EXCEPTION 'Facility or operation authority denied' USING ERRCODE='42501';
    END IF;
END $$;
CREATE FUNCTION ledger.require_evidence(a uuid,e uuid,o uuid,f uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ledger.evidence WHERE id=e AND facility_id=f AND owner_id=o) THEN
        RAISE EXCEPTION 'Evidence missing or outside owner/facility scope' USING ERRCODE='42501';
    END IF;
END $$;
CREATE FUNCTION ledger.receiving_restricted(l uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT EXISTS (
        SELECT 1 FROM ledger.receiving_blocks rb WHERE rb.blocked AND (
            rb.lot_id=l OR EXISTS (
                SELECT 1 FROM ledger.contributions c WHERE c.lot_id=l AND c.source_lot_id=rb.lot_id
            )
        )
    )
$$;
CREATE FUNCTION ledger.blocked(l uuid,ignore_correction uuid DEFAULT NULL) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT EXISTS(SELECT 1 FROM ledger.holds WHERE lot_id=l)
        OR ledger.receiving_restricted(l)
        OR EXISTS (
            SELECT 1 FROM ledger.corrections c JOIN ledger.lots x ON x.operation_id=c.operation_id
            WHERE x.id=l AND c.status IN ('REQUESTED','RECONCILED')
              AND c.id IS DISTINCT FROM ignore_correction
        )
        OR EXISTS (
            SELECT 1 FROM ledger.receipt_correction_blocks r
            JOIN ledger.lots x ON x.receipt_id=r.receipt_id WHERE x.id=l
        )
        OR EXISTS (
            SELECT 1 FROM ledger.receipt_reversals r
            JOIN ledger.lots x ON x.receipt_id=r.receipt_id WHERE x.id=l
        )
$$;

-- ponytail: transaction-wide posting lock bounds throughput; ordered per-lot/location locks
-- can replace it when measured contention requires it.
CREATE FUNCTION ledger.lock_writes() RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
    -- A fresh eligibility snapshot after the advisory wait is required.
    IF current_setting('transaction_isolation') IS DISTINCT FROM 'read committed' THEN
        RAISE EXCEPTION 'Protected stock writes require READ COMMITTED isolation' USING ERRCODE='23514';
    END IF;
    PERFORM pg_advisory_xact_lock(714928352);
END $$;
CREATE FUNCTION ledger.credit_location(l uuid,o uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE
    destination ledger.locations;
BEGIN
    SELECT * INTO STRICT destination FROM ledger.locations WHERE id=l FOR UPDATE;
    IF destination.kind='COMPARTMENT' THEN
        IF destination.owner_id IS DISTINCT FROM o
           AND (destination.owner_id IS NOT NULL OR NOT destination.cleared) THEN
            RAISE EXCEPTION 'Compartment requires empty-stock recorded clearance before owner switch'
                USING ERRCODE='23514';
        END IF;
        UPDATE ledger.locations SET owner_id=o,cleared=false WHERE id=l;
    END IF;
END $$;
CREATE FUNCTION ledger.post(e uuid,l uuid,loc uuid,q numeric,rev uuid DEFAULT NULL) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    INSERT INTO ledger.postings(event_id,lot_id,location_id,quantity_kg,reverses_posting_id)
    VALUES(e,l,loc,q,rev);
    UPDATE ledger.positions SET quantity_kg=quantity_kg+q WHERE lot_id=l AND location_id=loc;
    IF NOT FOUND THEN INSERT INTO ledger.positions VALUES(l,loc,q); END IF;
END $$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
