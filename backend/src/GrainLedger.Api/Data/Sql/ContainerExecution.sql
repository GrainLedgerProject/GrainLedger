-- Planned Export container is the authority; legacy preparation is retained separately.
CREATE TABLE grainledger.ledger_container_execution (
 container_id uuid PRIMARY KEY REFERENCES grainledger.exports_containers(id),
 facility_id uuid REFERENCES grainledger.ledger_facilities(id),
 proposal_id uuid REFERENCES grainledger.exports_blend_plan_proposals(id),
 container_number text CHECK (length(container_number) BETWEEN 1 AND 80),
 seal_number text CHECK (length(seal_number) BETWEEN 1 AND 80)
);
CREATE TABLE grainledger.ledger_container_loads (
 id uuid PRIMARY KEY REFERENCES grainledger.ledger_events(id),
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals(id),
 loaded_at timestamptz NOT NULL,
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 assessment jsonb NOT NULL
);
CREATE INDEX ledger_container_loads_parent ON grainledger.ledger_container_loads(container_id,recorded_at,id);
CREATE TABLE grainledger.ledger_container_load_lines (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 load_id uuid NOT NULL REFERENCES grainledger.ledger_container_loads(id),
 lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 location_id uuid NOT NULL REFERENCES grainledger.ledger_locations(id),
 quantity_kg numeric(20,6) NOT NULL CHECK (quantity_kg>0),
 UNIQUE(load_id,lot_id,location_id)
);
CREATE INDEX ledger_container_load_lines_lot ON grainledger.ledger_container_load_lines(lot_id,load_id);
CREATE TABLE grainledger.ledger_container_load_sources (
 line_id uuid NOT NULL REFERENCES grainledger.ledger_container_load_lines(id),
 source_lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 quantity_kg numeric(20,6) NOT NULL CHECK (quantity_kg>0),
 PRIMARY KEY(line_id,source_lot_id)
);
CREATE INDEX ledger_container_load_sources_source ON grainledger.ledger_container_load_sources(source_lot_id,line_id);
-- Private exact remaining source complement. External movements invalidate rather than invent it.
CREATE TABLE grainledger.ledger_container_source_balances (
 lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 location_id uuid NOT NULL REFERENCES grainledger.ledger_locations(id),
 source_lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg>=0),
 PRIMARY KEY(lot_id,location_id,source_lot_id)
);
CREATE TABLE grainledger.ledger_container_holds (
 id uuid PRIMARY KEY,
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 1 AND 1000),
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ledger_container_holds_parent ON grainledger.ledger_container_holds(container_id,recorded_at,id);
CREATE TABLE grainledger.ledger_container_closures (
 container_id uuid PRIMARY KEY REFERENCES grainledger.exports_containers(id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 dossier jsonb NOT NULL
);
CREATE TABLE grainledger.ledger_container_execution_requests (
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 operation_id uuid NOT NULL,
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 action text NOT NULL,
 payload jsonb NOT NULL,
 result jsonb NOT NULL,
 PRIMARY KEY(actor_id,operation_id)
);
CREATE TABLE grainledger.ledger_container_execution_events (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 kind text NOT NULL,
 details jsonb NOT NULL,
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ledger_container_execution_events_parent ON grainledger.ledger_container_execution_events(container_id,recorded_at,id);
DO $$ DECLARE name text; BEGIN
 FOREACH name IN ARRAY ARRAY['ledger_container_loads','ledger_container_load_lines','ledger_container_load_sources',
  'ledger_container_holds','ledger_container_closures','ledger_container_execution_requests','ledger_container_execution_events'] LOOP
  EXECUTE format('CREATE TRIGGER ledger_immutable BEFORE UPDATE OR DELETE ON grainledger.%I FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable()',name);
 END LOOP;
END $$;

-- Container checks include positive source/physical ancestry, without creating child HOLD records.
CREATE FUNCTION grainledger.ledger_container_lot_blocked(target uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 WITH RECURSIVE paths(lot_id,source_lot_id) AS (
  SELECT target,source_lot_id FROM grainledger.ledger_contributions WHERE lot_id=target AND quantity_kg>0
  UNION
  SELECT parent.id,path.source_lot_id FROM paths path JOIN grainledger.ledger_lots child ON child.id=path.lot_id
   CROSS JOIN LATERAL (
    SELECT edge.input_lot_id id FROM grainledger.ledger_custody_edges edge WHERE edge.output_lot_id=child.id AND edge.quantity_kg>0
    UNION
    SELECT (input->>'lotId')::uuid FROM grainledger.ledger_events event
     CROSS JOIN LATERAL jsonb_array_elements(coalesce(event.details->'resolvedInputs',event.details->'content'->'inputs','[]'::jsonb)) input
     WHERE event.operation_id=child.operation_id AND event.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING') AND (input->>'quantityKg')::numeric>0
   ) parent JOIN grainledger.ledger_contributions contribution ON contribution.lot_id=parent.id
    AND contribution.source_lot_id=path.source_lot_id AND contribution.quantity_kg>0
 ) SELECT grainledger.ledger_blocked(target) OR EXISTS(
   SELECT 1 FROM paths WHERE grainledger.ledger_blocked(paths.lot_id) OR grainledger.ledger_blocked(paths.source_lot_id))
$$;
REVOKE ALL ON FUNCTION grainledger.ledger_container_lot_blocked(uuid) FROM PUBLIC;

CREATE FUNCTION grainledger.ledger_container_allocate_sources(amount numeric,total numeric,sources jsonb)
RETURNS TABLE(source_lot_id uuid,quantity_kg numeric)
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 WITH raw AS (
  SELECT (s->>'sourceLotId')::uuid source_lot_id,
    amount*1000000*(s->>'quantityKg')::numeric*1000000 numerator,total*1000000 denominator
  FROM jsonb_array_elements(sources) s
 ), ranked AS (
  SELECT *,div(numerator,denominator) base,
   row_number() OVER(ORDER BY mod(numerator,denominator) DESC,source_lot_id) rn,
   amount*1000000-sum(div(numerator,denominator)) OVER() remaining FROM raw
 ) SELECT source_lot_id,(base+CASE WHEN rn<=remaining THEN 1 ELSE 0 END)/1000000
   FROM ranked WHERE base+CASE WHEN rn<=remaining THEN 1 ELSE 0 END>0
$$;
REVOKE ALL ON FUNCTION grainledger.ledger_container_allocate_sources(numeric,numeric,jsonb) FROM PUBLIC;

CREATE FUNCTION grainledger.ledger_export_container_read(a uuid,target uuid,action text DEFAULT 'detail',page_number integer DEFAULT 1,page_size integer DEFAULT 25) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; ext grainledger.ledger_container_execution; r text; result jsonb;
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
   FROM (SELECT id,kind,actor_id,details,recorded_at FROM grainledger.ledger_container_execution_events WHERE container_id=target
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
   'lines',(SELECT jsonb_agg(jsonb_build_object('lotId',l.lot_id,'lotCode',x.code,'locationId',l.location_id,'quantityKg',l.quantity_kg::text)
    ORDER BY l.lot_id,l.location_id) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_lots x ON x.id=l.lot_id WHERE l.load_id=h.id))
   ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_loads h WHERE h.container_id=target),'[]'::jsonb),
  'approval',(SELECT jsonb_build_object('proposalId',p.id,'planId',p.plan_id,'decisionVersion',d.version,
     'snapshot',p.snapshot_json,'previewRequest',v.request_json,'demandSnapshot',plan.demand_snapshot::text,
     'specificationId',plan.specification_id,'mode',p.snapshot_json->'selection'->>'mode',
     'allocations',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',res.lot_id,'lotCode',lot.code,'locationId',res.location_id,
       'restricted',grainledger.ledger_container_lot_blocked(res.lot_id),
       'loadingPositionReady',EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id JOIN grainledger.ledger_facilities f ON f.id=loc.facility_id
         WHERE pos.lot_id=res.lot_id AND pos.location_id=res.location_id AND pos.quantity_kg>0 AND NOT loc.is_deleted AND NOT f.is_deleted
          AND NOT loc.mixed_bulk AND NOT grainledger.ledger_mixed_bulk_custody_derived(pos.lot_id)
          AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)
          AND (EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id) OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 OR
           ((SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 AND
            (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id)=pos.quantity_kg))
          AND (NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)
           OR (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)=pos.quantity_kg)),
       'reservedKg',res.quantity_kg::text,'remainingKg',(res.quantity_kg-coalesce((SELECT sum(quantity_kg) FROM grainledger.ledger_blend_reservation_events ev
         WHERE ev.proposal_id=res.proposal_id AND ev.lot_id=res.lot_id AND ev.location_id=res.location_id),0))::numeric(20,6)::text)
       ORDER BY res.lot_id,res.location_id) FROM grainledger.ledger_blend_reservations res JOIN grainledger.ledger_lots lot ON lot.id=res.lot_id WHERE res.proposal_id=p.id),'[]'::jsonb))
    FROM grainledger.exports_active_blend_approvals active
    JOIN grainledger.exports_blend_plan_proposals p ON p.id=active.proposal_id
    JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id
    JOIN grainledger.exports_blend_plan_previews v ON v.id=p.preview_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=p.id AND d.version=active.decision_version AND d.action='APPROVED'
    WHERE active.container_id=target AND active.company_id=c.company_id),
  'dossierAvailable',EXISTS(SELECT 1 FROM grainledger.ledger_container_closures WHERE container_id=target)) INTO result;
 result:=result||jsonb_build_object('remainingKg',(c.target_mass_kg-(result->>'loadedKg')::numeric)::numeric(20,6)::text);
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

