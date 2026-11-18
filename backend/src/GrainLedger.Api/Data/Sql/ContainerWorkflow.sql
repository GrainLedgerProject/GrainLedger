-- S2 extends existing protected contracts; no history is rewritten and no runtime grant is repaired here.
ALTER TABLE grainledger.exports_blend_plan_decisions DROP COLUMN previous_version CASCADE;
ALTER TABLE grainledger.exports_blend_plan_decisions DROP COLUMN previous_action CASCADE;
DO $$ DECLARE c record; BEGIN
 FOR c IN SELECT conname FROM pg_constraint WHERE conrelid='grainledger.exports_blend_plan_decisions'::regclass AND contype='c'
 AND pg_get_constraintdef(oid) LIKE '%version = 1%' LOOP
 EXECUTE format('ALTER TABLE grainledger.exports_blend_plan_decisions DROP CONSTRAINT %I',c.conname); END LOOP;
 FOR c IN SELECT conname FROM pg_constraint WHERE conrelid='grainledger.exports_active_blend_approvals'::regclass AND contype='c'
 AND pg_get_constraintdef(oid) LIKE '%decision_version = 1%' LOOP
 EXECUTE format('ALTER TABLE grainledger.exports_active_blend_approvals DROP CONSTRAINT %I',c.conname); END LOOP;
END $$;
ALTER TABLE grainledger.exports_blend_plan_decisions ADD CHECK(version>0 AND action IN ('APPROVED','REJECTED','REVOKED'));
ALTER TABLE grainledger.exports_active_blend_approvals ADD CHECK(decision_version>0);
CREATE TABLE grainledger.ledger_container_batches (
 operation_id uuid PRIMARY KEY REFERENCES grainledger.ledger_operations(id),
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals(id),
 output_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id), location_id uuid NOT NULL REFERENCES grainledger.ledger_locations(id),
 quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0), inputs jsonb NOT NULL,
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
INSERT INTO grainledger.ledger_container_batches(operation_id,container_id,proposal_id,output_id,location_id,quantity_kg,inputs)
 SELECT p.operation_id,p.container_id,p.proposal_id,p.output_id,p.output_location_id,
 grainledger.ledger_kg(o.content->'outputs'->0->>'quantityKg'),o.content->'inputs'
 FROM grainledger.ledger_container_production p JOIN grainledger.ledger_operations o ON o.id=p.operation_id WHERE p.output_id IS NOT NULL;
CREATE INDEX ledger_container_batches_scope ON grainledger.ledger_container_batches(container_id,proposal_id);
CREATE TABLE grainledger.ledger_container_output_claims (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals(id), lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 location_id uuid NOT NULL REFERENCES grainledger.ledger_locations(id), quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0),
 operation_id uuid NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
INSERT INTO grainledger.ledger_container_output_claims(container_id,proposal_id,lot_id,location_id,quantity_kg,operation_id)
 SELECT container_id,proposal_id,output_id,location_id,quantity_kg,operation_id FROM grainledger.ledger_container_batches;
CREATE INDEX ledger_container_output_claims_scope ON grainledger.ledger_container_output_claims(proposal_id,lot_id,location_id);
CREATE TABLE grainledger.ledger_container_scope_reviews (
 id uuid PRIMARY KEY, container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals(id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), scope text NOT NULL CHECK(scope IN ('ALL_REMAINING','OUTPUT_ONLY')),
 decision text NOT NULL CHECK(decision IN ('APPROVED','REJECTED','STALE')), reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 1 AND 1000),
 basis jsonb NOT NULL, transaction_id bigint NOT NULL DEFAULT txid_current(), recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ledger_container_scope_reviews_latest ON grainledger.ledger_container_scope_reviews(container_id,proposal_id,recorded_at DESC,id);
INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis,recorded_at)
 SELECT rev.id,t.container_id,t.proposal_id,rev.actor_id,'OUTPUT_ONLY',rev.decision,rev.reason,
 rev.assessment||jsonb_build_object('outputSnapshot',rev.snapshot),rev.recorded_at
 FROM grainledger.ledger_container_production t JOIN grainledger.ledger_container_output_reviews rev ON rev.id=t.review_id;
CREATE TABLE grainledger.ledger_container_workflow_requests (
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), operation_id uuid NOT NULL,
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id), action text NOT NULL,
 payload jsonb NOT NULL, result jsonb NOT NULL, PRIMARY KEY(actor_id,operation_id)
);
CREATE TABLE grainledger.ledger_container_handoffs (
 id uuid PRIMARY KEY, container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals(id),
 facility_id uuid NOT NULL REFERENCES grainledger.ledger_facilities(id), actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 kind text NOT NULL CHECK(kind IN ('TRANSFER','BAGGING')), content jsonb NOT NULL,
 state text NOT NULL CHECK(state IN ('DRAFT','APPROVED','COMMITTED')), version integer NOT NULL DEFAULT 1,
 reviewed_by uuid REFERENCES grainledger.identity_users("Id"), review_basis jsonb, review_reason text,
 result jsonb, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE grainledger.ledger_container_claim_transitions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), handoff_id uuid NOT NULL REFERENCES grainledger.ledger_container_handoffs(id),
 proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals(id),
 lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id), location_id uuid NOT NULL REFERENCES grainledger.ledger_locations(id),
 quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>0), purpose text NOT NULL CHECK(purpose IN ('INPUT_AUTHORIZATION','REMAINDER_HANDOFF')),
 basis jsonb NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE grainledger.ledger_container_documents (
 id uuid PRIMARY KEY, container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 document_type text NOT NULL CHECK(length(btrim(document_type)) BETWEEN 1 AND 80), version integer NOT NULL CHECK(version>0),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), filename text NOT NULL,
 content_type text NOT NULL, content bytea NOT NULL CHECK(octet_length(content) BETWEEN 1 AND 10485760),
 reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 1 AND 1000), sha256 text NOT NULL,
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(), UNIQUE(container_id,document_type,version)
);
CREATE TABLE grainledger.ledger_container_supplements (
 id uuid PRIMARY KEY, container_id uuid NOT NULL REFERENCES grainledger.ledger_container_closures(container_id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), evidence_id uuid NOT NULL REFERENCES grainledger.ledger_evidence(id),
 reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 1 AND 1000), recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE grainledger.ledger_container_notices (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), hold_id uuid NOT NULL REFERENCES grainledger.ledger_container_holds(id),
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id), recipient_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 message text NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(), read_at timestamptz,
 UNIQUE(hold_id,recipient_id)
);
CREATE TABLE grainledger.ledger_container_hold_responses (
 id uuid PRIMARY KEY, hold_id uuid NOT NULL REFERENCES grainledger.ledger_container_holds(id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), facility_id uuid NOT NULL REFERENCES grainledger.ledger_facilities(id),
 action text NOT NULL CHECK(action IN ('ACKNOWLEDGE','ISOLATE','RECORD_RESPONSE')),
 evidence_id uuid NOT NULL REFERENCES grainledger.ledger_evidence(id), reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 1 AND 1000),
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
DO $$ DECLARE n text; BEGIN
 FOREACH n IN ARRAY ARRAY['ledger_container_batches','ledger_container_output_claims','ledger_container_scope_reviews',
 'ledger_container_workflow_requests','ledger_container_claim_transitions','ledger_container_documents','ledger_container_supplements','ledger_container_hold_responses'] LOOP
 EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON grainledger.%I FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable()',n||'_immutable',n);
 END LOOP;
END $$;

CREATE FUNCTION grainledger.ledger_container_outstanding(proposal uuid,lot uuid,location uuid) RETURNS numeric
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT r.quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END)
 FROM grainledger.ledger_blend_reservation_events e WHERE (e.proposal_id,e.lot_id,e.location_id)=(r.proposal_id,r.lot_id,r.location_id)),0)
 FROM grainledger.ledger_blend_reservations r WHERE (r.proposal_id,r.lot_id,r.location_id)=($1,$2,$3)
$$;
CREATE FUNCTION grainledger.ledger_container_set_reservation(a uuid,proposal uuid,lot uuid,location uuid,desired numeric) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE current_mass numeric;
BEGIN
 IF desired<0 OR desired<>round(desired,6) THEN RAISE EXCEPTION 'Invalid desired reservation' USING ERRCODE='22023'; END IF;
 SELECT grainledger.ledger_container_outstanding(proposal,lot,location) INTO current_mass;
 IF current_mass IS NULL THEN
  IF desired>0 THEN INSERT INTO grainledger.ledger_blend_reservations VALUES(proposal,lot,location,desired); END IF;
 ELSE
  IF current_mass<>desired THEN INSERT INTO grainledger.ledger_blend_reservation_events VALUES(gen_random_uuid(),proposal,lot,location,
   CASE WHEN desired>current_mass THEN 'RESERVE' ELSE 'RELEASE' END,abs(desired-current_mass),a,clock_timestamp()); END IF;
 END IF;
END $$;
CREATE FUNCTION grainledger.ledger_container_unplanned_kg(target uuid) RETURNS numeric
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT c.target_mass_kg-coalesce((SELECT sum(b.quantity_kg) FROM grainledger.ledger_container_batches b WHERE b.container_id=c.id),0)
 -coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
 JOIN grainledger.exports_blend_plan_proposals p ON p.id=h.proposal_id WHERE h.container_id=c.id AND p.snapshot_json->'selection'->>'mode'='DIRECT_STOCK'),0)
 FROM grainledger.exports_containers c WHERE c.id=$1
$$;
CREATE OR REPLACE FUNCTION grainledger.exports_protect_plan_draft() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Plan history cannot be deleted' USING ERRCODE='23514';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('export:' || replace(NEW.company_id::text, '-', ''), 0));
    IF TG_OP = 'UPDATE' THEN
        IF OLD.status <> 'DRAFT' OR NEW.status <> 'CANCELLED' OR NEW.version <> OLD.version + 1
            OR (to_jsonb(NEW) - ARRAY['status','version','cancelled_by','cancelled_at'])
               IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['status','version','cancelled_by','cancelled_at']) THEN
            RAISE EXCEPTION 'Plan demand is immutable; only cancellation is supported' USING ERRCODE='23514';
        END IF;
    ELSE
        IF NEW.status <> 'DRAFT' OR NEW.version <> 1 OR NOT EXISTS (
            SELECT 1 FROM grainledger.exports_containers c
            JOIN grainledger.exports_export_orders o ON o.id=c.order_id
            JOIN grainledger.exports_order_specifications s ON s.order_id=o.id AND s.version_no=o.current_specification_version
            WHERE c.id=NEW.container_id AND c.company_id=NEW.company_id AND o.id=NEW.order_id
              AND s.id=NEW.specification_id AND c.status<>'CLOSED' AND NEW.target_mass_kg>0 AND grainledger.ledger_container_unplanned_kg(c.id)=NEW.target_mass_kg
        ) THEN
            RAISE EXCEPTION 'Plan must reference current container demand' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END; $$;

CREATE FUNCTION grainledger.ledger_container_review_context(proposal uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 WITH prop AS (SELECT p.*,plan.container_id,v.request_json,plan.target_mass_kg FROM grainledger.exports_blend_plan_proposals p
 JOIN grainledger.exports_blend_plan_previews v ON v.id=p.preview_id JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id WHERE p.id=$1),
 original AS (SELECT (i->>'lotId')::uuid lot_id,(i->>'locationId')::uuid location_id,grainledger.ledger_kg(i->>'quantityKg') quantity_kg
 FROM prop,jsonb_array_elements(snapshot_json->'selection'->'allocations') i),
 claims AS (SELECT lot_id,location_id,sum(quantity_kg) quantity_kg FROM (
 SELECT lot_id,location_id,quantity_kg FROM original WHERE (SELECT snapshot_json->'selection'->>'mode' FROM prop)='DIRECT_STOCK'
 UNION ALL SELECT lot_id,location_id,quantity_kg FROM grainledger.ledger_container_output_claims WHERE proposal_id=$1) q GROUP BY lot_id,location_id),
 balances AS (SELECT c.*,
 coalesce((SELECT sum(e.quantity_kg) FROM grainledger.ledger_blend_reservation_events e WHERE (e.proposal_id,e.lot_id,e.location_id)=($1,c.lot_id,c.location_id) AND e.action='CONSUME'),0) spent,
 coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
 WHERE h.proposal_id=$1 AND l.lot_id=c.lot_id AND l.location_id=c.location_id),0) loaded,
 coalesce((SELECT sum(t.quantity_kg) FROM grainledger.ledger_container_claim_transitions t WHERE (t.proposal_id,t.lot_id,t.location_id)=($1,c.lot_id,c.location_id) AND t.purpose='REMAINDER_HANDOFF'),0) moved FROM claims c),
 budgeted AS (SELECT b.*,CASE WHEN (SELECT snapshot_json->'selection'->>'mode' FROM prop)='DIRECT_STOCK' THEN
 least(b.quantity_kg-b.spent-b.moved,greatest(0,grainledger.ledger_container_unplanned_kg((SELECT container_id FROM prop))-
 coalesce(sum(b.quantity_kg-b.spent-b.moved) OVER(ORDER BY b.lot_id,b.location_id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0)))
 ELSE b.quantity_kg-b.spent-b.moved END remaining FROM balances b)
 SELECT jsonb_build_object('proposalId',p.id,'containerId',p.container_id,'facilityId',(p.request_json->>'FacilityId')::uuid,
 'mode',p.snapshot_json->'selection'->>'mode','compatibilityPolicyId',p.snapshot_json->'selection'->'compatibilityPolicyId','targetKg',p.target_mass_kg::text,
 'inputs',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',o.lot_id,'locationId',o.location_id,'lotCode',(SELECT code FROM grainledger.ledger_lots WHERE id=o.lot_id),'quantityKg',
 (o.quantity_kg-coalesce((SELECT sum(e.quantity_kg) FROM grainledger.ledger_blend_reservation_events e WHERE (e.proposal_id,e.lot_id,e.location_id)=($1,o.lot_id,o.location_id) AND e.action='CONSUME'),0))::numeric(20,6)::text))
 FROM original o WHERE p.snapshot_json->'selection'->>'mode'='BLEND_INPUT_STOCK'),'[]'::jsonb),
 'outputs',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',b.lot_id,'locationId',b.location_id,'lotCode',(SELECT code FROM grainledger.ledger_lots WHERE id=b.lot_id),
 'locationName',(SELECT name FROM grainledger.ledger_locations WHERE id=b.location_id),'poolRevision',(SELECT revision FROM grainledger.ledger_mixed_bulk_pools WHERE lot_id=b.lot_id AND location_id=b.location_id),
 'quantityKg',(b.remaining+b.loaded)::numeric(20,6)::text,
 'remainingKg',b.remaining::numeric(20,6)::text,'loadedKg',b.loaded::numeric(20,6)::text) ORDER BY b.lot_id,b.location_id) FROM budgeted b
 WHERE b.remaining+b.loaded>0),'[]'::jsonb),
 'remainingProductionKg',(p.target_mass_kg-coalesce((SELECT sum(quantity_kg) FROM grainledger.ledger_container_batches WHERE proposal_id=$1),0))::numeric(20,6)::text)
 FROM prop p
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_protect_production_scope() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF TG_TABLE_NAME='exports_active_blend_approvals' THEN
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_batches WHERE proposal_id=OLD.proposal_id)
   AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_scope_reviews r WHERE r.container_id=OLD.container_id AND r.transaction_id=txid_current()) THEN
   RAISE EXCEPTION 'Produced authority requires a retained scope review transition' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='exports_containers' THEN
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_batches WHERE container_id=OLD.id)
   AND (NEW.target_mass_kg<>OLD.target_mass_kg OR NEW.order_id<>OLD.order_id) THEN
   RAISE EXCEPTION 'Produced demand cannot be changed' USING ERRCODE='23514'; END IF;
 ELSE
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_batches b JOIN grainledger.exports_containers c ON c.id=b.container_id WHERE c.order_id=OLD.id) THEN
   IF NOT (NEW.current_specification_version=OLD.current_specification_version+1 AND NEW.version=OLD.version+1
    AND NEW.required_mass_kg=OLD.required_mass_kg AND NEW.market_id=OLD.market_id AND NEW.commodity_id=OLD.commodity_id
    AND NEW.shipping_from=OLD.shipping_from AND NEW.shipping_to=OLD.shipping_to) THEN
    RAISE EXCEPTION 'Produced order header is immutable; use a versioned specification review' USING ERRCODE='23514'; END IF;
  END IF;
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_container_scope_sources(lot uuid,location uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE physical numeric; roots integer;
BEGIN
 SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=$1 AND location_id=$2;
 IF physical IS NULL THEN RAISE EXCEPTION 'Missing physical stock position' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=$1 AND location_id=$2) THEN
  SELECT count(*) INTO roots FROM grainledger.ledger_contributions WHERE lot_id=$1 AND quantity_kg>0;
  IF roots=0 OR roots>10000 OR (roots>1 AND ((SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=$1)<>physical
    OR (SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=$1 AND quantity_kg>0)<>1)) THEN
   RAISE EXCEPTION 'Position source complement is not proven' USING ERRCODE='23514'; END IF;
  INSERT INTO grainledger.ledger_container_source_balances SELECT $1,$2,source_lot_id,CASE WHEN roots=1 THEN physical ELSE quantity_kg END
   FROM grainledger.ledger_contributions WHERE lot_id=$1 AND quantity_kg>0;
 END IF;
 IF (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=$1 AND location_id=$2) IS DISTINCT FROM physical THEN
  RAISE EXCEPTION 'Retained source complement differs from physical stock' USING ERRCODE='23514'; END IF;
