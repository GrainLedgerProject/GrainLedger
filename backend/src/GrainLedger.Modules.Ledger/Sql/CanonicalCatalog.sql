-- Catalog owns commodity identity and display labels. Existing Ledger IDs and mappings never change.
-- Historical unmapped names remain independent; never infer a mapping from names or codes.
ALTER TABLE ledger.commodities DROP CONSTRAINT commodities_name_key;
ALTER TABLE ledger.commodities DROP CONSTRAINT commodities_name_check;
ALTER TABLE ledger.commodities ADD CONSTRAINT commodities_name_check CHECK(length(name)>0);

CREATE FUNCTION ledger.provision_catalog_commodity(c uuid) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE commodity uuid;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT ledger_commodity_id INTO commodity FROM ledger.catalog_commodities WHERE catalog_commodity_id=c;
    IF FOUND THEN RETURN commodity; END IF;
    commodity:=gen_random_uuid();
    INSERT INTO ledger.commodities(id,name) SELECT commodity,name FROM catalog.commodities WHERE id=c;
    IF NOT FOUND THEN RAISE EXCEPTION 'Catalog commodity not found' USING ERRCODE='23514'; END IF;
    INSERT INTO ledger.catalog_commodities VALUES(c,commodity);
    RETURN commodity;
END $$;
REVOKE ALL ON FUNCTION ledger.provision_catalog_commodity(uuid) FROM PUBLIC;

CREATE FUNCTION ledger.provision_catalog_commodities(a uuid) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE c uuid;
BEGIN
    IF ledger.role(a) IS DISTINCT FROM 'Administrator' THEN
        RAISE EXCEPTION 'Catalog provisioning authority denied' USING ERRCODE='42501';
    END IF;
    PERFORM ledger.lock_writes();
    FOR c IN SELECT cc.id FROM catalog.commodities cc
        WHERE NOT EXISTS(SELECT 1 FROM ledger.catalog_commodities m WHERE m.catalog_commodity_id=cc.id)
        ORDER BY cc.id
    LOOP
        PERFORM ledger.provision_catalog_commodity(c);
    END LOOP;
END $$;
REVOKE ALL ON FUNCTION ledger.provision_catalog_commodities(uuid) FROM PUBLIC;

-- Explicit owner migration provisions existing Catalog rows, without touching stock/history.
DO $$ DECLARE c uuid; BEGIN
    FOR c IN SELECT id FROM catalog.commodities ORDER BY id LOOP
        PERFORM ledger.provision_catalog_commodity(c);
    END LOOP;
END $$;