CREATE FUNCTION grainledger.ledger_export_container_command(a uuid,target uuid,action text,p jsonb,basis jsonb) RETURNS jsonb
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
  IF selected_proposal IS NULL OR (ext.proposal_id IS NOT NULL AND ext.proposal_id<>selected_proposal) THEN RAISE EXCEPTION 'Current pinned approval required' USING ERRCODE='23514'; END IF;
  IF action='load' THEN
   IF selected_proposal IS DISTINCT FROM (p->>'proposalId')::uuid OR NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals
    WHERE proposal_id=selected_proposal AND decision_version=(p->>'decisionVersion')::integer) THEN RAISE EXCEPTION 'Approval decision changed' USING ERRCODE='23514'; END IF;
   SELECT (v.request_json->>'FacilityId')::uuid INTO facility FROM grainledger.exports_blend_plan_proposals prop
    JOIN grainledger.exports_blend_plan_previews v ON v.id=prop.preview_id
    WHERE prop.id=selected_proposal AND prop.snapshot_json->'selection'->>'mode'='DIRECT_STOCK';
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
    SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid AND owner_id=company;
    SELECT * INTO loc FROM grainledger.ledger_locations WHERE id=(line->>'locationId')::uuid AND facility_id=facility;
    IF lot.id IS NULL OR loc.id IS NULL OR (loc.owner_id IS NOT NULL AND loc.owner_id<>company) THEN RAISE EXCEPTION 'Loading position outside approved scope' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.ledger_require_active_location(loc.id);
    IF loc.mixed_bulk OR grainledger.ledger_mixed_bulk_custody_derived(lot.id) THEN RAISE EXCEPTION 'Pool/custody reservation handoff is not available for loading' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_container_lot_blocked(lot.id) THEN RAISE EXCEPTION 'Loading lot is restricted' USING ERRCODE='23514'; END IF;
    SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=loc.id FOR UPDATE;
    SELECT res.quantity_kg-coalesce((SELECT sum(quantity_kg) FROM grainledger.ledger_blend_reservation_events e
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
 result:=grainledger.ledger_export_container_read(a,target,'detail');
 INSERT INTO grainledger.ledger_container_execution_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;

-- Any actual container consumption makes correction/reversal of the originating package unsafe.
CREATE FUNCTION grainledger.ledger_protect_loaded_stock() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF TG_TABLE_NAME='ledger_corrections' THEN
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_lots x ON x.id=l.lot_id WHERE x.operation_id=NEW.operation_id) THEN
   RAISE EXCEPTION 'Container-loaded output requires an explicit compensation workflow' USING ERRCODE='23514'; END IF;
 ELSE
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_lots x ON x.id=l.lot_id WHERE x.receipt_id=NEW.receipt_id) THEN
   RAISE EXCEPTION 'Container-loaded stock cannot be reversed as unused stock' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER ledger_protect_loaded_stock BEFORE INSERT ON grainledger.ledger_corrections FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_loaded_stock();