END $$;

CREATE FUNCTION grainledger.ledger_container_workflow(a uuid,target uuid,action text,p jsonb DEFAULT '{}'::jsonb,basis jsonb DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; prop grainledger.exports_blend_plan_proposals; plan grainledger.exports_blend_plan_drafts;
 role_name text; facility uuid; operation uuid; prior grainledger.ledger_container_workflow_requests;
 review grainledger.ledger_container_scope_reviews; ctx jsonb; item jsonb; result jsonb; prev uuid; decision integer;
 qty numeric; own_mass numeric; physical numeric; remaining_mass numeric; source uuid; destination uuid; output uuid;
 handoff grainledger.ledger_container_handoffs; content jsonb; actual jsonb; source_lot grainledger.ledger_lots; shares jsonb;
 revision bigint; source_pool uuid; recipient_count integer; hold uuid;
BEGIN
 IF action NOT IN ('context','detail','document-file','notifications','assert-basis','planning','work-items','handoff-quality') THEN PERFORM grainledger.ledger_lock_writes(); END IF;
 role_name:=grainledger.ledger_role(a);
 IF role_name IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
 IF action='work-items' THEN
  IF role_name NOT IN ('MillOperator','MillManager') THEN RAISE EXCEPTION 'Assigned mill actor required' USING ERRCODE='42501'; END IF;
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',x.id,'code',x.internal_code,'proposalId',q.id,
   'facilityId',(v.request_json->>'FacilityId')::uuid,'mode',q.snapshot_json->'selection'->>'mode','state',coalesce(t.state,'HANDOFF_AVAILABLE')) ORDER BY x.internal_code)
   FROM grainledger.exports_active_blend_approvals active JOIN grainledger.exports_containers x ON x.id=active.container_id
   JOIN grainledger.exports_blend_plan_proposals q ON q.id=active.proposal_id JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
   LEFT JOIN grainledger.ledger_container_production t ON t.container_id=x.id AND t.proposal_id=q.id
   WHERE x.status<>'CLOSED' AND grainledger.ledger_assigned(a,(v.request_json->>'FacilityId')::uuid)),'[]'::jsonb));
 END IF;
 IF action='notifications' THEN
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('id',n.id,'holdId',n.hold_id,'containerId',n.container_id,
   'code',x.internal_code,'message',n.message,'createdAt',n.created_at,'readAt',n.read_at) ORDER BY n.created_at DESC,n.id)
   FROM grainledger.ledger_container_notices n JOIN grainledger.exports_containers x ON x.id=n.container_id
   WHERE n.recipient_id=a AND (role_name IN ('CustomerManager','ExportStaff') AND grainledger.ledger_own(a,x.company_id)
    OR role_name='MillManager' AND EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
     WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=x.id) AND grainledger.ledger_assigned(a,(v.request_json->>'FacilityId')::uuid)))),'[]'::jsonb));
 END IF;
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 IF c.id IS NULL OR NOT (role_name IN ('CustomerManager','ExportStaff') AND grainledger.ledger_own(a,c.company_id)
  OR role_name IN ('MillOperator','MillManager') AND EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q
   JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=target) AND grainledger.ledger_assigned(a,(v.request_json->>'FacilityId')::uuid))) THEN
  RAISE EXCEPTION 'Container workflow unavailable in current scope' USING ERRCODE='42501'; END IF;
 IF action='planning' THEN RETURN jsonb_build_object('unplannedKg',grainledger.ledger_container_unplanned_kg(target)::numeric(20,6)::text,
  'orderStarted',EXISTS(SELECT 1 FROM grainledger.exports_containers x WHERE x.order_id=c.order_id AND (x.status<>'PLANNED'
   OR EXISTS(SELECT 1 FROM grainledger.ledger_container_batches b WHERE b.container_id=x.id)))); END IF;
 IF action='assert-basis' THEN
  PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id); RETURN jsonb_build_object('valid',true);
 END IF;
 IF action='document-file' THEN
  IF role_name NOT IN ('CustomerManager','ExportStaff') THEN RAISE EXCEPTION 'Customer document access required' USING ERRCODE='42501'; END IF;
  SELECT jsonb_build_object('filename',d.filename,'contentType',d.content_type,'content',encode(d.content,'base64'),'sha256',d.sha256) INTO result
   FROM grainledger.ledger_container_documents d WHERE d.id=(p->>'id')::uuid AND d.container_id=target;
  IF result IS NULL THEN RAISE EXCEPTION 'Document unavailable' USING ERRCODE='42501'; END IF; RETURN result;
 END IF;
 IF action='context' THEN
  SELECT * INTO prop FROM grainledger.exports_blend_plan_proposals WHERE id=(p->>'proposalId')::uuid AND plan_id IN (SELECT id FROM grainledger.exports_blend_plan_drafts WHERE container_id=target) AND company_id=c.company_id;
  IF prop.id IS NULL OR role_name NOT IN ('CustomerManager','ExportStaff') THEN RAISE EXCEPTION 'Own-company proposal required' USING ERRCODE='42501'; END IF;
  RETURN grainledger.ledger_container_review_context(prop.id);
 END IF;
 -- Internal scientific input for an existing scoped handoff; never exposed by an HTTP read route.
 IF action='handoff-quality' THEN
  SELECT * INTO handoff FROM grainledger.ledger_container_handoffs h WHERE h.id=(p->>'handoffId')::uuid AND h.container_id=target;
  IF handoff.id IS NULL OR role_name NOT IN ('CustomerManager','MillOperator') THEN RAISE EXCEPTION 'Handoff assessment denied' USING ERRCODE='42501'; END IF;
  IF role_name='MillOperator' THEN PERFORM grainledger.ledger_require_staff(a,handoff.facility_id,ARRAY['MillOperator']); END IF;
  RETURN coalesce((SELECT jsonb_agg(to_jsonb(q) ORDER BY q.lot_id,q.attribute_id) FROM (
   SELECT DISTINCT ON (q.lot_id,q.attribute_id) q.id,q.lot_id,q.attribute_id,q.version_no,q.measured_value::text,q.unit
   FROM grainledger.ledger_planning_quality_observations q WHERE q.owner_id=c.company_id AND q.facility_id=handoff.facility_id
   AND q.lot_id=(handoff.content->>'lotId')::uuid AND q.confirmed_at<=(p->>'at')::timestamptz
   ORDER BY q.lot_id,q.attribute_id,q.version_no DESC,q.id) q),'[]'::jsonb);
 END IF;
 IF action='detail' THEN
  RETURN jsonb_build_object('containerId',target,'ownerId',c.company_id,'status',c.status,'version',c.version,
   'reviews',CASE WHEN role_name IN ('CustomerManager','ExportStaff') THEN coalesce((SELECT jsonb_agg(jsonb_build_object('id',r.id,'proposalId',r.proposal_id,
    'scope',r.scope,'decision',r.decision,'reason',r.reason,'recordedAt',r.recorded_at) ORDER BY r.recorded_at,r.id) FROM grainledger.ledger_container_scope_reviews r WHERE r.container_id=target),'[]'::jsonb) ELSE '[]'::jsonb END,
   'proposals',coalesce((SELECT jsonb_agg(jsonb_build_object('id',q.id,'planId',q.plan_id,'version',d.version_no,
    'context',grainledger.ledger_container_review_context(q.id)) ORDER BY d.version_no) FROM grainledger.exports_blend_plan_proposals q
    JOIN grainledger.exports_blend_plan_drafts d ON d.id=q.plan_id WHERE d.container_id=target
    AND (role_name IN ('CustomerManager','ExportStaff') OR grainledger.ledger_assigned(a,(SELECT (v.request_json->>'FacilityId')::uuid FROM grainledger.exports_blend_plan_previews v WHERE v.id=q.preview_id)))),'[]'::jsonb),
   'handoffs',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'proposalId',h.proposal_id,'facilityId',h.facility_id,'kind',h.kind,'state',h.state,'version',h.version,'content',h.content)
    ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_handoffs h WHERE h.container_id=target
    AND (role_name IN ('CustomerManager','ExportStaff') OR grainledger.ledger_assigned(a,h.facility_id))),'[]'::jsonb),
   'documents',CASE WHEN role_name IN ('CustomerManager','ExportStaff') THEN coalesce((SELECT jsonb_agg(jsonb_build_object('id',d.id,'documentType',d.document_type,
    'version',d.version,'filename',d.filename,'contentType',d.content_type,'sha256',d.sha256,'reason',d.reason,'actorId',d.actor_id,'recordedAt',d.recorded_at)
    ORDER BY d.document_type,d.version) FROM grainledger.ledger_container_documents d WHERE d.container_id=target),'[]'::jsonb) ELSE '[]'::jsonb END,
   'supplements',CASE WHEN role_name IN ('CustomerManager','ExportStaff') THEN coalesce((SELECT jsonb_agg(jsonb_build_object('id',s.id,'evidenceId',s.evidence_id,
    'reason',s.reason,'actorId',s.actor_id,'recordedAt',s.recorded_at) ORDER BY s.recorded_at,s.id) FROM grainledger.ledger_container_supplements s WHERE s.container_id=target),'[]'::jsonb) ELSE '[]'::jsonb END,
   'holds',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'reason',h.reason,'recordedAt',h.recorded_at)) FROM grainledger.ledger_container_holds h WHERE h.container_id=target),'[]'::jsonb),
   'holdResponses',coalesce((SELECT jsonb_agg(jsonb_build_object('id',r.id,'holdId',r.hold_id,'action',r.action,'reason',r.reason,'evidenceId',r.evidence_id,'recordedAt',r.recorded_at)
    ORDER BY r.recorded_at,r.id) FROM grainledger.ledger_container_hold_responses r WHERE r.hold_id IN (SELECT id FROM grainledger.ledger_container_holds WHERE container_id=target)),'[]'::jsonb));
 END IF;
 PERFORM grainledger.ledger_lock_writes(); SELECT * INTO c FROM grainledger.exports_containers WHERE id=target FOR UPDATE;
 operation:=(p->>'operationId')::uuid;
 IF operation IS NULL OR operation='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable operation ID required' USING ERRCODE='22023'; END IF;
 IF (action IN ('review','handoff-approve') AND role_name<>'CustomerManager')
  OR (action IN ('handoff-draft','handoff-commit') AND role_name<>'MillOperator')
  OR (action IN ('document','supplement') AND role_name<>'ExportStaff')
  OR (action='hold-response' AND role_name<>'MillManager') THEN RAISE EXCEPTION 'Workflow action authority denied' USING ERRCODE='42501'; END IF;
 SELECT * INTO prior FROM grainledger.ledger_container_workflow_requests WHERE actor_id=a AND operation_id=operation;
 IF FOUND THEN
  IF prior.container_id<>target OR prior.action IS DISTINCT FROM coalesce(p->>'replayAction',action) OR prior.payload IS DISTINCT FROM (p-'replayAction') THEN
   RAISE EXCEPTION 'Workflow operation content conflict' USING ERRCODE='23514'; END IF;
  IF prior.action IN ('handoff-draft','handoff-commit') THEN
   SELECT facility_id INTO facility FROM grainledger.ledger_container_handoffs WHERE id=CASE WHEN prior.action='handoff-draft' THEN prior.operation_id ELSE (prior.payload->>'handoffId')::uuid END;
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
  ELSIF prior.action='hold-response' THEN PERFORM grainledger.ledger_require_staff(a,(prior.payload->>'facilityId')::uuid,ARRAY['MillManager']); END IF;
  RETURN prior.result;
 END IF;
 IF action='replay' THEN RETURN jsonb_build_object('replayMissing',true); END IF;
 IF c.version IS DISTINCT FROM (p->>'version')::integer THEN RAISE EXCEPTION 'Container version changed; reload' USING ERRCODE='23514'; END IF;
 IF action IN ('review','handoff-draft','handoff-approve','handoff-commit') AND (c.status='CLOSED'
  OR EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target)) THEN RAISE EXCEPTION 'Closed or held container cannot execute/review' USING ERRCODE='23514'; END IF;
 IF action='invalidate' THEN
  IF role_name NOT IN ('CustomerManager','ExportStaff','MillOperator','MillManager') OR basis->>'assessmentStatus' IS DISTINCT FROM 'INCOMPLETE' THEN RAISE EXCEPTION 'Scoped current stale basis required' USING ERRCODE='42501'; END IF;
  SELECT proposal_id INTO prev FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  IF prev IS NULL THEN RETURN grainledger.ledger_container_workflow(a,target,'detail'); END IF;
  IF prev IS NOT NULL THEN
   INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis)
    VALUES(operation,target,prev,a,'ALL_REMAINING','STALE','Current evidence or restriction invalidated unfinished authority',basis);
   FOR item IN SELECT jsonb_build_object('lotId',r.lot_id,'locationId',r.location_id) FROM grainledger.ledger_blend_reservations r WHERE r.proposal_id=prev LOOP
    PERFORM grainledger.ledger_container_set_reservation(a,prev,(item->>'lotId')::uuid,(item->>'locationId')::uuid,0);
   END LOOP;
   DELETE FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  END IF;
 ELSIF action='review' THEN
  IF p->>'scope' NOT IN ('ALL_REMAINING','OUTPUT_ONLY') OR p->>'decision' NOT IN ('APPROVED','REJECTED') OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN
   RAISE EXCEPTION 'Review scope, decision and reason required' USING ERRCODE='22023'; END IF;
  SELECT * INTO prop FROM grainledger.exports_blend_plan_proposals WHERE id=(p->>'proposalId')::uuid AND plan_id IN (SELECT id FROM grainledger.exports_blend_plan_drafts WHERE container_id=target) AND company_id=c.company_id;
  IF prop.id IS NULL THEN RAISE EXCEPTION 'Own-company proposal required' USING ERRCODE='42501'; END IF;
  SELECT * INTO plan FROM grainledger.exports_blend_plan_drafts WHERE id=prop.plan_id;
  ctx:=grainledger.ledger_container_review_context(prop.id); facility:=(ctx->>'facilityId')::uuid;
  IF p->>'decision'='APPROVED' THEN
   PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
   IF basis->'context' IS DISTINCT FROM ctx OR basis->>'scope' IS DISTINCT FROM p->>'scope' THEN RAISE EXCEPTION 'Review scope changed' USING ERRCODE='23514'; END IF;
   IF p->>'scope'='ALL_REMAINING' AND jsonb_array_length(ctx->'inputs')>0 AND grainledger.ledger_kg(ctx->>'remainingProductionKg')>grainledger.ledger_container_unplanned_kg(target) THEN
    RAISE EXCEPTION 'Another execution already produced the planned remaining demand' USING ERRCODE='23514'; END IF;
   IF p->>'scope'='OUTPUT_ONLY' AND jsonb_array_length(ctx->'outputs')=0 THEN RAISE EXCEPTION 'OUTPUT_ONLY requires existing output' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements((ctx->'inputs')||(ctx->'outputs')) i JOIN grainledger.ledger_container_output_claims claimed
    ON claimed.lot_id=(i->>'lotId')::uuid AND claimed.container_id=target AND claimed.proposal_id<>prop.id) THEN
    RAISE EXCEPTION 'Previously produced output keeps its original plan; re-review that plan OUTPUT_ONLY' USING ERRCODE='23514'; END IF;
  END IF;
  INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis)
   VALUES(operation,target,prop.id,a,p->>'scope',p->>'decision',btrim(p->>'reason'),coalesce(basis,'{}'::jsonb));
  SELECT proposal_id INTO prev FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  -- Freeze the old permission and release only its unspent reservations. Immutable loads retain their original plan.
  IF prev IS NOT NULL AND (p->>'decision'='APPROVED' OR prev=prop.id) THEN
   FOR item IN SELECT jsonb_build_object('lotId',r.lot_id,'locationId',r.location_id) FROM grainledger.ledger_blend_reservations r WHERE r.proposal_id=prev LOOP
    PERFORM grainledger.ledger_container_set_reservation(a,prev,(item->>'lotId')::uuid,(item->>'locationId')::uuid,0);
   END LOOP;
   DELETE FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  END IF;
  SELECT coalesce(max(version),0)+1 INTO decision FROM grainledger.exports_blend_plan_decisions WHERE proposal_id=prop.id;
  INSERT INTO grainledger.exports_blend_plan_decisions(proposal_id,version,company_id,container_id,plan_id,action,actor_id,recorded_at,reason)
   VALUES(prop.id,decision,c.company_id,target,plan.id,p->>'decision',a,clock_timestamp(),CASE WHEN p->>'decision'='REJECTED' THEN btrim(p->>'reason') END);
  IF p->>'decision'='APPROVED' THEN
   INSERT INTO grainledger.exports_active_blend_approvals(company_id,container_id,proposal_id,decision_version) VALUES(c.company_id,target,prop.id,decision);
   FOR item IN SELECT x FROM jsonb_array_elements(CASE WHEN p->>'scope'='ALL_REMAINING' THEN ctx->'inputs' ELSE '[]'::jsonb END) x
    UNION ALL SELECT x||jsonb_build_object('quantityKg',x->>'remainingKg') FROM jsonb_array_elements(ctx->'outputs') x LOOP
    qty:=grainledger.ledger_kg(item->>'quantityKg');
    IF qty>0 THEN
     IF grainledger.ledger_container_lot_blocked((item->>'lotId')::uuid) OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions pos
      JOIN grainledger.ledger_lots lot ON lot.id=pos.lot_id JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
      WHERE pos.lot_id=(item->>'lotId')::uuid AND pos.location_id=(item->>'locationId')::uuid AND lot.owner_id=c.company_id AND loc.facility_id=facility
       AND NOT loc.is_deleted AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)
       AND (NOT loc.mixed_bulk OR EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=pos.lot_id AND pool.location_id=loc.id))
       AND pos.quantity_kg-grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)>=qty) THEN
      RAISE EXCEPTION 'Reviewed remaining stock is restricted or unavailable' USING ERRCODE='23514'; END IF;
     PERFORM grainledger.ledger_container_set_reservation(a,prop.id,(item->>'lotId')::uuid,(item->>'locationId')::uuid,qty);
    END IF;
   END LOOP;
  END IF;
 ELSIF action='handoff-draft' THEN
  SELECT * INTO prop FROM grainledger.exports_blend_plan_proposals WHERE id=(p->>'proposalId')::uuid AND plan_id IN (SELECT id FROM grainledger.exports_blend_plan_drafts WHERE container_id=target) AND company_id=c.company_id;
  ctx:=grainledger.ledger_container_review_context(prop.id); facility:=(ctx->>'facilityId')::uuid;
  PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
  IF NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals WHERE proposal_id=prop.id) THEN RAISE EXCEPTION 'Active plan required for handoff' USING ERRCODE='23514'; END IF;
  content:=p->'content'; qty:=grainledger.ledger_kg(content->>'quantityKg'); source:=(content->>'fromLocationId')::uuid; destination:=(content->>'toLocationId')::uuid;
  IF p->>'kind' NOT IN ('TRANSFER','BAGGING') OR qty<=0 OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(ctx->'outputs') i
   WHERE i->>'lotId'=content->>'lotId' AND (i->>'locationId')::uuid=source AND grainledger.ledger_kg(i->>'remainingKg')>=qty) THEN
   RAISE EXCEPTION 'Handoff must name current output and remaining quota' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations l WHERE l.id=destination AND l.facility_id=facility AND NOT l.is_deleted AND (l.owner_id IS NULL OR l.owner_id=c.company_id)) THEN
   RAISE EXCEPTION 'Handoff destination outside scope' USING ERRCODE='42501'; END IF;
  IF p->>'kind'='TRANSFER' AND source=destination THEN RAISE EXCEPTION 'Transfer requires a different destination' USING ERRCODE='22023'; END IF;
  IF p->>'kind'='BAGGING' THEN
   SELECT * INTO source_lot FROM grainledger.ledger_lots WHERE id=(content->>'lotId')::uuid;
   IF grainledger.ledger_kg(content->>'outputKg')<>qty OR grainledger.ledger_kg(content->>'declaredLossKg')<>0 THEN RAISE EXCEPTION 'Quantity exception is outside reviewed handoff scope' USING ERRCODE='23514'; END IF;
   actual:=grainledger.ledger_command(a,'draft',jsonb_build_object('id',operation,'ownerId',c.company_id,'facilityId',facility,'kind','BAGGING',
    'equipmentId',content->>'equipmentId','occurredAt',content->>'occurredAt','declaredLossKg',content->>'declaredLossKg',
    'inputs',jsonb_build_array(jsonb_build_object('lotId',content->>'lotId','locationId',source,'quantityKg',content->>'quantityKg')
     ||CASE WHEN content ? 'poolRevision' AND content->'poolRevision'<>'null'::jsonb THEN jsonb_build_object('poolRevision',content->'poolRevision') ELSE '{}'::jsonb END),
    'outputs',jsonb_build_array(jsonb_build_object('id',gen_random_uuid(),'code','','commodityId',source_lot.commodity_id,'stage','BAGGED_RICE',
     'productIdentity',source_lot.product_identity,'locationId',destination,'quantityKg',content->>'outputKg'))));
   content:=content||jsonb_build_object('processingOperationId',operation);
  END IF;
  INSERT INTO grainledger.ledger_container_handoffs(id,container_id,proposal_id,facility_id,actor_id,kind,content,state)
   VALUES(operation,target,prop.id,facility,a,p->>'kind',content,'DRAFT');
 ELSIF action IN ('handoff-approve','handoff-commit') THEN
  SELECT * INTO handoff FROM grainledger.ledger_container_handoffs WHERE id=(p->>'handoffId')::uuid AND container_id=target FOR UPDATE;
  IF handoff.id IS NULL OR handoff.version IS DISTINCT FROM (p->>'handoffVersion')::integer THEN RAISE EXCEPTION 'Handoff version changed' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals WHERE proposal_id=handoff.proposal_id) THEN RAISE EXCEPTION 'Handoff plan no longer active' USING ERRCODE='23514'; END IF;
  content:=handoff.content; facility:=handoff.facility_id; source:=(content->>'fromLocationId')::uuid; destination:=(content->>'toLocationId')::uuid;
  qty:=grainledger.ledger_kg(content->>'quantityKg'); SELECT * INTO source_lot FROM grainledger.ledger_lots WHERE id=(content->>'lotId')::uuid;
  IF action='handoff-approve' THEN
   IF handoff.state<>'DRAFT' OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'Draft handoff and review reason required' USING ERRCODE='23514'; END IF;
   PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
   UPDATE grainledger.ledger_container_handoffs SET state='APPROVED',reviewed_by=a,review_basis=basis,review_reason=btrim(p->>'reason'),version=version+1 WHERE id=handoff.id;
  ELSE
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
   IF handoff.state<>'APPROVED' OR grainledger.ledger_container_lot_blocked(source_lot.id) THEN RAISE EXCEPTION 'Current unrestricted approved handoff required' USING ERRCODE='23514'; END IF;
   PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
   IF basis->>'dependencyRevision' IS DISTINCT FROM handoff.review_basis->>'dependencyRevision' THEN RAISE EXCEPTION 'Handoff review evidence changed' USING ERRCODE='23514'; END IF;
   own_mass:=grainledger.ledger_container_outstanding(handoff.proposal_id,source_lot.id,source);
   SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=source_lot.id AND location_id=source FOR UPDATE;
   IF own_mass IS NULL OR own_mass<qty OR physical<qty THEN RAISE EXCEPTION 'Handoff exceeds reservation or physical stock' USING ERRCODE='23514'; END IF;
   IF grainledger.ledger_mixed_bulk_custody_derived(source_lot.id) OR (SELECT mixed_bulk FROM grainledger.ledger_locations WHERE id=source) THEN
    IF grainledger.ledger_blend_reserved_kg(source_lot.id,source)<>own_mass THEN RAISE EXCEPTION 'Other-plan pool reservations require a separate reviewed handoff' USING ERRCODE='23514'; END IF;
   ELSE
    PERFORM grainledger.ledger_container_scope_sources(source_lot.id,source);
    SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,'quantityKg',s.quantity_kg::numeric(20,6)::text)) INTO shares
     FROM grainledger.ledger_container_allocate_sources(qty,physical,(SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
      FROM grainledger.ledger_container_source_balances WHERE lot_id=source_lot.id AND location_id=source AND quantity_kg>0)) s;
   END IF;
   INSERT INTO grainledger.ledger_container_claim_transitions(handoff_id,proposal_id,lot_id,location_id,quantity_kg,purpose,basis)
    VALUES(handoff.id,handoff.proposal_id,source_lot.id,source,qty,'INPUT_AUTHORIZATION',handoff.review_basis);
   -- Explicitly release OUTPUT and establish the reviewed INPUT authority before its single consumption.
   PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,source_lot.id,source,0);
   PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,source_lot.id,source,qty);
   INSERT INTO grainledger.ledger_blend_reservation_events VALUES(gen_random_uuid(),handoff.proposal_id,source_lot.id,source,'CONSUME',qty,a,clock_timestamp());
   IF handoff.kind='TRANSFER' THEN
    actual:=grainledger.ledger_command(a,'transfer',jsonb_build_object('id',handoff.id,'lotId',source_lot.id,'fromLocationId',source,'toLocationId',destination,
     'quantityKg',content->>'quantityKg','occurredAt',content->>'occurredAt')
     ||CASE WHEN content ? 'poolRevision' AND content->'poolRevision'<>'null'::jsonb THEN jsonb_build_object('poolRevision',content->'poolRevision') ELSE '{}'::jsonb END);
    output:=coalesce((actual->>'currentPoolLotId')::uuid,(actual->>'actualLotId')::uuid,source_lot.id);
   ELSE
    actual:=grainledger.ledger_commit_operation(a,(content->>'processingOperationId')::uuid);
    output:=coalesce((actual->'poolOutputs'->0->>'currentPoolLotId')::uuid,(actual->'outputs'->0->>'id')::uuid);
   END IF;
   IF shares IS NOT NULL THEN
    IF (SELECT coalesce(sum(quantity_kg),0) FROM grainledger.ledger_container_source_balances WHERE lot_id=source_lot.id AND location_id=source)>0 THEN
     UPDATE grainledger.ledger_container_source_balances b SET quantity_kg=b.quantity_kg-grainledger.ledger_kg(s->>'quantityKg') FROM jsonb_array_elements(shares) s
      WHERE b.lot_id=source_lot.id AND b.location_id=source AND b.source_lot_id=(s->>'sourceLotId')::uuid;
    END IF;
    IF handoff.kind='TRANSFER' AND output=source_lot.id AND NOT (SELECT mixed_bulk FROM grainledger.ledger_locations WHERE id=destination) THEN
     INSERT INTO grainledger.ledger_container_source_balances(lot_id,location_id,source_lot_id,quantity_kg)
      SELECT output,destination,(s->>'sourceLotId')::uuid,grainledger.ledger_kg(s->>'quantityKg') FROM jsonb_array_elements(shares) s
      ON CONFLICT(lot_id,location_id,source_lot_id) DO UPDATE SET quantity_kg=grainledger.ledger_container_source_balances.quantity_kg+excluded.quantity_kg;
    END IF;
   END IF;
   IF own_mass>qty THEN
    source_pool:=coalesce((actual->>'sourceCurrentPoolLotId')::uuid,
     (SELECT pool.lot_id FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.location_id=source AND pool.lot_id<>source_lot.id),
     (SELECT edge.output_lot_id FROM grainledger.ledger_custody_events ce JOIN grainledger.ledger_custody_edges edge ON edge.event_id=ce.event_id
      JOIN grainledger.ledger_positions pos ON pos.lot_id=edge.output_lot_id AND pos.location_id=source AND pos.quantity_kg>0
      WHERE ce.command_id=handoff.id AND ce.action='WITHDRAW' AND edge.input_lot_id=source_lot.id
       AND edge.output_lot_id<>ce.output_lot_id LIMIT 1),source_lot.id);
    IF source_pool<>source_lot.id THEN
     INSERT INTO grainledger.ledger_container_claim_transitions(handoff_id,proposal_id,lot_id,location_id,quantity_kg,purpose,basis)
      VALUES(handoff.id,handoff.proposal_id,source_lot.id,source,own_mass-qty,'REMAINDER_HANDOFF',handoff.review_basis);
     INSERT INTO grainledger.ledger_container_output_claims(container_id,proposal_id,lot_id,location_id,quantity_kg,operation_id)
      VALUES(target,handoff.proposal_id,source_pool,source,own_mass-qty,handoff.id);
    END IF;
    PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,source_pool,source,own_mass-qty);
   END IF;
   INSERT INTO grainledger.ledger_container_output_claims(container_id,proposal_id,lot_id,location_id,quantity_kg,operation_id)
    VALUES(target,handoff.proposal_id,output,destination,qty,handoff.id);
   PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,output,destination,coalesce(grainledger.ledger_container_outstanding(handoff.proposal_id,output,destination),0)+qty);
   UPDATE grainledger.ledger_container_handoffs SET state='COMMITTED',result=actual,version=version+1 WHERE id=handoff.id;
   -- Loading requires a new actual-output review for the resulting identity/position; no inherited PASS.
  END IF;
 ELSIF action='document' THEN
  IF c.status<>'CLOSED' OR length(btrim(coalesce(p->>'documentType',''))) NOT BETWEEN 1 AND 80 OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000
   OR length(coalesce(p->>'filename','')) NOT BETWEEN 1 AND 200 OR length(coalesce(p->>'contentType','')) NOT BETWEEN 1 AND 120 THEN
   RAISE EXCEPTION 'Closed container, document metadata and reason required' USING ERRCODE='22023'; END IF;
  SELECT coalesce(max(version),0)+1 INTO decision FROM grainledger.ledger_container_documents WHERE container_id=target AND document_type=btrim(p->>'documentType');
  INSERT INTO grainledger.ledger_container_documents(id,container_id,document_type,version,actor_id,filename,content_type,content,reason,sha256)
   VALUES(operation,target,btrim(p->>'documentType'),decision,a,p->>'filename',p->>'contentType',decode(p->>'content','base64'),btrim(p->>'reason'),p->>'sha256');
 ELSIF action='supplement' THEN
  IF c.status<>'CLOSED' OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'Closed dossier and supplement reason required' USING ERRCODE='22023'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_evidence e WHERE e.id=(p->>'evidenceId')::uuid AND e.owner_id=c.company_id AND
   (EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=target) AND (v.request_json->>'FacilityId')::uuid=e.facility_id))) THEN
   RAISE EXCEPTION 'Supplement evidence outside company/facility' USING ERRCODE='42501'; END IF;
  INSERT INTO grainledger.ledger_container_supplements(id,container_id,actor_id,evidence_id,reason) VALUES(operation,target,a,(p->>'evidenceId')::uuid,btrim(p->>'reason'));
 ELSIF action='notice-read' THEN
  UPDATE grainledger.ledger_container_notices SET read_at=coalesce(read_at,clock_timestamp()) WHERE id=(p->>'noticeId')::uuid AND recipient_id=a AND container_id=target;
  IF NOT FOUND THEN RAISE EXCEPTION 'Notice unavailable for current recipient' USING ERRCODE='42501'; END IF;
 ELSIF action='hold-response' THEN
  hold:=(p->>'holdId')::uuid; facility:=(p->>'facilityId')::uuid;
  PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
  PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,c.company_id,facility);
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE id=hold AND container_id=target)
   OR NOT EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=target) AND (v.request_json->>'FacilityId')::uuid=facility)
   OR p->>'responseAction' NOT IN ('ACKNOWLEDGE','ISOLATE','RECORD_RESPONSE') OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN
   RAISE EXCEPTION 'Scoped HOLD handling and evidence required; release unsupported' USING ERRCODE='23514'; END IF;
  IF p->>'responseAction'='ISOLATE' THEN RAISE EXCEPTION 'Container unloading/isolation movement is outside scope; use lot HOLD isolation for remaining stock' USING ERRCODE='23514'; END IF;
  INSERT INTO grainledger.ledger_container_hold_responses(id,hold_id,actor_id,facility_id,action,evidence_id,reason)
   VALUES(operation,hold,a,facility,p->>'responseAction',(p->>'evidenceId')::uuid,btrim(p->>'reason'));
 ELSE RAISE EXCEPTION 'Unsupported workflow action' USING ERRCODE='22023'; END IF;
 UPDATE grainledger.exports_containers SET version=version+1 WHERE id=target;
 INSERT INTO grainledger.ledger_container_execution_events(container_id,actor_id,kind,details)
  VALUES(target,a,upper(replace(action,'-','_')),jsonb_build_object('operationId',operation,'request',p-'content','basis',basis));
 result:=grainledger.ledger_container_workflow(a,target,'detail');
 INSERT INTO grainledger.ledger_container_workflow_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_container_hold_notify() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE company uuid;