CREATE OR REPLACE FUNCTION ledger.read(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    result jsonb;
    requested_lot uuid:=NULLIF(p->>'lotId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_owner uuid:=NULLIF(p->>'ownerId','')::uuid;
BEGIN
    IF ledger.role(a) IS NULL THEN
        RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501';
    END IF;

    -- Receiving lookups expose names and IDs, never occupancy or staff configuration.
    IF action IN ('setup','lookups') THEN
        IF action='setup' AND ledger.role(a) NOT IN ('Administrator','MillManager','MillOperator') THEN
            RAISE EXCEPTION 'Setup access denied' USING ERRCODE='42501';
        END IF;
        SELECT jsonb_build_object(
            'facilities',coalesce((
                SELECT jsonb_agg(jsonb_build_object('id',x.id,'name',name))
                FROM ledger.facilities x
                WHERE ledger.role(a) IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff')
                   OR ledger.assigned(a,x.id)
            ),'[]'::jsonb),
            'locations',coalesce((
                SELECT jsonb_agg(jsonb_build_object(
                    'id',x.id,'facilityId',facility_id,'name',name,'kind',kind
                ) || CASE WHEN action='setup' THEN
                    jsonb_build_object('ownerId',owner_id,'cleared',cleared)
                ELSE '{}'::jsonb END)
                FROM ledger.locations x
                WHERE ledger.role(a) IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff')
                   OR ledger.assigned(a,facility_id)
            ),'[]'::jsonb),
            'commodities',coalesce((
                SELECT jsonb_agg(jsonb_build_object(
                    'id',lc.id,'name',coalesce(cc.name,lc.name),
                    'code',cc.code,'catalogId',cc.id
                ) ORDER BY coalesce(cc.name,lc.name),lc.id)
                FROM ledger.commodities lc
                LEFT JOIN ledger.catalog_commodities m ON m.ledger_commodity_id=lc.id
                LEFT JOIN catalog.commodities cc ON cc.id=m.catalog_commodity_id
            ),'[]'::jsonb),
            'owners',coalesce((
                SELECT jsonb_agg(jsonb_build_object('id',o."Id",'name',o."Name") ORDER BY o."Name",o."Id")
                FROM identity.organizations o
                WHERE o."Kind"='Customer' AND (
                    ledger.own(a,o."Id")
                    OR EXISTS(SELECT 1 FROM ledger.lots l WHERE l.owner_id=o."Id" AND ledger.can_read_lot(a,l.id))
                    OR EXISTS(SELECT 1 FROM ledger.operations op WHERE op.owner_id=o."Id" AND ledger.can_read_operation(a,op.id))
                )
            ),'[]'::jsonb),
            'equipment',coalesce((
                SELECT jsonb_agg(jsonb_build_object('id',id,'facilityId',facility_id,'name',name))
                FROM ledger.equipment
                WHERE ledger.role(a)='Administrator'
                   OR ledger.role(a) IN ('MillManager','MillOperator') AND ledger.assigned(a,facility_id)
            ),'[]'::jsonb)
        ) INTO result;
        IF action='setup' THEN
            result:=result||jsonb_build_object(
                'assignments',coalesce((
                    SELECT jsonb_agg(jsonb_build_object('userId',user_id,'facilityId',facility_id))
                    FROM ledger.assignments WHERE ledger.role(a)='Administrator' OR user_id=a
                ),'[]'::jsonb),
                'policies',coalesce((
                    SELECT jsonb_agg(jsonb_build_object(
                        'id',id,'facilityId',facility_id,'kind',kind,
                        'tolerancePercent',tolerance_percent::text,
                        'effectiveFrom',effective_from,'evidenceId',evidence_id
                    )) FROM ledger.policies
                    WHERE ledger.role(a)='Administrator' OR ledger.assigned(a,facility_id)
                ),'[]'::jsonb),
                'compatibilities',coalesce((
                    SELECT jsonb_agg(jsonb_build_object(
                        'id',id,'facilityId',facility_id,'inputProducts',input_products,
                        'outputCommodityId',output_commodity_id,
                        'effectiveFrom',effective_from,'evidenceId',evidence_id
                    )) FROM ledger.compatibilities
                    WHERE ledger.role(a)='Administrator' OR ledger.assigned(a,facility_id)
                ),'[]'::jsonb)
            );
        END IF;
        RETURN result;
    END IF;

    IF action='lots' THEN
        SELECT coalesce(jsonb_agg(ledger.lot_json(a,x.id,false) ORDER BY x.code),'[]'::jsonb)
        INTO result FROM ledger.lots x
        WHERE ledger.can_read_lot(a,x.id)
          AND (requested_owner IS NULL OR owner_id=requested_owner)
          AND (p->>'search' IS NULL OR code ILIKE '%'||(p->>'search')||'%')
          AND (requested_facility IS NULL OR EXISTS (
              SELECT 1 FROM ledger.positions pos JOIN ledger.locations lc ON lc.id=pos.location_id
              WHERE pos.lot_id=x.id AND lc.facility_id=requested_facility
                AND (ledger.own(a,x.owner_id) OR ledger.assigned(a,requested_facility))
          ));
        RETURN result;
    END IF;
    IF action='lot' THEN
        IF NOT ledger.can_read_lot(a,requested_lot) THEN
            RAISE EXCEPTION 'Lot access denied' USING ERRCODE='42501';
        END IF;
        RETURN ledger.lot_json(a,requested_lot,true)||jsonb_build_object('history',ledger.read(a,'history',p));
    END IF;
    IF action IN ('operations','operation') THEN
        SELECT coalesce(jsonb_agg(ledger.operation_json(x.id) ORDER BY occurred_at DESC),'[]'::jsonb)
        INTO result FROM ledger.operations x
        WHERE ledger.can_read_operation(a,x.id) AND (p->>'id' IS NULL OR x.id=(p->>'id')::uuid);
        IF action='operation' THEN
            IF jsonb_array_length(result)=0 THEN
                RAISE EXCEPTION 'Operation access denied' USING ERRCODE='42501';
            END IF;
            RETURN result->0;
        END IF;
        RETURN result;
    END IF;
    IF action='holds' THEN
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',id,'lotId',lot_id,'reason',reason,'status',status,
            'evidenceId',CASE WHEN ledger.role(a)<>'MillOperator' THEN evidence_id END,
            'response',response,'createdAt',created_at
        )),'[]'::jsonb) INTO result FROM ledger.holds WHERE ledger.can_read_lot(a,lot_id);
        RETURN result;
    END IF;
    IF action='corrections' THEN
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',id,'operationId',operation_id,'reason',reason,'status',status,
            'evidenceId',CASE WHEN ledger.role(a)<>'MillOperator' THEN evidence_id END,
            'reconciliation',CASE WHEN ledger.role(a)<>'MillOperator' THEN reconciliation END,
            'replacementOperationId',replacement_operation_id
        )),'[]'::jsonb) INTO result FROM ledger.corrections
        WHERE ledger.can_read_operation(a,operation_id);
        RETURN result;
    END IF;
    IF action='notifications' THEN
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',id,'holdId',hold_id,'lotId',lot_id,'message',message,
            'createdAt',created_at,'readAt',read_at
        ) ORDER BY created_at DESC),'[]'::jsonb) INTO result FROM ledger.notifications
        WHERE user_id=a AND ledger.can_read_lot(a,lot_id);
        RETURN result;
    END IF;

    -- Posting visibility is location scoped; operator details contain no source documents/evidence.
    IF action='history' THEN
        IF requested_lot IS NOT NULL AND NOT ledger.can_read_lot(a,requested_lot) THEN
            RAISE EXCEPTION 'History lot access denied' USING ERRCODE='42501';
        END IF;
        IF requested_lot IS NULL THEN
            SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',ee.id,'postingId',pp.id,'kind',ee.kind,'occurredAt',ee.occurred_at,
                'actorId',ee.actor_id,'operationId',ee.operation_id,
                'lotId',coalesce(pp.lot_id,ee.lot_id),'locationId',pp.location_id,
                'quantityKg',pp.quantity_kg::text,'reversesPostingId',pp.reverses_posting_id,
                'details',CASE WHEN ledger.role(a)='MillOperator' THEN
                    jsonb_build_object('declaredLossKg',ee.details->>'declaredLossKg','varianceKg',ee.details->>'varianceKg')
                ELSE ee.details END
            ) ORDER BY ee.occurred_at,ee.id,pp.id),'[]'::jsonb)
            INTO result FROM ledger.events ee
            LEFT JOIN ledger.postings pp ON pp.event_id=ee.id
            LEFT JOIN ledger.locations lc ON lc.id=pp.location_id
            WHERE (requested_facility IS NULL OR lc.facility_id=requested_facility OR EXISTS (
                SELECT 1 FROM ledger.operations WHERE id=ee.operation_id AND facility_id=requested_facility
            ))
              AND CASE
                  WHEN pp.id IS NOT NULL THEN ledger.can_read_lot(a,pp.lot_id) AND (
                      ledger.own(a,(SELECT owner_id FROM ledger.lots WHERE id=pp.lot_id))
                      OR ledger.assigned(a,lc.facility_id)
                  )
                  WHEN ee.lot_id IS NOT NULL THEN ledger.can_read_lot(a,ee.lot_id)
                  ELSE ledger.can_read_operation(a,ee.operation_id)
              END;
        ELSE
            WITH candidate_event_ids AS MATERIALIZED (
                SELECT id FROM ledger.events WHERE lot_id=requested_lot
                UNION
                SELECT id FROM ledger.events
                WHERE operation_id=(SELECT operation_id FROM ledger.lots WHERE id=requested_lot)
                UNION
                SELECT event_id FROM ledger.postings WHERE lot_id=requested_lot
            )
            SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',ee.id,'postingId',pp.id,'kind',ee.kind,'occurredAt',ee.occurred_at,
                'actorId',ee.actor_id,'operationId',ee.operation_id,
                'lotId',coalesce(pp.lot_id,ee.lot_id),'locationId',pp.location_id,
                'quantityKg',pp.quantity_kg::text,'reversesPostingId',pp.reverses_posting_id,
                'details',CASE WHEN ledger.role(a)='MillOperator' THEN
                    jsonb_build_object('declaredLossKg',ee.details->>'declaredLossKg','varianceKg',ee.details->>'varianceKg')
                ELSE ee.details END
            ) ORDER BY ee.occurred_at,ee.id,pp.id),'[]'::jsonb)
            INTO result FROM candidate_event_ids ci
            JOIN ledger.events ee ON ee.id=ci.id
            LEFT JOIN ledger.postings pp ON pp.event_id=ee.id
            LEFT JOIN ledger.locations lc ON lc.id=pp.location_id
            WHERE (coalesce(pp.lot_id,ee.lot_id)=requested_lot
                OR ee.operation_id=(SELECT operation_id FROM ledger.lots WHERE id=requested_lot))
              AND (requested_facility IS NULL OR lc.facility_id=requested_facility OR EXISTS (
                  SELECT 1 FROM ledger.operations WHERE id=ee.operation_id AND facility_id=requested_facility
              ))
              AND CASE
                  WHEN pp.id IS NOT NULL THEN ledger.can_read_lot(a,pp.lot_id) AND (
                      ledger.own(a,(SELECT owner_id FROM ledger.lots WHERE id=pp.lot_id))
                      OR ledger.assigned(a,lc.facility_id)
                  )
                  WHEN ee.lot_id IS NOT NULL THEN ledger.can_read_lot(a,ee.lot_id)
                  ELSE ledger.can_read_operation(a,ee.operation_id)
              END;
        END IF;
        RETURN result;
    END IF;
    RAISE EXCEPTION 'Unsupported ledger read' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION ledger.read(uuid,text,jsonb) FROM PUBLIC;