CREATE TRIGGER ledger_protect_loaded_stock BEFORE INSERT ON grainledger.ledger_receipt_reversals FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_loaded_stock();

REVOKE ALL ON TABLE grainledger.ledger_container_execution,grainledger.ledger_container_loads,
 grainledger.ledger_container_load_lines,grainledger.ledger_container_load_sources,grainledger.ledger_container_source_balances,
 grainledger.ledger_container_holds,grainledger.ledger_container_closures,grainledger.ledger_container_execution_requests,
 grainledger.ledger_container_execution_events FROM PUBLIC;
REVOKE ALL ON FUNCTION grainledger.ledger_export_container_read(uuid,uuid,text,integer,integer),grainledger.ledger_export_container_command(uuid,uuid,text,jsonb,jsonb),grainledger.ledger_protect_loaded_stock() FROM PUBLIC;

CREATE FUNCTION grainledger.ledger_container_targets(a uuid,root uuid,lot_ids uuid[]) RETURNS jsonb
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
 ) SELECT jsonb_build_object('complete',(SELECT count(*) FROM totals)<=10000,'items',
  CASE WHEN (SELECT count(*) FROM totals)>10000 THEN '[]'::jsonb ELSE coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',id,'code',internal_code,'status',status,
    'linkedLoadedKg',linked_kg::numeric(20,6)::text,'basis','ACTUAL_LOADING',
    'held',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id),
    'restricted',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id) OR EXISTS(SELECT 1 FROM grainledger.ledger_container_loads h JOIN grainledger.ledger_container_load_lines l ON l.load_id=h.id WHERE h.container_id=totals.id AND grainledger.ledger_container_lot_blocked(l.lot_id))) ORDER BY internal_code,id) FROM totals),'[]'::jsonb) END) INTO result;
 IF NOT (result->>'complete')::boolean THEN RAISE EXCEPTION 'Actual container target set exceeds complete result limit' USING ERRCODE='22023'; END IF;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_container_targets(uuid,uuid,uuid[]) FROM PUBLIC;