BEGIN
 SELECT company_id INTO company FROM grainledger.exports_containers WHERE id=NEW.container_id;
 INSERT INTO grainledger.ledger_container_notices(hold_id,container_id,recipient_id,message)
 SELECT NEW.id,NEW.container_id,u."Id",NEW.reason FROM grainledger.identity_users u WHERE u."IsActive" AND
  (u."OrganizationId"=company AND u."Role" IN ('CustomerManager','ExportStaff') OR u."Role"='MillManager' AND EXISTS(
   SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
   WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=NEW.container_id) AND grainledger.ledger_assigned(u."Id",(v.request_json->>'FacilityId')::uuid))) ON CONFLICT DO NOTHING;
 IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_notices n JOIN grainledger.identity_users u ON u."Id"=n.recipient_id WHERE n.hold_id=NEW.id AND u."Role"='CustomerManager')
 OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_notices n JOIN grainledger.identity_users u ON u."Id"=n.recipient_id WHERE n.hold_id=NEW.id AND u."Role"='ExportStaff')
 OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_notices n JOIN grainledger.identity_users u ON u."Id"=n.recipient_id WHERE n.hold_id=NEW.id AND u."Role"='MillManager') THEN
  RAISE EXCEPTION 'HOLD recipients are not configured for owner and facility' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER ledger_container_hold_notify AFTER INSERT ON grainledger.ledger_container_holds FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_container_hold_notify();