CREATE OR REPLACE FUNCTION ledger.command(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    command_id uuid;
    facility uuid;
    owner uuid;
    location ledger.locations;
    lot ledger.lots;
    operation ledger.operations;
    correction ledger.corrections;
    item jsonb;
    quantity numeric;
    event_id uuid:=gen_random_uuid();
    hold ledger.holds;
    original_posting record;
    physical_position record;
    offset_quantity numeric;
    evidence_bytes bytea;
    evidence_hash text;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS NULL THEN
        RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501';
    END IF;
    command_id:=NULLIF(p->>'id','')::uuid;

    -- Setup does not confer operational, reconciliation or HOLD authority.
    IF action IN ('facilities','locations','equipment','assignments','policies','compatibilities','clearance') THEN
        IF ledger.role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        CASE action
            WHEN 'facilities' THEN
                INSERT INTO ledger.facilities VALUES(command_id,p->>'name');
            WHEN 'locations' THEN
                INSERT INTO ledger.locations(id,facility_id,name,kind)
                VALUES(command_id,(p->>'facilityId')::uuid,p->>'name',p->>'kind');
            WHEN 'equipment' THEN
                INSERT INTO ledger.equipment VALUES(command_id,(p->>'facilityId')::uuid,p->>'name');
            WHEN 'assignments' THEN
                IF NOT EXISTS (
                    SELECT 1 FROM identity.users WHERE "Id"=(p->>'userId')::uuid
                      AND "IsActive" AND "Role" IN ('MillOperator','MillManager')
                ) THEN
                    RAISE EXCEPTION 'Only active operational mill staff may be assigned' USING ERRCODE='23514';
                END IF;
                INSERT INTO ledger.assignments VALUES((p->>'userId')::uuid,(p->>'facilityId')::uuid)
                ON CONFLICT DO NOTHING;
            WHEN 'policies' THEN
                facility:=(p->>'facilityId')::uuid;
                SELECT owner_id INTO STRICT owner FROM ledger.evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=facility;
                PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,owner,facility);
                INSERT INTO ledger.policies VALUES(command_id,facility,p->>'kind',
                    ledger.kg(p->>'tolerancePercent'),(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid);
            WHEN 'compatibilities' THEN
                facility:=(p->>'facilityId')::uuid;
                SELECT owner_id INTO STRICT owner FROM ledger.evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=facility;
                PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,owner,facility);
                IF jsonb_typeof(p->'inputProducts') IS DISTINCT FROM 'array' THEN
                    RAISE EXCEPTION 'Explicit blend compatibility products required' USING ERRCODE='23514';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM jsonb_array_elements_text(p->'inputProducts') product
                    WHERE product !~ '^[0-9a-fA-F-]{36}:(HEAD_RICE|BROKEN_RICE|BLENDED_RICE)$'
                       OR NOT EXISTS (
                           SELECT 1 FROM ledger.commodities WHERE id=split_part(product,':',1)::uuid
                       )
                ) THEN
                    RAISE EXCEPTION 'Blend compatibility requires explicit registered commodity/product identities'
                        USING ERRCODE='23514';
                END IF;
                INSERT INTO ledger.compatibilities VALUES(command_id,facility,
                    ARRAY(SELECT jsonb_array_elements_text(p->'inputProducts')),
                    (p->>'outputCommodityId')::uuid,(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid);
            WHEN 'clearance' THEN
                SELECT * INTO STRICT location FROM ledger.locations WHERE id=command_id FOR UPDATE;
                IF EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=location.id AND quantity_kg>0) THEN
                    RAISE EXCEPTION 'Clearance requires empty stock' USING ERRCODE='23514';
                END IF;
                SELECT owner_id INTO STRICT owner FROM ledger.evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=location.facility_id;
                PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,owner,location.facility_id);
                IF p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                    RAISE EXCEPTION 'Clearance confirmation reason required' USING ERRCODE='23514';
                END IF;
                UPDATE ledger.locations SET cleared=true,owner_id=NULL WHERE id=location.id;
                INSERT INTO ledger.events(kind,actor_id,details) VALUES('CLEARANCE',a,p);
        END CASE;
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='evidence' THEN
        facility:=(p->>'facilityId')::uuid;
        owner:=(p->>'ownerId')::uuid;
        IF ledger.role(a)<>'Administrator' AND NOT ledger.own(a,owner) THEN
            PERFORM ledger.require_staff(a,facility,ARRAY['MillManager','MillOperator']);
        END IF;
        IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=owner) THEN
            RAISE EXCEPTION 'Unknown evidence owner' USING ERRCODE='23514';
        END IF;
        evidence_bytes:=decode(p->>'contentBase64','base64');
        evidence_hash:=encode(sha256(evidence_bytes),'hex');
        -- Same immutable upload can be retried after a timeout; content/scope cannot be overwritten.
        IF EXISTS(SELECT 1 FROM ledger.evidence WHERE id=command_id) THEN
            IF NOT EXISTS (
                SELECT 1 FROM ledger.evidence WHERE id=command_id AND actor_id=a
                  AND owner_id=owner AND facility_id=facility AND file_name=p->>'fileName'
                  AND content_type=p->>'contentType' AND content=evidence_bytes
            ) THEN
                RAISE EXCEPTION 'Evidence retry conflicts with immutable content or scope' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id,'sha256',evidence_hash);
        END IF;
        INSERT INTO ledger.evidence VALUES(command_id,owner,facility,p->>'fileName',p->>'contentType',
                                           evidence_bytes,evidence_hash,a,now());
        RETURN jsonb_build_object('id',command_id,'sha256',evidence_hash);
    END IF;

    IF action='draft' THEN
        facility:=(p->>'facilityId')::uuid;
        owner:=(p->>'ownerId')::uuid;
        PERFORM ledger.require_staff(a,facility,ARRAY['MillOperator']);
        IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=owner AND "Kind"='Customer') THEN
            RAISE EXCEPTION 'Operation owner must be customer' USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM ledger.operations old WHERE old.id=command_id
              AND (old.status<>'DRAFT' OR old.owner_id<>owner OR old.facility_id<>facility)
        ) THEN
            RAISE EXCEPTION 'Committed operation immutable or draft scope conflict' USING ERRCODE='23514';
        END IF;
        IF jsonb_typeof(p->'inputs') IS DISTINCT FROM 'array'
           OR jsonb_typeof(p->'outputs') IS DISTINCT FROM 'array' THEN
            RAISE EXCEPTION 'Input/output arrays required' USING ERRCODE='23514';
        END IF;
        IF jsonb_array_length(p->'inputs')=0 OR jsonb_array_length(p->'outputs')=0 THEN
            RAISE EXCEPTION 'Nonempty input/output arrays required; loss-only operations unsupported'
                USING ERRCODE='23514';
        END IF;
        FOR item IN SELECT value FROM jsonb_array_elements(p->'inputs') LOOP
            IF NOT EXISTS (
                SELECT 1 FROM ledger.lots source JOIN ledger.positions pos ON pos.lot_id=source.id
                JOIN ledger.locations loc ON loc.id=pos.location_id
                WHERE source.id=(item->>'lotId')::uuid AND source.owner_id=owner
                  AND loc.id=(item->>'locationId')::uuid AND loc.facility_id=facility
            ) THEN
                RAISE EXCEPTION 'Draft input outside persisted owner/facility scope' USING ERRCODE='42501';
            END IF;
            PERFORM ledger.kg(item->>'quantityKg');
        END LOOP;
        FOR item IN SELECT value FROM jsonb_array_elements(p->'outputs') LOOP
            PERFORM ledger.kg(item->>'quantityKg');
            IF NOT EXISTS (
                SELECT 1 FROM ledger.locations WHERE id=(item->>'locationId')::uuid
                  AND ledger.assigned(a,facility_id)
            ) THEN
                RAISE EXCEPTION 'Draft output outside assigned facility' USING ERRCODE='42501';
            END IF;
        END LOOP;
        PERFORM ledger.kg(p->>'declaredLossKg');
        INSERT INTO ledger.operations(id,owner_id,facility_id,kind,occurred_at,equipment_id,content,version)
        VALUES(command_id,owner,facility,p->>'kind',(p->>'occurredAt')::timestamptz,
               (p->>'equipmentId')::uuid,p,gen_random_uuid())
        ON CONFLICT ON CONSTRAINT operations_pkey DO UPDATE SET
            kind=excluded.kind,occurred_at=excluded.occurred_at,equipment_id=excluded.equipment_id,
            content=excluded.content,version=excluded.version,confirmed_version=NULL,
            confirmed_by=NULL,confirmation_evidence_id=NULL;
        WITH invalidated AS (
            UPDATE ledger.corrections SET status='REQUESTED'
            WHERE replacement_operation_id=command_id AND status='RECONCILED'
            RETURNING operation_id,id
        ) INSERT INTO ledger.events(kind,actor_id,operation_id,details)
        SELECT 'RECONCILIATION_INVALIDATED',a,operation_id,
               jsonb_build_object('correctionId',id,'replacementOperationId',command_id) FROM invalidated;
        RETURN ledger.operation_json(command_id);
    END IF;

    IF action IN ('confirm','commit') THEN
        SELECT * INTO STRICT operation FROM ledger.operations WHERE id=command_id FOR UPDATE;
        IF action='commit' THEN RETURN ledger.commit_operation(a,command_id); END IF;
        PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillManager']);
        PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,operation.owner_id,operation.facility_id);
        IF operation.status<>'DRAFT' OR operation.version IS DISTINCT FROM (p->>'version')::uuid THEN
            RAISE EXCEPTION 'Confirmation review stale or version omitted' USING ERRCODE='23514';
        END IF;
        UPDATE ledger.operations SET confirmed_version=version,confirmed_by=a,
            confirmation_evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
        INSERT INTO ledger.events(kind,actor_id,operation_id,details)
        VALUES('VARIANCE_CONFIRMATION',a,command_id,p||jsonb_build_object('reviewedContent',operation.content));
        RETURN ledger.operation_json(command_id);
    END IF;

    IF action='transfer' THEN
        SELECT * INTO STRICT lot FROM ledger.lots WHERE id=(p->>'lotId')::uuid FOR UPDATE;
        SELECT * INTO STRICT location FROM ledger.locations WHERE id=(p->>'fromLocationId')::uuid FOR UPDATE;
        SELECT facility_id INTO STRICT facility FROM ledger.locations WHERE id=(p->>'toLocationId')::uuid;
        IF EXISTS(SELECT 1 FROM ledger.events WHERE id=command_id) THEN
            PERFORM ledger.require_staff(a,location.facility_id,
                CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
            PERFORM ledger.require_staff(a,facility,
                CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
            IF NOT EXISTS(SELECT 1 FROM ledger.events WHERE id=command_id AND kind='TRANSFER' AND details=p) THEN
                RAISE EXCEPTION 'Transfer retry payload conflicts' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF p->>'holdId' IS NULL THEN
            PERFORM ledger.require_staff(a,location.facility_id,ARRAY['MillOperator']);
            PERFORM ledger.require_staff(a,facility,ARRAY['MillOperator']);
            IF ledger.blocked(lot.id) THEN
                RAISE EXCEPTION 'Lot restricted from transfer' USING ERRCODE='23514';
            END IF;
        ELSE
            PERFORM ledger.require_staff(a,location.facility_id,ARRAY['MillManager']);
            PERFORM ledger.require_staff(a,facility,ARRAY['MillManager']);
            IF NOT EXISTS(SELECT 1 FROM ledger.holds WHERE id=(p->>'holdId')::uuid AND lot_id=lot.id)
               OR NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=(p->>'toLocationId')::uuid AND kind='ISOLATION') THEN
                RAISE EXCEPTION 'HOLD transfer requires active HOLD and isolation location' USING ERRCODE='23514';
            END IF;
            IF ledger.receiving_restricted(lot.id) OR EXISTS (
                SELECT 1 FROM ledger.corrections WHERE operation_id=lot.operation_id
                  AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS(SELECT 1 FROM ledger.receipt_correction_blocks WHERE receipt_id=lot.receipt_id) THEN
                RAISE EXCEPTION 'Independent correction/receiving block prevents isolation movement'
                    USING ERRCODE='23514';
            END IF;
        END IF;
        quantity:=ledger.kg(p->>'quantityKg');
        IF quantity<=0 OR location.id=(p->>'toLocationId')::uuid THEN
            RAISE EXCEPTION 'Positive transfer between different locations required' USING ERRCODE='23514';
        END IF;
        PERFORM ledger.credit_location((p->>'toLocationId')::uuid,lot.owner_id);
        INSERT INTO ledger.events(id,kind,actor_id,lot_id,details,occurred_at)
        VALUES(command_id,'TRANSFER',a,lot.id,p,(p->>'occurredAt')::timestamptz);
        PERFORM ledger.post(command_id,lot.id,location.id,-quantity);
        PERFORM ledger.post(command_id,lot.id,(p->>'toLocationId')::uuid,quantity);
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='hold' THEN
        SELECT * INTO STRICT lot FROM ledger.lots WHERE id=(p->>'lotId')::uuid FOR UPDATE;
        IF ledger.role(a)<>'CustomerManager' OR NOT ledger.own(a,lot.owner_id) THEN
            RAISE EXCEPTION 'HOLD authority denied' USING ERRCODE='42501';
        END IF;
        INSERT INTO ledger.holds(id,lot_id,reason) VALUES(command_id,lot.id,p->>'reason');
        INSERT INTO ledger.events(kind,actor_id,lot_id,details) VALUES('HOLD',a,lot.id,p);
        INSERT INTO ledger.notifications(user_id,hold_id,lot_id,message)
        SELECT u."Id",command_id,lot.id,p->>'reason' FROM identity.users u
        WHERE u."IsActive" AND (
            u."Role"='CustomerManager' AND u."OrganizationId"=lot.owner_id
            OR u."Role" IN ('MillManager','MillOperator') AND EXISTS (
                SELECT 1 FROM ledger.positions pos JOIN ledger.locations loc ON loc.id=pos.location_id
                WHERE pos.lot_id=lot.id AND pos.quantity_kg>0 AND ledger.assigned(u."Id",loc.facility_id)
            )
        );
        RETURN jsonb_build_object('id',command_id);
    END IF;
    IF action='hold_response' THEN
        SELECT * INTO STRICT hold FROM ledger.holds WHERE id=command_id;
        SELECT * INTO STRICT lot FROM ledger.lots WHERE id=hold.lot_id;
        -- Authority and evidence remain mandatory even when stock is completely consumed.
        SELECT ev.facility_id INTO facility FROM ledger.evidence ev
        WHERE ev.id=(p->>'evidenceId')::uuid AND ev.owner_id=lot.owner_id AND EXISTS (
            SELECT 1 FROM ledger.positions pos JOIN ledger.locations loc ON loc.id=pos.location_id
            WHERE pos.lot_id=lot.id AND loc.facility_id=ev.facility_id AND (
                pos.quantity_kg>0 OR NOT EXISTS (
                    SELECT 1 FROM ledger.positions active WHERE active.lot_id=lot.id AND active.quantity_kg>0
                )
            )
        );
        IF facility IS NULL THEN
            RAISE EXCEPTION 'HOLD response requires evidence scoped to current or historical lot facility'
                USING ERRCODE='42501';
        END IF;
        PERFORM ledger.require_staff(a,facility,ARRAY['MillManager']);
        PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,lot.owner_id,facility);
        FOR facility IN
            SELECT DISTINCT loc.facility_id FROM ledger.positions pos
            JOIN ledger.locations loc ON loc.id=pos.location_id WHERE pos.lot_id=lot.id AND pos.quantity_kg>0
        LOOP
            PERFORM ledger.require_staff(a,facility,ARRAY['MillManager']);
        END LOOP;
        IF p->>'status' IS NULL OR p->>'status' NOT IN ('ACKNOWLEDGED','ISOLATED','RESPONDED') THEN
            RAISE EXCEPTION 'HOLD release unsupported' USING ERRCODE='23514';
        END IF;
        IF p->>'status'='ISOLATED' AND EXISTS (
            SELECT 1 FROM ledger.positions pos JOIN ledger.locations loc ON loc.id=pos.location_id
            WHERE pos.lot_id=lot.id AND pos.quantity_kg>0 AND loc.kind<>'ISOLATION'
        ) THEN
            RAISE EXCEPTION 'Isolation requires physically transferred stock' USING ERRCODE='23514';
        END IF;
        UPDATE ledger.holds SET status=p->>'status',response=p->>'response',
            evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
        INSERT INTO ledger.events(kind,actor_id,lot_id,details) VALUES('HOLD_RESPONSE',a,lot.id,p);
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='correction_request' THEN
        SELECT * INTO STRICT operation FROM ledger.operations WHERE id=(p->>'operationId')::uuid FOR UPDATE;
        PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillOperator']);
        IF operation.status<>'COMMITTED' THEN
            RAISE EXCEPTION 'Only committed original operations can be corrected' USING ERRCODE='23514';
        END IF;
        INSERT INTO ledger.corrections(id,operation_id,reason) VALUES(command_id,operation.id,p->>'reason');
        INSERT INTO ledger.events(kind,actor_id,operation_id,details) VALUES('CORRECTION_REQUEST',a,operation.id,p);
        RETURN jsonb_build_object('id',command_id);
    END IF;
    IF action IN ('correction_reconcile','correction_cancel','correction_commit') THEN
        SELECT * INTO STRICT correction FROM ledger.corrections WHERE id=command_id FOR UPDATE;
        SELECT * INTO STRICT operation FROM ledger.operations WHERE id=correction.operation_id FOR UPDATE;
        IF action='correction_commit' THEN
            PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillOperator']);
        ELSE
            PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillManager']);
            PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,operation.owner_id,operation.facility_id);
        END IF;
        IF action='correction_commit' AND correction.status='COMMITTED' THEN
            IF correction.replacement_operation_id IS DISTINCT FROM (p->>'replacementOperationId')::uuid THEN
                RAISE EXCEPTION 'Correction retry replacement conflicts' USING ERRCODE='23514';
            END IF;
            RETURN ledger.operation_json(correction.replacement_operation_id);
        END IF;
        IF action='correction_cancel' THEN
            IF correction.status NOT IN ('REQUESTED','RECONCILED')
               OR p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                RAISE EXCEPTION 'Verified original-correct evidence and reason required' USING ERRCODE='23514';
            END IF;
            UPDATE ledger.corrections SET status='CANCELLED',evidence_id=(p->>'evidenceId')::uuid
            WHERE id=command_id;
            INSERT INTO ledger.events(kind,actor_id,operation_id,details)
            VALUES('CORRECTION_CANCEL_VERIFIED_ORIGINAL',a,operation.id,p);
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF correction.status NOT IN ('REQUESTED','RECONCILED') THEN
            RAISE EXCEPTION 'Correction already resolved' USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM ledger.lots output_lot JOIN ledger.postings pp ON pp.lot_id=output_lot.id
            JOIN ledger.events ee ON ee.id=pp.event_id WHERE output_lot.operation_id=operation.id
              AND ee.operation_id IS DISTINCT FROM operation.id AND ee.kind<>'TRANSFER'
        ) THEN
            RAISE EXCEPTION 'Downstream consumption prevents direct correction' USING ERRCODE='23514';
        END IF;
        IF action='correction_reconcile' THEN
            IF jsonb_typeof(p->'reconciliation') IS DISTINCT FROM 'array' THEN
                RAISE EXCEPTION 'Physical stock reconciliation array required' USING ERRCODE='23514';
            END IF;
            IF jsonb_array_length(p->'reconciliation')=0
               OR p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                RAISE EXCEPTION 'Physical stock reconciliation and reason required' USING ERRCODE='23514';
            END IF;
            IF EXISTS (
                SELECT 1 FROM jsonb_array_elements(p->'reconciliation') observed
                GROUP BY (observed->>'lotId')::uuid,(observed->>'locationId')::uuid HAVING count(*)>1
            ) THEN
                RAISE EXCEPTION 'Duplicate physical reconciliation position' USING ERRCODE='23514';
            END IF;
            -- Evidence must cover every actual position, and no invented/extra position.
            IF EXISTS (
                SELECT 1 FROM ledger.positions pos JOIN ledger.lots output_lot ON output_lot.id=pos.lot_id
                WHERE output_lot.operation_id=operation.id AND pos.quantity_kg>0 AND NOT EXISTS (
                    SELECT 1 FROM jsonb_array_elements(p->'reconciliation') observed
                    WHERE (observed->>'lotId')::uuid=pos.lot_id
                      AND (observed->>'locationId')::uuid=pos.location_id
                      AND ledger.kg(observed->>'quantityKg')=pos.quantity_kg
                )
            ) OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(p->'reconciliation') observed WHERE NOT EXISTS (
                    SELECT 1 FROM ledger.positions pos JOIN ledger.lots output_lot ON output_lot.id=pos.lot_id
                    WHERE output_lot.operation_id=operation.id AND pos.lot_id=(observed->>'lotId')::uuid
                      AND pos.location_id=(observed->>'locationId')::uuid
                      AND pos.quantity_kg=ledger.kg(observed->>'quantityKg') AND pos.quantity_kg>0
                )
            ) THEN
                RAISE EXCEPTION 'Physical reconciliation must exactly cover actual current output positions'
                    USING ERRCODE='23514';
            END IF;
            FOR facility IN
                SELECT DISTINCT loc.facility_id FROM jsonb_array_elements(p->'reconciliation') observed
                JOIN ledger.locations loc ON loc.id=(observed->>'locationId')::uuid
            LOOP
                PERFORM ledger.require_staff(a,facility,ARRAY['MillManager']);
            END LOOP;
            PERFORM ledger.review_replacement(a,command_id,(p->>'replacementOperationId')::uuid,
                                               (p->>'replacementVersion')::uuid,p->'reconciliation');
            UPDATE ledger.corrections SET status='RECONCILED',reconciliation=p->'reconciliation',
                evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
            INSERT INTO ledger.events(kind,actor_id,operation_id,details)
            VALUES('PHYSICAL_RECONCILIATION',a,operation.id,p);
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF correction.status<>'RECONCILED' THEN
            RAISE EXCEPTION 'Correction requires physical reconciliation' USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM ledger.lots output_lot WHERE output_lot.operation_id=operation.id AND (
                EXISTS(SELECT 1 FROM ledger.holds WHERE lot_id=output_lot.id)
                OR ledger.receiving_restricted(output_lot.id)
            )
        ) THEN
            RAISE EXCEPTION 'Independent HOLD or receiving restriction prevents correction replacement'
                USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM ledger.positions pos JOIN ledger.lots output_lot ON output_lot.id=pos.lot_id
            WHERE output_lot.operation_id=operation.id AND pos.quantity_kg>0 AND NOT EXISTS (
                SELECT 1 FROM jsonb_array_elements(correction.reconciliation) observed
                WHERE (observed->>'lotId')::uuid=pos.lot_id
                  AND (observed->>'locationId')::uuid=pos.location_id
                  AND ledger.kg(observed->>'quantityKg')=pos.quantity_kg
            )
        ) THEN
            RAISE EXCEPTION 'Physical reconciliation stale' USING ERRCODE='23514';
        END IF;
        SELECT * INTO STRICT operation FROM ledger.operations WHERE id=(p->>'replacementOperationId')::uuid;
        IF operation.id IS DISTINCT FROM correction.replacement_operation_id
           OR operation.version IS DISTINCT FROM correction.replacement_version
           OR operation.content IS DISTINCT FROM correction.replacement_content THEN
            RAISE EXCEPTION 'Replacement changed after physical reconciliation' USING ERRCODE='23514';
        END IF;
        IF operation.owner_id<>(SELECT owner_id FROM ledger.operations WHERE id=correction.operation_id)
           OR operation.facility_id<>(SELECT facility_id FROM ledger.operations WHERE id=correction.operation_id) THEN
            RAISE EXCEPTION 'Replacement cannot change owner/facility' USING ERRCODE='23514';
        END IF;
        INSERT INTO ledger.events(id,kind,actor_id,operation_id,details)
        VALUES(event_id,'PROCESSING_COMPENSATION',a,correction.operation_id,jsonb_build_object(
            'correctionId',command_id,'evidenceId',correction.evidence_id,'reconciliation',correction.reconciliation
        ));

        -- Reverse each original posting once. Transfers remain intact; approved location offsets
        -- drain outputs where physically reconciled, rather than fabricating reverse transfers.
        FOR original_posting IN
            SELECT pp.* FROM ledger.postings pp JOIN ledger.events ee ON ee.id=pp.event_id
            WHERE ee.operation_id=correction.operation_id
              AND ee.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING') ORDER BY pp.id
        LOOP
            IF original_posting.quantity_kg<0 THEN
                PERFORM ledger.credit_location(original_posting.location_id,
                    (SELECT owner_id FROM ledger.lots WHERE id=original_posting.lot_id));
                PERFORM ledger.post(event_id,original_posting.lot_id,original_posting.location_id,
                                    -original_posting.quantity_kg,original_posting.id);
            ELSE
                INSERT INTO ledger.postings(event_id,lot_id,location_id,quantity_kg,reverses_posting_id)
                VALUES(event_id,original_posting.lot_id,original_posting.location_id,
                       -original_posting.quantity_kg,original_posting.id);
                FOR physical_position IN
                    SELECT * FROM ledger.positions WHERE lot_id=original_posting.lot_id ORDER BY location_id
                LOOP
                    offset_quantity:=-physical_position.quantity_kg+CASE
                        WHEN physical_position.location_id=original_posting.location_id
                        THEN original_posting.quantity_kg ELSE 0 END;
                    IF offset_quantity<>0 THEN
                        INSERT INTO ledger.postings(event_id,lot_id,location_id,quantity_kg)
                        VALUES(event_id,original_posting.lot_id,physical_position.location_id,offset_quantity);
                    END IF;
                    UPDATE ledger.positions SET quantity_kg=0
                    WHERE lot_id=original_posting.lot_id AND location_id=physical_position.location_id;
                END LOOP;
            END IF;
        END LOOP;
        PERFORM ledger.commit_operation(a,operation.id,command_id);
        UPDATE ledger.operations SET status='CORRECTED' WHERE id=correction.operation_id;
        UPDATE ledger.corrections SET status='COMMITTED' WHERE id=command_id;
        RETURN ledger.operation_json(operation.id);
    END IF;

    IF action='notification_read' THEN
        UPDATE ledger.notifications SET read_at=coalesce(read_at,now()) WHERE id=command_id AND user_id=a;
        IF NOT FOUND THEN RAISE EXCEPTION 'Notification unavailable' USING ERRCODE='42501'; END IF;
        RETURN jsonb_build_object('id',command_id);
    END IF;
    RAISE EXCEPTION 'Unsupported ledger command' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION ledger.command(uuid,text,jsonb) FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.procurement_receipt(a uuid,o uuid,ev uuid,r uuid,c uuid,f uuid,d uuid,lines jsonb) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE
    h capture.procurements;
    snapshot jsonb;
    commodity uuid;
    row record;
    event_id uuid;
    lot_code text;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT ledger.own(a,o)
       OR NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=o AND "Kind"='Customer') THEN
        RAISE EXCEPTION 'Procurement company authority denied' USING ERRCODE='42501';
    END IF;
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND company_id=o AND status='COMMITTED';
    SELECT payload->'result' INTO STRICT snapshot FROM capture.business_events
    WHERE id=ev AND id=h.event_id AND company_id=o AND actor_id=a AND event_type='PROCUREMENT_COMMITTED';
    IF h.received_by<>a OR h.collection_site_id<>f
       OR (snapshot->>'CommodityId')::uuid IS DISTINCT FROM c
       OR (snapshot->>'ReceivingLocationId')::uuid IS DISTINCT FROM d
       OR NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=c AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=d AND facility_id=f AND kind IN ('STORE','SILO','COMPARTMENT')
           AND (owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared) OR owner_id=o))
       OR jsonb_typeof(lines) IS DISTINCT FROM 'array' OR jsonb_array_length(lines) NOT BETWEEN 1 AND 100
       OR jsonb_array_length(lines)<>(SELECT count(*) FROM capture.procurement_items WHERE procurement_id=r)
       OR (SELECT count(DISTINCT (x->>'procurementItemId')::uuid) FROM jsonb_array_elements(lines) x)<>jsonb_array_length(lines)
       OR EXISTS(SELECT 1 FROM jsonb_array_elements(lines) x WHERE NOT EXISTS(
           SELECT 1 FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id
           WHERE i.procurement_id=r AND i.company_id=o AND sl.company_id=o
           AND i.id=(x->>'procurementItemId')::uuid AND sl.id=(x->>'sourceLotId')::uuid
           AND sl.lot_id=(x->>'lotId')::uuid AND i.net_mass_kg=(x->>'netMassKg')::numeric
           AND x->>'lotCode'=upper('LL-'||replace(sl.lot_id::text,'-','')))) THEN
        RAISE EXCEPTION 'Persisted procurement package or receiving option mismatch' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM ledger.lots WHERE receipt_id=r) THEN
        IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Procurement replay conflicts with committed stock' USING ERRCODE='23514';
        END IF;
        RETURN;
    END IF;
    commodity:=ledger.provision_catalog_commodity(c);
    PERFORM ledger.credit_location(d,o);
    FOR row IN
        SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety
        FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        WHERE i.procurement_id=r ORDER BY i.line_no
    LOOP
        lot_code:=upper('LL-'||replace(row.lot_id::text,'-',''));
        INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id)
        VALUES(row.lot_id,lot_code,o,commodity,'RAW_PADDY','RAW_PADDY',r,row.id,ledger.register_code(lot_code,row.lot_id,NULL,NULL));
        event_id:=gen_random_uuid();
        INSERT INTO ledger.events(id,kind,actor_id,lot_id,details,occurred_at)
        VALUES(event_id,'RECEIPT',a,row.lot_id,jsonb_build_object('receiptId',r,'receiptLineId',row.id,
            'captureEventId',ev,'sourceLotId',row.source_id,'catalogCommodityId',c,'farmerId',h.farmer_id,
            'plotSeasonId',row.plot_season_id,'plotId',row.plot_id,'seasonId',row.season_id,'riceVariety',row.rice_variety,
            'originalQuantity',row.measured_value::text,'originalUnit',row.measured_unit,
            'quantityKg',row.net_mass_kg::text,'moisturePercent',row.moisture_percent::text),h.received_at);
        PERFORM ledger.post(event_id,row.lot_id,d,row.net_mass_kg);
        INSERT INTO ledger.contributions VALUES(row.lot_id,row.lot_id,row.net_mass_kg);
    END LOOP;
    IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Procurement, provenance and stock package incomplete' USING ERRCODE='23514';
    END IF;
END $$;