-- Forward replacement of S1 production, retaining its public signature.
CREATE OR REPLACE FUNCTION grainledger.ledger_container_production(a uuid,target uuid,action text,p jsonb DEFAULT '{}'::jsonb,basis jsonb DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE task grainledger.ledger_container_production; c grainledger.exports_containers; prop grainledger.exports_blend_plan_proposals;
 role_name text:=grainledger.ledger_role(a); facility uuid; decision integer; prior grainledger.ledger_container_production_requests;
 operation uuid; item record; op grainledger.ledger_operations; content jsonb; result jsonb; output jsonb;
 compatibility grainledger.ledger_compatibilities; planned_target numeric; remaining_target numeric; batch_qty numeric; current_scope grainledger.ledger_container_scope_reviews; qty numeric; outstanding numeric; quality jsonb; yield jsonb; review uuid;
BEGIN
 IF action NOT IN ('list','detail','context') THEN PERFORM grainledger.ledger_lock_writes(); role_name:=grainledger.ledger_role(a); END IF;
 IF role_name IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
 IF action='list' THEN
  IF role_name NOT IN ('MillOperator','MillManager') THEN RAISE EXCEPTION 'Assigned mill actor required' USING ERRCODE='42501'; END IF;
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',t.container_id,'code',x.internal_code,'facilityId',t.facility_id,'state',t.state,'version',t.version))
   FROM grainledger.ledger_container_production t JOIN grainledger.exports_containers x ON x.id=t.container_id
   WHERE grainledger.ledger_assigned(a,t.facility_id) AND x.status<>'CLOSED'),'[]'::jsonb));
 END IF;
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 SELECT * INTO task FROM grainledger.ledger_container_production WHERE container_id=target;
 IF c.id IS NULL OR NOT (role_name IN ('ExportStaff','CustomerManager') AND grainledger.ledger_own(a,c.company_id)
   OR role_name IN ('MillOperator','MillManager') AND task.container_id IS NOT NULL AND grainledger.ledger_assigned(a,task.facility_id)) THEN
  RAISE EXCEPTION 'Production unavailable in current scope' USING ERRCODE='42501'; END IF;
 SELECT q.* INTO prop FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_active_blend_approvals active ON active.proposal_id=q.id
  WHERE active.container_id=target AND active.company_id=c.company_id;
 SELECT target_mass_kg INTO planned_target FROM grainledger.exports_blend_plan_drafts WHERE id=prop.plan_id;
 remaining_target:=planned_target-coalesce((SELECT sum(quantity_kg) FROM grainledger.ledger_container_batches WHERE proposal_id=prop.id),0);
 SELECT * INTO current_scope FROM grainledger.ledger_container_scope_reviews WHERE proposal_id=prop.id ORDER BY recorded_at DESC,id DESC LIMIT 1;
 IF action='detail' THEN
  IF task.container_id IS NULL THEN RETURN jsonb_build_object('containerId',target,'state','NOT_REQUESTED','version',0); END IF;
  RETURN jsonb_build_object('containerId',target,'code',c.internal_code,'state',CASE WHEN prop.id IS DISTINCT FROM task.proposal_id THEN 'APPROVAL_CHANGED' ELSE task.state END,'version',task.version,'facilityId',task.facility_id,
   'ownerId',c.company_id,'outputCommodityId',(SELECT output_commodity_id FROM grainledger.ledger_compatibilities WHERE id=(prop.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid),
   'proposalId',task.proposal_id,'operationId',task.operation_id,'outputId',task.output_id,'outputLocationId',task.output_location_id,
   'outputCode',(SELECT code FROM grainledger.ledger_lots WHERE id=task.output_id),'targetKg',coalesce((SELECT grainledger.ledger_kg(saved_op.content->'outputs'->0->>'quantityKg') FROM grainledger.ledger_operations saved_op WHERE saved_op.id=task.operation_id),remaining_target)::numeric(20,6)::text,'remainingProductionKg',remaining_target::numeric(20,6)::text,'plannedKg',planned_target::numeric(20,6)::text,'containerVersion',c.version,
   'operation',CASE WHEN task.operation_id IS NOT NULL THEN grainledger.ledger_operation_json(task.operation_id) ELSE NULL END,
   'inputs',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',i->>'lotId','locationId',i->>'locationId','quantityKg',(grainledger.ledger_kg(i->>'quantityKg')-coalesce((SELECT sum(e.quantity_kg) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=prop.id AND e.lot_id=(i->>'lotId')::uuid AND e.location_id=(i->>'locationId')::uuid AND e.action='CONSUME'),0))::numeric(20,6)::text,'approvedKg',i->>'quantityKg','lotCode',i->>'lotCode','poolRevision',(SELECT revision FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=(i->>'locationId')::uuid AND lot_id=(i->>'lotId')::uuid))) FROM jsonb_array_elements(CASE WHEN prop.id=task.proposal_id THEN prop.snapshot_json->'selection'->'allocations' ELSE '[]'::jsonb END) i),'[]'::jsonb));
 END IF;
 -- Internal assessment material is protected by the same task/owner/facility checks, not request-supplied company IDs.
 IF action='context' THEN
  IF basis IS NOT NULL THEN PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id); END IF;
  IF prop.id IS NULL THEN RAISE EXCEPTION 'Active approval required' USING ERRCODE='23514'; END IF;
  facility:=(SELECT (v.request_json->>'FacilityId')::uuid FROM grainledger.exports_blend_plan_previews v WHERE v.id=prop.preview_id);
  IF role_name IN ('MillOperator','MillManager') AND (task.proposal_id IS DISTINCT FROM prop.id OR NOT grainledger.ledger_assigned(a,facility)) THEN RAISE EXCEPTION 'Task authority changed' USING ERRCODE='42501'; END IF;
  SELECT * INTO compatibility FROM grainledger.ledger_compatibilities WHERE id=(prop.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid;
  SELECT jsonb_build_object('id',y.id,'facilityId',y.facility_id,'compatibilityPolicyId',y.compatibility_id,'versionNo',y.version_no,
   'outputFraction',y.output_fraction::text,'effectiveFrom',y.effective_from,'effectiveTo',y.effective_to,'evidenceId',y.evidence_id,'publishedAt',y.published_at)
   INTO yield FROM grainledger.ledger_planning_yield_rules y WHERE y.facility_id=facility AND y.compatibility_id=compatibility.id
   AND y.published_at<=statement_timestamp() AND y.effective_from<=statement_timestamp() AND (y.effective_to IS NULL OR statement_timestamp()<y.effective_to) ORDER BY y.version_no DESC,y.id LIMIT 1;
  SELECT coalesce(jsonb_agg(to_jsonb(q) ORDER BY q.lot_id,q.attribute_id),'[]'::jsonb) INTO quality FROM (
   SELECT DISTINCT ON (q.lot_id,q.attribute_id) q.id,q.lot_id,q.attribute_id,q.version_no,q.measured_value::text,q.unit,q.evidence_id,q.measured_at,q.confirmed_at
   FROM grainledger.ledger_planning_quality_observations q WHERE q.owner_id=c.company_id AND q.facility_id=facility
   AND q.lot_id IN (SELECT (i->>'lotId')::uuid FROM jsonb_array_elements(prop.snapshot_json->'selection'->'allocations') i)
   AND q.confirmed_at<=statement_timestamp() ORDER BY q.lot_id,q.attribute_id,q.version_no DESC,q.id) q;
  RETURN jsonb_build_object('proposalId',prop.id,'snapshot',prop.snapshot_json,'facilityId',facility,'outputCommodityId',compatibility.output_commodity_id,'yieldRule',yield,'quality',quality,
   'operationId',task.operation_id,'outputId',task.output_id,'outputLocationId',task.output_location_id,'state',task.state,'inputSnapshot',CASE WHEN current_scope.decision='APPROVED' AND current_scope.scope='ALL_REMAINING' THEN current_scope.basis->'inputSnapshot' ELSE NULL END,'reviewScope',current_scope.scope,'outputKg',(SELECT o->>'quantityKg' FROM grainledger.ledger_operations saved_operation,jsonb_array_elements(saved_operation.content->'outputs') o WHERE saved_operation.id=task.operation_id LIMIT 1),'plannedKg',planned_target::numeric(20,6)::text);
 END IF;
 PERFORM grainledger.ledger_lock_writes();
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target FOR UPDATE;
 SELECT * INTO task FROM grainledger.ledger_container_production WHERE container_id=target FOR UPDATE;
 IF (action='request' AND role_name<>'ExportStaff') OR (action IN ('draft','commit','next') AND role_name<>'MillOperator')
  OR (action IN ('approve','reject') AND role_name<>'CustomerManager') OR action NOT IN ('request','draft','commit','next','approve','reject','replay') THEN
  RAISE EXCEPTION 'Production action authority denied' USING ERRCODE='42501'; END IF;
 IF action='replay' THEN
  -- The API checks the intended action permission before this lookup.
  operation:=(p->>'operationId')::uuid;
 ELSE operation:=(p->>'operationId')::uuid; END IF;
 IF operation IS NULL OR operation='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable operation ID required' USING ERRCODE='22023'; END IF;
 SELECT * INTO prior FROM grainledger.ledger_container_production_requests WHERE actor_id=a AND operation_id=operation;
 IF FOUND THEN
  IF (prior.action='request' AND role_name<>'ExportStaff') OR (prior.action IN ('draft','commit','next') AND role_name<>'MillOperator') OR (prior.action IN ('approve','reject') AND role_name<>'CustomerManager') THEN RAISE EXCEPTION 'Replay authority changed' USING ERRCODE='42501'; END IF;
  IF prior.container_id<>target OR prior.payload<>p OR (action='replay' AND prior.action IS DISTINCT FROM basis->>'action') OR (action<>'replay' AND prior.action<>action) THEN RAISE EXCEPTION 'Operation ID content conflict' USING ERRCODE='23514'; END IF;
  RETURN prior.result;
 END IF;
 IF action='replay' THEN RETURN jsonb_build_object('replayMissing',true); END IF;
 IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE NOT k=ANY(CASE action WHEN 'request' THEN ARRAY['operationId','version','proposalId'] WHEN 'draft' THEN ARRAY['operationId','version','draft'] WHEN 'commit' THEN ARRAY['operationId','version'] WHEN 'next' THEN ARRAY['operationId','version'] ELSE ARRAY['operationId','version','reason'] END)) THEN RAISE EXCEPTION 'Unknown production fields' USING ERRCODE='22023'; END IF;
 IF c.status='CLOSED' THEN RAISE EXCEPTION 'Closed container cannot produce/review' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target) THEN RAISE EXCEPTION 'Container HOLD blocks production/review' USING ERRCODE='23514'; END IF;
 IF prop.id IS NULL OR prop.snapshot_json->'selection'->>'mode' IS DISTINCT FROM 'BLEND_INPUT_STOCK' THEN RAISE EXCEPTION 'Active approved blend inputs required' USING ERRCODE='23514'; END IF;
 facility:=(SELECT (v.request_json->>'FacilityId')::uuid FROM grainledger.exports_blend_plan_previews v WHERE v.id=prop.preview_id);
 IF action='request' THEN
  IF (task.container_id IS NOT NULL AND task.proposal_id=prop.id) OR (p->>'version')::integer<>c.version OR prop.id IS DISTINCT FROM (p->>'proposalId')::uuid THEN RAISE EXCEPTION 'Production request is stale or already exists' USING ERRCODE='23514'; END IF;
  PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(prop.snapshot_json->'selection'->'allocations') i
   LEFT JOIN grainledger.ledger_positions pos ON pos.lot_id=(i->>'lotId')::uuid AND pos.location_id=(i->>'locationId')::uuid
   JOIN grainledger.ledger_locations loc ON loc.id=(i->>'locationId')::uuid
   WHERE pos.quantity_kg IS NULL OR pos.quantity_kg<grainledger.ledger_kg(i->>'quantityKg') OR loc.is_deleted OR grainledger.ledger_container_lot_blocked((i->>'lotId')::uuid)) THEN
   RAISE EXCEPTION 'Production inputs require current ordinary unrestricted stock' USING ERRCODE='23514'; END IF;
  INSERT INTO grainledger.ledger_container_production(container_id,proposal_id,facility_id,requested_by,state) VALUES(target,prop.id,facility,a,'REQUESTED')
   ON CONFLICT(container_id) DO UPDATE SET proposal_id=excluded.proposal_id,facility_id=excluded.facility_id,requested_by=excluded.requested_by,operation_id=NULL,output_id=NULL,output_location_id=NULL,review_id=NULL,state='REQUESTED',version=grainledger.ledger_container_production.version+1;
 ELSE
  IF task.proposal_id IS DISTINCT FROM prop.id OR task.version IS DISTINCT FROM (p->>'version')::integer THEN RAISE EXCEPTION 'Production version changed; reload' USING ERRCODE='23514'; END IF;
  IF action='next' THEN
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
   IF task.state NOT IN ('PRODUCED','APPROVED','REJECTED') OR remaining_target<=0 THEN RAISE EXCEPTION 'No remaining approved production quantity' USING ERRCODE='23514'; END IF;
   UPDATE grainledger.ledger_container_production SET operation_id=NULL,output_id=NULL,output_location_id=NULL,review_id=NULL,state='REQUESTED',version=version+1 WHERE container_id=target;
  ELSIF action IN ('draft','commit') THEN
   IF current_scope.id IS NOT NULL AND (current_scope.decision<>'APPROVED' OR current_scope.scope<>'ALL_REMAINING') THEN RAISE EXCEPTION 'Current ALL_REMAINING review required for further production' USING ERRCODE='23514'; END IF;
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
   IF task.state NOT IN ('REQUESTED','DRAFT') THEN RAISE EXCEPTION 'Only one production execution is supported' USING ERRCODE='23514'; END IF;
   SELECT * INTO compatibility FROM grainledger.ledger_compatibilities WHERE id=(prop.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid AND facility_id=facility;
   IF compatibility.id IS NULL OR compatibility.effective_from>clock_timestamp() THEN RAISE EXCEPTION 'Current blend compatibility required' USING ERRCODE='23514'; END IF;
   IF action='draft' THEN
    content:=p->'draft';
    IF content->>'kind' IS DISTINCT FROM 'BLENDING' OR (content->>'ownerId')::uuid IS DISTINCT FROM c.company_id OR (content->>'facilityId')::uuid IS DISTINCT FROM facility
      OR jsonb_array_length(content->'outputs')<>1 OR (task.operation_id IS NOT NULL AND task.operation_id IS DISTINCT FROM (content->>'id')::uuid) THEN RAISE EXCEPTION 'Single approved blend draft required' USING ERRCODE='23514'; END IF;
   ELSE
    SELECT * INTO op FROM grainledger.ledger_operations WHERE id=task.operation_id;
    content:=op.content;
    IF op.id IS NULL OR op.status<>'DRAFT' OR op.is_deleted OR op.kind<>'BLENDING' OR op.owner_id<>c.company_id OR op.facility_id<>facility THEN RAISE EXCEPTION 'Current matching production draft required' USING ERRCODE='23514'; END IF;
   END IF;
   IF content IS NULL OR jsonb_typeof(content->'inputs') IS DISTINCT FROM 'array' OR jsonb_typeof(content->'outputs') IS DISTINCT FROM 'array'
    OR jsonb_typeof(content->'declaredLossKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Exact processing arrays and decimal strings required' USING ERRCODE='22023'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements(content->'inputs') i WHERE jsonb_typeof(i->'quantityKg') IS DISTINCT FROM 'string')
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(content->'outputs') o WHERE jsonb_typeof(o->'quantityKg') IS DISTINCT FROM 'string') THEN RAISE EXCEPTION 'Processing mass must be a decimal string' USING ERRCODE='22023'; END IF;
   output:=content->'outputs'->0;
   batch_qty:=grainledger.ledger_kg(output->>'quantityKg');
   IF batch_qty<=0 OR batch_qty>remaining_target OR jsonb_array_length(content->'outputs')<>1 THEN RAISE EXCEPTION 'Batch exceeds remaining approved output' USING ERRCODE='23514'; END IF;
   IF jsonb_array_length(content->'inputs') IS DISTINCT FROM jsonb_array_length(prop.snapshot_json->'selection'->'allocations')
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(prop.snapshot_json->'selection'->'allocations') expected
     WHERE (SELECT count(*) FROM jsonb_array_elements(content->'inputs') i WHERE i->>'lotId'=expected->>'lotId' AND i->>'locationId'=expected->>'locationId'
      AND grainledger.ledger_kg(i->>'quantityKg')*planned_target=grainledger.ledger_kg(expected->>'quantityKg')*batch_qty)<>1) THEN
     RAISE EXCEPTION 'Partial execution must retain the approved input recipe and exact quota' USING ERRCODE='23514'; END IF;
   IF output->>'stage' IS DISTINCT FROM 'BLENDED_RICE' OR output->>'productIdentity' IS DISTINCT FROM 'BLENDED_RICE'
    OR (output->>'commodityId')::uuid IS DISTINCT FROM compatibility.output_commodity_id
    OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations loc WHERE loc.id=(output->>'locationId')::uuid AND loc.facility_id=facility AND NOT loc.mixed_bulk AND NOT loc.is_deleted AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)) THEN RAISE EXCEPTION 'Reviewed ordinary finished-output position required' USING ERRCODE='23514'; END IF;
   FOR item IN SELECT (i->>'lotId')::uuid lot_id,(i->>'locationId')::uuid location_id,grainledger.ledger_kg(i->>'quantityKg') quantity_kg FROM jsonb_array_elements(content->'inputs') i LOOP
    outstanding:=grainledger.ledger_container_outstanding(prop.id,item.lot_id,item.location_id);
    IF outstanding IS NULL OR outstanding<item.quantity_kg OR grainledger.ledger_container_lot_blocked(item.lot_id) THEN
     RAISE EXCEPTION 'Remaining approved input quota is changed or restricted' USING ERRCODE='23514'; END IF;
   END LOOP;
   IF action='draft' THEN
    result:=grainledger.ledger_command(a,'draft',content);
    UPDATE grainledger.ledger_container_production SET operation_id=(result->>'id')::uuid,state='DRAFT',version=version+1 WHERE container_id=target;
   ELSE
    PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
    -- Consume before stock debit so the existing reservation guard permits only this protected execution.
    INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
     SELECT gen_random_uuid(),prop.id,(i->>'lotId')::uuid,(i->>'locationId')::uuid,'CONSUME',grainledger.ledger_kg(i->>'quantityKg'),a,clock_timestamp() FROM jsonb_array_elements(content->'inputs') i;
    result:=grainledger.ledger_commit_operation(a,task.operation_id);
    INSERT INTO grainledger.ledger_blend_reservations(proposal_id,lot_id,location_id,quantity_kg) VALUES(prop.id,(output->>'id')::uuid,(output->>'locationId')::uuid,batch_qty);
    INSERT INTO grainledger.ledger_container_batches(operation_id,container_id,proposal_id,output_id,location_id,quantity_kg,inputs) VALUES(task.operation_id,target,prop.id,(output->>'id')::uuid,(output->>'locationId')::uuid,batch_qty,content->'inputs');
    INSERT INTO grainledger.ledger_container_output_claims(container_id,proposal_id,lot_id,location_id,quantity_kg,operation_id) VALUES(target,prop.id,(output->>'id')::uuid,(output->>'locationId')::uuid,batch_qty,task.operation_id);
    UPDATE grainledger.ledger_container_production SET output_id=(output->>'id')::uuid,output_location_id=(output->>'locationId')::uuid,state='PRODUCED',version=version+1 WHERE container_id=target;
   END IF;
  ELSE
   IF (SELECT count(*) FROM grainledger.ledger_container_batches WHERE proposal_id=prop.id)>1 THEN RAISE EXCEPTION 'Use scope review for multiple actual outputs' USING ERRCODE='23514'; END IF;
   SELECT grainledger.ledger_kg(o->>'quantityKg') INTO qty FROM grainledger.ledger_operations saved_operation,jsonb_array_elements(saved_operation.content->'outputs') o WHERE saved_operation.id=task.operation_id LIMIT 1;
   qty:=qty-coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.proposal_id=prop.id AND l.lot_id=task.output_id AND l.location_id=task.output_location_id),0);
   IF task.output_id IS NULL OR task.state NOT IN ('PRODUCED','APPROVED','REJECTED') OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'Produced output and review reason required' USING ERRCODE='23514'; END IF;
   IF action='approve' THEN
    PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
    IF grainledger.ledger_container_lot_blocked(task.output_id) THEN RAISE EXCEPTION 'Output restricted' USING ERRCODE='23514'; END IF;
    SELECT quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=r.proposal_id AND e.lot_id=r.lot_id AND e.location_id=r.location_id),0)
     INTO outstanding FROM grainledger.ledger_blend_reservations r WHERE proposal_id=prop.id AND lot_id=task.output_id AND location_id=task.output_location_id;
    IF task.state='REJECTED' THEN
     IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions pos WHERE pos.lot_id=task.output_id AND pos.location_id=task.output_location_id
      AND pos.quantity_kg-grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)>=qty) THEN RAISE EXCEPTION 'Output is no longer available for re-review' USING ERRCODE='23514'; END IF;
     INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
      VALUES(gen_random_uuid(),prop.id,task.output_id,task.output_location_id,'RESERVE',qty,a,clock_timestamp());
     outstanding:=qty;
    END IF;
    IF outstanding<>qty OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE lot_id=task.output_id AND location_id=task.output_location_id AND quantity_kg>=outstanding) THEN RAISE EXCEPTION 'Output reservation changed' USING ERRCODE='23514'; END IF;
   ELSE
    INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
     SELECT gen_random_uuid(),prop.id,task.output_id,task.output_location_id,'RELEASE',r.quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=r.proposal_id AND e.lot_id=r.lot_id AND e.location_id=r.location_id),0),a,clock_timestamp()
     FROM grainledger.ledger_blend_reservations r WHERE r.proposal_id=prop.id AND r.lot_id=task.output_id AND r.location_id=task.output_location_id
     AND r.quantity_kg>coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=r.proposal_id AND e.lot_id=r.lot_id AND e.location_id=r.location_id),0);
   END IF;
   review:=operation;
   INSERT INTO grainledger.ledger_container_output_reviews VALUES(review,target,a,CASE action WHEN 'approve' THEN 'APPROVED' ELSE 'REJECTED' END,btrim(p->>'reason'),coalesce(basis,'{}'::jsonb),basis->'outputSnapshot',clock_timestamp());
   UPDATE grainledger.ledger_container_production SET state=CASE action WHEN 'approve' THEN 'APPROVED' ELSE 'REJECTED' END,review_id=review,version=version+1 WHERE container_id=target;
   INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis) VALUES(review,target,prop.id,a,'OUTPUT_ONLY',CASE action WHEN 'approve' THEN 'APPROVED' ELSE 'REJECTED' END,btrim(p->>'reason'),coalesce(basis,'{}'::jsonb));
  END IF;
 END IF;
 IF action IN ('commit','approve','reject') THEN UPDATE grainledger.exports_containers SET version=version+1 WHERE id=target; END IF;
 INSERT INTO grainledger.ledger_container_production_events(container_id,actor_id,action,details) VALUES(target,a,action,jsonb_build_object('request',p,'basis',basis));
 IF action IN ('request','commit','approve') THEN PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id); END IF;
 result:=grainledger.ledger_container_production(a,target,'detail');
 INSERT INTO grainledger.ledger_container_production_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;

-- Forward read/loading contracts retain every historical plan.
CREATE OR REPLACE FUNCTION grainledger.ledger_export_container_read(a uuid,target uuid,action text DEFAULT 'detail',page_number integer DEFAULT 1,page_size integer DEFAULT 25) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; ext grainledger.ledger_container_execution; r text; result jsonb; scope_review grainledger.ledger_container_scope_reviews; closure_scopes jsonb;
BEGIN
 r:=grainledger.ledger_role(a);
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 SELECT * INTO ext FROM grainledger.ledger_container_execution WHERE container_id=target;
 IF c.id IS NULL OR r IS NULL OR NOT (
   r IN ('ExportStaff','CustomerManager') AND grainledger.ledger_own(a,c.company_id)
   OR r IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(a,ext.facility_id)
   OR r='Administrator' AND action='trace') THEN
  RAISE EXCEPTION 'Container unavailable in current scope' USING ERRCODE='42501'; END IF;
 IF action='trace' AND r NOT IN ('CustomerManager','Administrator') THEN
  RAISE EXCEPTION 'Backward source composition is not permitted' USING ERRCODE='42501'; END IF;
 IF action='history' THEN
  IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size>2147483647 THEN
   RAISE EXCEPTION 'Invalid history pagination' USING ERRCODE='22023'; END IF;
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(to_jsonb(e) ORDER BY e.recorded_at DESC,e.id)
   FROM (SELECT id,kind,actor_id,CASE WHEN r='CustomerManager' THEN details ELSE details-'basis' END details,recorded_at FROM grainledger.ledger_container_execution_events WHERE container_id=target
    ORDER BY recorded_at DESC,id LIMIT page_size OFFSET (page_number::bigint-1)*page_size) e),'[]'::jsonb),
    'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM grainledger.ledger_container_execution_events WHERE container_id=target),
    'hasMore',(SELECT count(*) FROM grainledger.ledger_container_execution_events WHERE container_id=target)>page_number::bigint*page_size);
 END IF;
 IF action='dossier' THEN
  SELECT dossier INTO result FROM grainledger.ledger_container_closures WHERE container_id=target;
  IF result IS NULL THEN RAISE EXCEPTION 'Container has no immutable closure dossier' USING ERRCODE='23514'; END IF;
  IF r<>'CustomerManager' THEN
   result:=(result-'sourceContributions'-'assessment'-'loads')||jsonb_build_object('redacted',true,
     'assessment',(result->'assessment')-'evidence',
     'loads',(SELECT jsonb_agg(item-'assessment' ORDER BY item->>'recorded_at',item->>'id') FROM jsonb_array_elements(result->'loads') item));
  END IF;
  RETURN result;
 END IF;
 IF action NOT IN ('detail','trace') THEN RAISE EXCEPTION 'Unsupported container read' USING ERRCODE='22023'; END IF;
 SELECT jsonb_build_object('id',c.id,'orderId',c.order_id,'code',c.internal_code,'companyId',c.company_id,
  'targetKg',c.target_mass_kg::text,'status',c.status,'version',c.version,
  'facilityId',ext.facility_id,'proposalId',ext.proposal_id,'containerNumber',ext.container_number,'sealNumber',ext.seal_number,
  'loadedKg',coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l
   JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target),0)::numeric(20,6)::text,
  'holds',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'reason',reason,'recordedAt',recorded_at) ORDER BY recorded_at,id)
   FROM grainledger.ledger_container_holds WHERE container_id=target),'[]'::jsonb),
  'loads',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'proposalId',h.proposal_id,'loadedAt',h.loaded_at,'recordedAt',h.recorded_at,
   'lines',(SELECT jsonb_agg(jsonb_build_object('lotId',l.lot_id,'lotCode',x.code,'locationId',l.location_id,'quantityKg',l.quantity_kg::text,'restricted',grainledger.ledger_container_lot_blocked(l.lot_id))
    ORDER BY l.lot_id,l.location_id) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_lots x ON x.id=l.lot_id WHERE l.load_id=h.id))
   ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_loads h WHERE h.container_id=target),'[]'::jsonb),
  'approval',(SELECT jsonb_build_object('proposalId',p.id,'planId',p.plan_id,'decisionVersion',d.version,
     'snapshot',p.snapshot_json,'previewRequest',v.request_json,'demandSnapshot',plan.demand_snapshot::text,
     'productionState',(SELECT t.state FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=p.id),
     'productionOperationId',(SELECT t.operation_id FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=p.id),
     'outputReviewId',(SELECT t.review_id FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=p.id AND t.state='APPROVED'),
     'outputSnapshot',(SELECT rev.snapshot FROM grainledger.ledger_container_production t JOIN grainledger.ledger_container_output_reviews rev ON rev.id=t.review_id WHERE t.container_id=target AND t.proposal_id=p.id AND t.state='APPROVED'),
     'productionOperations',(SELECT coalesce(jsonb_agg(b.operation_id ORDER BY b.recorded_at,b.operation_id),'[]'::jsonb) FROM grainledger.ledger_container_batches b WHERE b.proposal_id=p.id),
     'specificationId',plan.specification_id,'mode',p.snapshot_json->'selection'->>'mode',
     'allocations',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',res.lot_id,'lotCode',lot.code,'locationId',res.location_id,
       'restricted',grainledger.ledger_container_lot_blocked(res.lot_id),
       'loadingPositionReady',EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id JOIN grainledger.ledger_facilities f ON f.id=loc.facility_id
         WHERE pos.lot_id=res.lot_id AND pos.location_id=res.location_id AND pos.quantity_kg>0 AND NOT loc.is_deleted AND NOT f.is_deleted
          AND NOT loc.mixed_bulk
          AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)
          AND (EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id) OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 OR
           ((SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 AND
            (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id)=pos.quantity_kg))
          AND (NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)
           OR (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)=pos.quantity_kg)),
       'reservedKg',res.quantity_kg::text,'remainingKg',(res.quantity_kg-coalesce((SELECT sum(CASE WHEN ev.action='RESERVE' THEN -ev.quantity_kg ELSE ev.quantity_kg END) FROM grainledger.ledger_blend_reservation_events ev
         WHERE ev.proposal_id=res.proposal_id AND ev.lot_id=res.lot_id AND ev.location_id=res.location_id),0))::numeric(20,6)::text)
       ORDER BY res.lot_id,res.location_id) FROM grainledger.ledger_blend_reservations res JOIN grainledger.ledger_lots lot ON lot.id=res.lot_id WHERE res.proposal_id=p.id AND EXISTS(SELECT 1 FROM jsonb_array_elements(grainledger.ledger_container_review_context(p.id)->'outputs') o WHERE (o->>'lotId')::uuid=res.lot_id AND (o->>'locationId')::uuid=res.location_id)),'[]'::jsonb))
    FROM grainledger.exports_active_blend_approvals active
    JOIN grainledger.exports_blend_plan_proposals p ON p.id=active.proposal_id
    JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id
    JOIN grainledger.exports_blend_plan_previews v ON v.id=p.preview_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=p.id AND d.version=active.decision_version AND d.action='APPROVED'
    WHERE active.container_id=target AND active.company_id=c.company_id),
  'dossierAvailable',EXISTS(SELECT 1 FROM grainledger.ledger_container_closures WHERE container_id=target)) INTO result;
 result:=result||jsonb_build_object('remainingKg',(c.target_mass_kg-(result->>'loadedKg')::numeric)::numeric(20,6)::text);
 IF result->'approval'<>'null'::jsonb THEN
  SELECT * INTO scope_review FROM grainledger.ledger_container_scope_reviews WHERE proposal_id=(result->'approval'->>'proposalId')::uuid ORDER BY recorded_at DESC,id DESC LIMIT 1;
  IF scope_review.id IS NOT NULL THEN
   result:=jsonb_set(result,'{approval}',(result->'approval')||jsonb_build_object('scopeReviewId',scope_review.id,'scope',scope_review.scope,
    'scopeReviewDecision',scope_review.decision,'outputReviewId',scope_review.id,
    'outputSnapshot',CASE WHEN scope_review.decision='APPROVED' THEN scope_review.basis->'outputSnapshot' ELSE NULL END));
  END IF;
 END IF;
 SELECT coalesce(jsonb_agg(jsonb_build_object('proposalId',q.id,'facilityId',(v.request_json->>'FacilityId')::uuid,'specificationId',d.specification_id,
  'snapshot',coalesce((SELECT r.basis->'outputSnapshot' FROM grainledger.ledger_container_scope_reviews r WHERE r.proposal_id=q.id AND r.decision='APPROVED' ORDER BY r.recorded_at DESC,r.id DESC LIMIT 1),
   CASE WHEN q.snapshot_json->'selection'->>'mode'='DIRECT_STOCK' THEN q.snapshot_json END),
  'reviewId',(SELECT r.id FROM grainledger.ledger_container_scope_reviews r WHERE r.proposal_id=q.id ORDER BY r.recorded_at DESC,r.id DESC LIMIT 1),
  'productionOperations',(SELECT coalesce(jsonb_agg(b.operation_id ORDER BY b.recorded_at,b.operation_id),'[]'::jsonb) FROM grainledger.ledger_container_batches b WHERE b.proposal_id=q.id),
  'latestDecision',(SELECT r.decision FROM grainledger.ledger_container_scope_reviews r WHERE r.proposal_id=q.id ORDER BY r.recorded_at DESC,r.id DESC LIMIT 1))),
  '[]'::jsonb) INTO closure_scopes
 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
 JOIN grainledger.exports_blend_plan_drafts d ON d.id=q.plan_id
 WHERE q.id IN (SELECT proposal_id FROM grainledger.ledger_container_loads WHERE container_id=target);
 result:=result||jsonb_build_object('closingScopes',closure_scopes,'workflowAvailable',true);
 IF action='trace' THEN
  IF (SELECT count(*) FROM grainledger.ledger_container_load_sources s JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id
       JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>50000 THEN
   RAISE EXCEPTION 'Container trace exceeds the supported complete result size' USING ERRCODE='22023'; END IF;
  result:=result-'approval'||jsonb_build_object('sourceContributions',coalesce((SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',kg::numeric(20,6)::text) ORDER BY source_lot_id)
   FROM (SELECT s.source_lot_id,sum(s.quantity_kg) kg FROM grainledger.ledger_container_load_sources s
    JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
    WHERE h.container_id=target GROUP BY s.source_lot_id) totals),'[]'::jsonb));
  INSERT INTO grainledger.ledger_events(kind,actor_id,details)
   VALUES('TRACEBACK',a,jsonb_build_object('containerId',target,'ownerId',c.company_id,'facilityId',ext.facility_id,'direction','backward'));
 END IF;
 RETURN result;
END $$;



CREATE OR REPLACE FUNCTION grainledger.ledger_export_container_command(a uuid,target uuid,action text,p jsonb,basis jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; ext grainledger.ledger_container_execution; req grainledger.ledger_container_execution_requests;
 r text; company uuid; operation uuid; line jsonb; lot grainledger.ledger_lots; loc grainledger.ledger_locations;
 quantity numeric; total numeric:=0; physical numeric; outstanding numeric; source_count integer; line_id uuid;
 event_id uuid:=gen_random_uuid(); selected_proposal uuid; facility uuid; result jsonb; allowed text[];
BEGIN
 PERFORM grainledger.ledger_lock_writes();
 SELECT "OrganizationId" INTO company FROM grainledger.identity_users WHERE "Id"=a AND "IsActive" FOR SHARE;
 r:=grainledger.ledger_role(a);
 IF r IS NULL OR (action='hold' AND r<>'CustomerManager') OR (action<>'hold' AND r<>'ExportStaff') THEN
  RAISE EXCEPTION 'Container mutation authority denied' USING ERRCODE='42501'; END IF;
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target AND company_id=company FOR UPDATE;
 IF c.id IS NULL OR NOT grainledger.ledger_own(a,c.company_id) THEN RAISE EXCEPTION 'Container unavailable in current company' USING ERRCODE='42501'; END IF;
 allowed:=CASE action WHEN 'metadata' THEN ARRAY['operationId','version','containerNumber','sealNumber']
  WHEN 'hold' THEN ARRAY['operationId','version','reason'] WHEN 'load' THEN ARRAY['operationId','version','proposalId','decisionVersion','loadedAt','lines']
  WHEN 'close' THEN ARRAY['operationId','version'] END;
 IF allowed IS NULL OR jsonb_typeof(p) IS DISTINCT FROM 'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE NOT k=ANY(allowed)) THEN
  RAISE EXCEPTION 'Unsupported container command fields' USING ERRCODE='22023'; END IF;
 operation:=(p->>'operationId')::uuid;
 IF operation IS NULL OR operation='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable operation ID required' USING ERRCODE='22023'; END IF;
 SELECT * INTO req FROM grainledger.ledger_container_execution_requests WHERE actor_id=a AND operation_id=operation;
 IF FOUND THEN
  IF req.container_id<>target OR req.action<>action OR req.payload<>p THEN RAISE EXCEPTION 'Operation ID reused with different content' USING ERRCODE='23514'; END IF;
  RETURN req.result;
 END IF;
 IF basis IS NULL AND action IN ('load','close') THEN RETURN jsonb_build_object('replayMissing',true); END IF;
 IF c.version IS DISTINCT FROM (p->>'version')::integer THEN RAISE EXCEPTION 'Container version changed; reload' USING ERRCODE='23514'; END IF;
 IF c.status='CLOSED' AND action<>'hold' THEN RAISE EXCEPTION 'Closed container cannot be changed or loaded' USING ERRCODE='23514'; END IF;
 INSERT INTO grainledger.ledger_container_execution(container_id) VALUES(target) ON CONFLICT DO NOTHING;
 SELECT * INTO ext FROM grainledger.ledger_container_execution WHERE container_id=target;
 IF action='metadata' THEN
  UPDATE grainledger.ledger_container_execution SET container_number=nullif(btrim(p->>'containerNumber'),''),seal_number=nullif(btrim(p->>'sealNumber'),'') WHERE container_id=target;
 ELSIF action='hold' THEN
  INSERT INTO grainledger.ledger_container_holds(id,container_id,actor_id,reason) VALUES(operation,target,a,btrim(p->>'reason'));
 ELSE
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target) THEN RAISE EXCEPTION 'Container HOLD blocks loading and closure' USING ERRCODE='23514'; END IF;
  IF basis->>'assessmentStatus' IS DISTINCT FROM 'PASS' OR basis->>'phase' IS DISTINCT FROM (CASE WHEN action='load' THEN 'LOADING' ELSE 'CLOSE' END) THEN
   RAISE EXCEPTION 'Current container assessment is required' USING ERRCODE='23514'; END IF;
  -- Future-dated publications can become effective while calculations run even under the writer lock.
  IF (SELECT count(*) FROM grainledger.catalog_mrl_versions m
       WHERE m.market_id=(basis->'evidence'->'applicableMrl'->>'marketId')::uuid
         AND m.commodity_id=(basis->'evidence'->'applicableMrl'->>'commodityId')::uuid AND m.status='PUBLISHED'
         AND m.published_at<=clock_timestamp() AND m.effective_from<=clock_timestamp()
         AND (m.effective_to IS NULL OR clock_timestamp()<m.effective_to))<>1
     OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_mrl_versions m
       WHERE m.id=(basis->'evidence'->'applicableMrl'->>'versionId')::uuid AND m.status='PUBLISHED'
         AND m.effective_from<=clock_timestamp() AND (m.effective_to IS NULL OR clock_timestamp()<m.effective_to))
     OR (SELECT id FROM grainledger.catalog_blend_assessment_policies policy WHERE policy.company_id=company
       AND policy.market_id=(basis->'evidence'->'assessmentPolicy'->>'marketId')::uuid
       AND policy.commodity_id=(basis->'evidence'->'assessmentPolicy'->>'commodityId')::uuid
       AND policy.published_at<=clock_timestamp() AND policy.effective_from<=clock_timestamp()
       AND (policy.effective_to IS NULL OR clock_timestamp()<policy.effective_to)
       ORDER BY policy.version_no DESC,policy.id LIMIT 1) IS DISTINCT FROM (basis->'evidence'->'assessmentPolicy'->>'id')::uuid THEN
   RAISE EXCEPTION 'Assessment policy or legal version changed at commit; refresh and review approval' USING ERRCODE='23514'; END IF;
  SELECT active.proposal_id INTO selected_proposal FROM grainledger.exports_active_blend_approvals active
   JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=active.proposal_id AND d.version=active.decision_version AND d.action='APPROVED'
   WHERE active.container_id=target AND active.company_id=company;
  IF selected_proposal IS NULL THEN RAISE EXCEPTION 'Current pinned approval required' USING ERRCODE='23514'; END IF;
  IF action='load' THEN
   IF selected_proposal IS DISTINCT FROM (p->>'proposalId')::uuid OR NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals
    WHERE proposal_id=selected_proposal AND decision_version=(p->>'decisionVersion')::integer) THEN RAISE EXCEPTION 'Approval decision changed' USING ERRCODE='23514'; END IF;
   SELECT (v.request_json->>'FacilityId')::uuid INTO facility FROM grainledger.exports_blend_plan_proposals prop
    JOIN grainledger.exports_blend_plan_previews v ON v.id=prop.preview_id
    WHERE prop.id=selected_proposal AND (prop.snapshot_json->'selection'->>'mode'='DIRECT_STOCK' OR EXISTS(SELECT 1 FROM grainledger.ledger_container_scope_reviews r WHERE r.container_id=target AND r.proposal_id=prop.id AND r.decision='APPROVED' AND r.basis->'outputSnapshot'<>'null'::jsonb));
   IF facility IS NULL THEN RAISE EXCEPTION 'Approved blend execution/output handoff is required before loading' USING ERRCODE='23514'; END IF;
   IF jsonb_typeof(p->'lines') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'lines') NOT BETWEEN 1 AND 100 OR p->>'loadedAt' IS NULL THEN RAISE EXCEPTION 'Loading time and one to 100 lines required' USING ERRCODE='22023'; END IF;
   IF (SELECT count(*) FROM grainledger.ledger_container_loads WHERE container_id=target)>=1000 THEN RAISE EXCEPTION 'Container load limit reached' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'lines') x GROUP BY x->>'lotId',x->>'locationId' HAVING count(*)>1) THEN RAISE EXCEPTION 'Combine duplicate positions before loading' USING ERRCODE='22023'; END IF;
   FOR line IN SELECT * FROM jsonb_array_elements(p->'lines') LOOP
    IF jsonb_typeof(line) IS DISTINCT FROM 'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(line) k WHERE k NOT IN ('lotId','locationId','quantityKg')) OR jsonb_typeof(line->'quantityKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Exact loading line fields and kg string required' USING ERRCODE='22023'; END IF;
    quantity:=grainledger.ledger_kg(line->>'quantityKg');
    IF quantity<=0 THEN RAISE EXCEPTION 'Positive loading kg required' USING ERRCODE='22023'; END IF;
    total:=total+quantity;
   END LOOP;
   IF total+(SELECT coalesce(sum(l.quantity_kg),0) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>c.target_mass_kg THEN RAISE EXCEPTION 'Loading exceeds container target' USING ERRCODE='23514'; END IF;
   INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details)
    VALUES(event_id,'CONTAINER_LOADING',a,(p->'lines'->0->>'lotId')::uuid,jsonb_build_object('containerId',target,'ownerId',company,'facilityId',facility,'proposalId',selected_proposal,'operationId',operation));
   INSERT INTO grainledger.ledger_container_loads(id,container_id,proposal_id,loaded_at,actor_id,assessment)
    VALUES(event_id,target,selected_proposal,(p->>'loadedAt')::timestamptz,a,basis);
   FOR line IN SELECT * FROM jsonb_array_elements(p->'lines') LOOP
    quantity:=grainledger.ledger_kg(line->>'quantityKg');
    IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(grainledger.ledger_container_review_context(selected_proposal)->'outputs') o
     WHERE (o->>'lotId')::uuid=(line->>'lotId')::uuid AND (o->>'locationId')::uuid=(line->>'locationId')::uuid) THEN
     RAISE EXCEPTION 'Only current reviewed output positions may be loaded' USING ERRCODE='23514'; END IF;
    SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid AND owner_id=company;
    SELECT * INTO loc FROM grainledger.ledger_locations WHERE id=(line->>'locationId')::uuid AND facility_id=facility;
    IF lot.id IS NULL OR loc.id IS NULL OR (loc.owner_id IS NOT NULL AND loc.owner_id<>company) THEN RAISE EXCEPTION 'Loading position outside approved scope' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.ledger_require_active_location(loc.id);
    IF loc.mixed_bulk THEN RAISE EXCEPTION 'Assigned operator must hand off current pool stock to a reviewed ordinary loading position' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_container_lot_blocked(lot.id) THEN RAISE EXCEPTION 'Loading lot is restricted' USING ERRCODE='23514'; END IF;
    SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=loc.id FOR UPDATE;
    SELECT res.quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e
      WHERE e.proposal_id=res.proposal_id AND e.lot_id=res.lot_id AND e.location_id=res.location_id),0)
     INTO outstanding FROM grainledger.ledger_blend_reservations res WHERE res.proposal_id=selected_proposal AND res.lot_id=lot.id AND res.location_id=loc.id;
    IF physical IS NULL OR outstanding IS NULL OR quantity>physical OR quantity>outstanding THEN RAISE EXCEPTION 'Loading exceeds stock or approved reservation' USING ERRCODE='23514'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=lot.id AND location_id=loc.id) THEN
     SELECT count(*) INTO source_count FROM grainledger.ledger_contributions WHERE lot_id=lot.id AND quantity_kg>0;
     IF source_count=0 OR source_count>10000 OR (source_count>1 AND (
       (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=lot.id)<>physical
       OR (SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=lot.id AND quantity_kg>0)<>1)) THEN
      RAISE EXCEPTION 'Position source attribution is unresolved' USING ERRCODE='23514'; END IF;
     INSERT INTO grainledger.ledger_container_source_balances(lot_id,location_id,source_lot_id,quantity_kg)
      SELECT lot.id,loc.id,source_lot_id,CASE WHEN source_count=1 THEN physical ELSE quantity_kg END
      FROM grainledger.ledger_contributions WHERE lot_id=lot.id AND quantity_kg>0;
    END IF;
    IF (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=lot.id AND location_id=loc.id) IS DISTINCT FROM physical THEN
     RAISE EXCEPTION 'External movement changed the retained source complement; reconciliation required' USING ERRCODE='23514'; END IF;
    INSERT INTO grainledger.ledger_container_load_lines(load_id,lot_id,location_id,quantity_kg) VALUES(event_id,lot.id,loc.id,quantity) RETURNING id INTO line_id;
    INSERT INTO grainledger.ledger_container_load_sources(line_id,source_lot_id,quantity_kg)
     SELECT line_id,shares.source_lot_id,shares.quantity_kg FROM grainledger.ledger_container_allocate_sources(quantity,physical,
       (SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
        FROM grainledger.ledger_container_source_balances WHERE lot_id=lot.id AND location_id=loc.id AND quantity_kg>0)) shares;
    UPDATE grainledger.ledger_container_source_balances b SET quantity_kg=b.quantity_kg-s.quantity_kg
     FROM grainledger.ledger_container_load_sources s WHERE s.line_id=line_id AND b.lot_id=lot.id AND b.location_id=loc.id AND b.source_lot_id=s.source_lot_id;
    IF (SELECT sum(quantity_kg) FROM grainledger.ledger_container_load_sources WHERE ledger_container_load_sources.line_id=line_id) IS DISTINCT FROM quantity THEN RAISE EXCEPTION 'Source allocation did not conserve mass' USING ERRCODE='23514'; END IF;
    INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
      VALUES(gen_random_uuid(),selected_proposal,lot.id,loc.id,'CONSUME',quantity,a,clock_timestamp());
    PERFORM grainledger.ledger_post(event_id,lot.id,loc.id,-quantity);
   END LOOP;
   UPDATE grainledger.ledger_container_execution SET facility_id=facility,proposal_id=selected_proposal WHERE container_id=target;
   UPDATE grainledger.exports_containers SET status='LOADING' WHERE id=target;
  ELSIF action='close' THEN
   IF c.status<>'LOADING' OR (SELECT coalesce(sum(l.quantity_kg),0) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)<>c.target_mass_kg THEN RAISE EXCEPTION 'Exact target must be actually loaded before closure' USING ERRCODE='23514'; END IF;
   IF ext.container_number IS NULL OR ext.seal_number IS NULL THEN RAISE EXCEPTION 'Container number and seal required before closure' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target AND grainledger.ledger_container_lot_blocked(l.lot_id)) THEN RAISE EXCEPTION 'A loaded lot is currently restricted' USING ERRCODE='23514'; END IF;
   IF (SELECT sum(target_mass_kg) FROM grainledger.exports_containers WHERE order_id=c.order_id)>(SELECT required_mass_kg FROM grainledger.exports_export_orders WHERE id=c.order_id) THEN RAISE EXCEPTION 'Container targets exceed order mass' USING ERRCODE='23514'; END IF;
   INSERT INTO grainledger.ledger_container_closures(container_id,actor_id,dossier) VALUES(target,a,jsonb_build_object(
    'schemaVersion',1,'containerId',target,'orderId',c.order_id,'companyId',company,'containerNumber',ext.container_number,'sealNumber',ext.seal_number,
    'proposalId',selected_proposal,'targetKg',c.target_mass_kg::text,'closedBy',a,'closedAt',clock_timestamp(),'assessment',basis,
    'loads',(SELECT jsonb_agg(to_jsonb(h) ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_loads h WHERE container_id=target),
    'lines',(SELECT jsonb_agg(to_jsonb(l) ORDER BY l.id) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target),
    'sourceContributions',(SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',kg::numeric(20,6)::text,'percentage',(kg*100/c.target_mass_kg)::numeric(18,6)::text) ORDER BY source_lot_id)
     FROM (SELECT s.source_lot_id,sum(s.quantity_kg) kg FROM grainledger.ledger_container_load_sources s JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target GROUP BY s.source_lot_id) totals)));
   UPDATE grainledger.exports_containers SET status='CLOSED' WHERE id=target;
  ELSE RAISE EXCEPTION 'Unsupported execution command' USING ERRCODE='22023'; END IF;
 END IF;
 UPDATE grainledger.exports_containers SET version=version+1 WHERE id=target;
 INSERT INTO grainledger.ledger_container_execution_events(container_id,actor_id,kind,details)
   VALUES(target,a,upper(action),jsonb_build_object('operationId',operation,'request',p));
 IF action IN ('load','close') THEN PERFORM grainledger.ledger_production_assert_basis(basis,company); END IF;
 result:=grainledger.ledger_export_container_read(a,target,'detail');
 INSERT INTO grainledger.ledger_container_execution_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;

-- Only the protected workflow entry is a new runtime surface. Setup grants it explicitly.
REVOKE ALL ON TABLE grainledger.ledger_container_batches,grainledger.ledger_container_output_claims,
 grainledger.ledger_container_scope_reviews,grainledger.ledger_container_workflow_requests,grainledger.ledger_container_handoffs,
 grainledger.ledger_container_claim_transitions,grainledger.ledger_container_documents,grainledger.ledger_container_supplements,
 grainledger.ledger_container_notices,grainledger.ledger_container_hold_responses FROM PUBLIC;
REVOKE ALL ON FUNCTION grainledger.ledger_container_outstanding(uuid,uuid,uuid),
 grainledger.ledger_container_set_reservation(uuid,uuid,uuid,uuid,numeric),grainledger.ledger_container_unplanned_kg(uuid),
 grainledger.ledger_container_review_context(uuid),grainledger.ledger_container_scope_sources(uuid,uuid),
 grainledger.ledger_container_hold_notify(),grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb) FROM PUBLIC;

-- Custody extraction uses the proven remaining source complement after actual loading.
CREATE OR REPLACE FUNCTION grainledger.ledger_pool_withdraw(a uuid, command_id uuid, command_kind text, lot uuid, loc uuid, quantity numeric, expected_revision bigint DEFAULT NULL::bigint) RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE
    destination grainledger.ledger_locations; source grainledger.ledger_lots; pool grainledger.ledger_mixed_bulk_pools; policy uuid;
    source_position numeric; source_total numeric; event_id uuid:=gen_random_uuid(); aliquot_id uuid:=gen_random_uuid();
    remainder_id uuid:=gen_random_uuid(); actual_id uuid; aliquot_code text; remainder_code text;
    aliquot_origin uuid; remainder_origin uuid; req jsonb; result_id uuid; revision_value bigint:=0;
    output_quantity numeric; source_count integer; force_extract boolean; current_sources jsonb; retained boolean:=false;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF quantity<=0 OR quantity<>round(quantity,6) OR command_id IS NULL OR command_kind IS NULL THEN
        RAISE EXCEPTION 'Positive exact custody withdrawal quantity and command identity required' USING ERRCODE='22023';
    END IF;
    SELECT * INTO STRICT destination FROM grainledger.ledger_locations WHERE id=loc FOR UPDATE;
    SELECT * INTO STRICT source FROM grainledger.ledger_lots WHERE id=lot FOR UPDATE;
    SELECT COALESCE(quantity_kg,0) INTO source_position FROM grainledger.ledger_positions WHERE lot_id=lot AND location_id=loc;
    force_extract:=command_kind='TRANSFER_TO_MIXED';
    IF NOT destination.mixed_bulk AND NOT force_extract
       AND NOT grainledger.ledger_mixed_bulk_custody_derived(lot)
       AND NOT (command_kind='PROCESSING' AND EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances b WHERE b.lot_id=lot AND b.location_id=loc)) THEN RETURN lot; END IF;
    req:=jsonb_build_object('actorId',a,'commandId',command_id,'commandKind',command_kind,'lotId',lot,
        'locationId',loc,'quantityKg',quantity::text,'expectedRevision',expected_revision);
    SELECT ce.output_lot_id INTO result_id FROM grainledger.ledger_custody_events ce
    WHERE ce.command_id=$2 AND ce.command_kind=$3 AND ce.action='WITHDRAW'
      AND ce.location_id=$5 AND ce.input_lot_id=$4;
    IF FOUND THEN
        IF (SELECT ce.request FROM grainledger.ledger_custody_events ce WHERE ce.command_id=$2
            AND ce.command_kind=$3 AND ce.action='WITHDRAW' AND ce.location_id=$5 AND ce.input_lot_id=$4)
            IS DISTINCT FROM req THEN RAISE EXCEPTION 'Custody command retry conflicts with original payload' USING ERRCODE='23505'; END IF;
        RETURN result_id;
    END IF;
    IF NOT grainledger.ledger_assigned(a,destination.facility_id) OR grainledger.ledger_role(a) NOT IN ('MillOperator','MillManager') THEN
        RAISE EXCEPTION 'Mixed-bulk custody authority denied' USING ERRCODE='42501';
    END IF;
    IF source_position<quantity THEN RAISE EXCEPTION 'Selected stock position has insufficient quantity' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_receiving_restricted(lot) OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_correction_blocks b JOIN grainledger.ledger_lots x ON x.receipt_id=b.receipt_id WHERE x.id=lot)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_reversals r JOIN grainledger.ledger_lots x ON x.receipt_id=r.receipt_id WHERE x.id=lot)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections c WHERE c.status IN ('REQUESTED','RECONCILED')
          AND EXISTS(SELECT 1 FROM grainledger.ledger_lots x WHERE x.operation_id=c.operation_id AND x.id=lot)) THEN
        RAISE EXCEPTION 'Restricted or correction-bound stock cannot be withdrawn' USING ERRCODE='23514';
    END IF;
    SELECT COALESCE(sum(quantity_kg),0) INTO source_total FROM grainledger.ledger_positions WHERE lot_id=lot AND quantity_kg>0;
    retained:=EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances b WHERE b.lot_id=lot AND b.location_id=loc);
    IF retained THEN
        IF (SELECT sum(b.quantity_kg) FROM grainledger.ledger_container_source_balances b WHERE b.lot_id=lot AND b.location_id=loc) IS DISTINCT FROM source_position THEN
            RAISE EXCEPTION 'Retained source complement changed before custody withdrawal' USING ERRCODE='23514'; END IF;
        SELECT jsonb_agg(jsonb_build_object('sourceLotId',b.source_lot_id,'quantityKg',b.quantity_kg::text) ORDER BY b.source_lot_id)
        INTO current_sources FROM grainledger.ledger_container_source_balances b WHERE b.lot_id=lot AND b.location_id=loc AND b.quantity_kg>0;
    ELSE
        SELECT jsonb_agg(jsonb_build_object('sourceLotId',c.source_lot_id,'quantityKg',c.quantity_kg::text) ORDER BY c.source_lot_id)
        INTO current_sources FROM grainledger.ledger_contributions c WHERE c.lot_id=lot AND c.quantity_kg>0;
    END IF;
    source_count:=coalesce(jsonb_array_length(current_sources),0);
    IF source_count=0 OR (NOT retained AND source_count>1 AND (
        (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=lot) IS DISTINCT FROM source_total
        OR source_position<>source_total
    )) THEN
        RAISE EXCEPTION 'Multi-source stock withdrawal attribution is ambiguous' USING ERRCODE='23514';
    END IF;
    IF destination.mixed_bulk THEN
        SELECT * INTO STRICT pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
        IF pool.lot_id IS DISTINCT FROM lot THEN RAISE EXCEPTION 'Selected pool generation is stale' USING ERRCODE='40001'; END IF;
        IF expected_revision IS NULL OR expected_revision<>pool.revision THEN
            RAISE EXCEPTION 'Selected pool revision is stale' USING ERRCODE='40001';
        END IF;
        revision_value:=pool.revision;
        policy:=grainledger.ledger_mixed_bulk_eligible(loc,source.owner_id,source.commodity_id,source.stage,source.product_identity,clock_timestamp());
        IF policy IS NULL THEN RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514'; END IF;
        IF source_position<>source_total OR EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>lot) THEN
            RAISE EXCEPTION 'Mixed-bulk generation position is not a complete physical balance' USING ERRCODE='23514';
        END IF;
    END IF;
    output_quantity:=source_position-quantity;
    actual_id:=aliquot_id;
    INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details) VALUES(event_id,'POOL_WITHDRAW',a,lot,
        jsonb_build_object('commandId',command_id,'commandKind',command_kind,'locationId',loc,
            'quantityKg',quantity::text,'poolRevision',revision_value,'expectedRevision',expected_revision));
    INSERT INTO grainledger.ledger_custody_events(event_id,command_id,command_kind,action,actor_id,location_id,input_lot_id,
        output_lot_id,policy_id,revision,quantity_kg,request)
    VALUES(event_id,command_id,command_kind,'WITHDRAW',a,loc,lot,aliquot_id,policy,revision_value+1,quantity,req);
    aliquot_code:=grainledger.ledger_reserve_lot_code_internal(aliquot_id,source.owner_id,'CUSTODY',command_id,source.stage,source.product_identity);
    aliquot_origin:=grainledger.ledger_register_code(aliquot_code,aliquot_id);
    INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(aliquot_id,aliquot_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,aliquot_origin);
    IF output_quantity>0 THEN
        remainder_code:=grainledger.ledger_reserve_lot_code_internal(remainder_id,source.owner_id,'CUSTODY',command_id,source.stage,source.product_identity);
        remainder_origin:=grainledger.ledger_register_code(remainder_code,remainder_id);
        INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
        VALUES(remainder_id,remainder_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,remainder_origin);
    END IF;
    PERFORM grainledger.ledger_post(event_id,lot,loc,-source_position);
    PERFORM grainledger.ledger_post(event_id,aliquot_id,loc,quantity);
    INSERT INTO grainledger.ledger_custody_edges VALUES(event_id,lot,aliquot_id,quantity);
    IF output_quantity>0 THEN
        PERFORM grainledger.ledger_post(event_id,remainder_id,loc,output_quantity);
        INSERT INTO grainledger.ledger_custody_edges VALUES(event_id,lot,remainder_id,output_quantity);
    END IF;
    -- Integer micro-kg largest remainder allocation; remainder is exact source-wise complement.
    WITH source_weights AS (
        SELECT (s->>'sourceLotId')::uuid source_lot_id,
               (CASE WHEN source_count=1 THEN source_position ELSE grainledger.ledger_kg(s->>'quantityKg') END*1000000)::bigint mass
        FROM jsonb_array_elements(current_sources) s
    ), bases AS (
        SELECT source_lot_id,div(quantity*1000000*mass,sum(mass) OVER()) base,
               mod(quantity*1000000*mass,sum(mass) OVER()) rem FROM source_weights
    ), ranked AS (
        SELECT *,row_number() OVER(ORDER BY rem DESC,source_lot_id) rn,
               quantity*1000000-sum(base) OVER() left_units FROM bases
    ), shares AS (
        SELECT source_lot_id,base+CASE WHEN rn<=left_units THEN 1 ELSE 0 END units FROM ranked
    )
    INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
    SELECT aliquot_id,source_lot_id,units/1000000::numeric FROM shares WHERE units>0;
    INSERT INTO grainledger.ledger_custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
    SELECT event_id,lot,aliquot_id,source_lot_id,quantity_kg FROM grainledger.ledger_contributions WHERE lot_id=aliquot_id;
    IF output_quantity>0 THEN
        INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
        SELECT remainder_id,prior.source_lot_id,
               CASE WHEN source_count=1 THEN source_position ELSE prior.quantity_kg END-COALESCE(aliquot.quantity_kg,0)
        FROM (SELECT (s->>'sourceLotId')::uuid source_lot_id,grainledger.ledger_kg(s->>'quantityKg') quantity_kg FROM jsonb_array_elements(current_sources) s) prior
        LEFT JOIN grainledger.ledger_contributions aliquot ON aliquot.lot_id=aliquot_id AND aliquot.source_lot_id=prior.source_lot_id
        WHERE
          (CASE WHEN source_count=1 THEN source_position ELSE prior.quantity_kg END)>COALESCE(aliquot.quantity_kg,0);
        INSERT INTO grainledger.ledger_custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,lot,remainder_id,source_lot_id,quantity_kg FROM grainledger.ledger_contributions WHERE lot_id=remainder_id;
    END IF;
    IF destination.mixed_bulk THEN
        UPDATE grainledger.ledger_mixed_bulk_pools SET lot_id=CASE WHEN output_quantity>0 THEN remainder_id ELSE NULL END,
            revision=revision+1 WHERE location_id=loc;
    END IF;
    IF retained THEN UPDATE grainledger.ledger_container_source_balances SET quantity_kg=0 WHERE lot_id=lot AND location_id=loc; END IF;
    RETURN actual_id;
END $_$;


-- Compute current source/ancestry restrictions once per distinct actual lot, retaining every manifest target.
CREATE OR REPLACE FUNCTION grainledger.ledger_container_targets(a uuid,root uuid,lot_ids uuid[]) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE r text:=grainledger.ledger_role(a); result jsonb; source_root boolean;
BEGIN
 IF r IS NULL OR r NOT IN ('CustomerManager','ExportStaff','Administrator') OR cardinality(lot_ids) NOT BETWEEN 1 AND 10000
  OR NOT root=ANY(lot_ids) OR EXISTS(SELECT 1 FROM unnest(lot_ids) x WHERE r<>'Administrator' AND NOT grainledger.ledger_can_read_lot(a,x)) THEN
  RAISE EXCEPTION 'Actual container targets unavailable in current trace scope' USING ERRCODE='42501'; END IF;
 SELECT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=root AND source_lot_id=root AND quantity_kg>0) INTO source_root;
 WITH linked AS MATERIALIZED (
  SELECT c.id,c.internal_code,c.status,l.quantity_kg,l.id line_id
  FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
  JOIN grainledger.exports_containers c ON c.id=h.container_id
  WHERE l.lot_id=ANY(lot_ids) AND (r='Administrator' OR grainledger.ledger_own(a,c.company_id))
   AND (NOT source_root OR EXISTS(SELECT 1 FROM grainledger.ledger_container_load_sources s WHERE s.line_id=l.id AND s.source_lot_id=root AND s.quantity_kg>0))
 ), totals AS (
  SELECT id,internal_code,status,sum(quantity_kg) linked_kg FROM linked GROUP BY id,internal_code,status
 ), all_manifest_lots AS MATERIALIZED (
  SELECT DISTINCT h.container_id,l.lot_id FROM grainledger.ledger_container_loads h
  JOIN totals t ON t.id=h.container_id JOIN grainledger.ledger_container_load_lines l ON l.load_id=h.id
 ), restrictions AS MATERIALIZED (
  SELECT lot_id,grainledger.ledger_container_lot_blocked(lot_id) blocked FROM (SELECT DISTINCT lot_id FROM all_manifest_lots) lots
 ) SELECT jsonb_build_object('complete',(SELECT count(*) FROM totals)<=10000,'items',
  CASE WHEN (SELECT count(*) FROM totals)>10000 THEN '[]'::jsonb ELSE coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',id,'code',internal_code,'status',status,
    'linkedLoadedKg',linked_kg::numeric(20,6)::text,'basis','ACTUAL_LOADING',
    'held',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id),
    'restricted',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id) OR EXISTS(SELECT 1 FROM all_manifest_lots l JOIN restrictions r ON r.lot_id=l.lot_id WHERE l.container_id=totals.id AND r.blocked)) ORDER BY internal_code,id) FROM totals),'[]'::jsonb) END) INTO result;
 IF NOT (result->>'complete')::boolean THEN RAISE EXCEPTION 'Actual container target set exceeds complete result limit' USING ERRCODE='22023'; END IF;
 RETURN result;
END $$;
