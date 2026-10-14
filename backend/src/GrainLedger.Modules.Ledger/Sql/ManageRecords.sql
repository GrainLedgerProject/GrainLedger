-- Forward-only ownership metadata. Legacy creators stay unknown, never inferred.
ALTER TABLE ledger.facilities ADD COLUMN created_by uuid REFERENCES identity.users("Id"), ADD COLUMN version uuid NOT NULL DEFAULT gen_random_uuid(), ADD COLUMN is_deleted boolean NOT NULL DEFAULT false;
ALTER TABLE ledger.locations ADD COLUMN created_by uuid REFERENCES identity.users("Id"), ADD COLUMN version uuid NOT NULL DEFAULT gen_random_uuid(), ADD COLUMN is_deleted boolean NOT NULL DEFAULT false;
ALTER TABLE ledger.equipment ADD COLUMN created_by uuid REFERENCES identity.users("Id"), ADD COLUMN version uuid NOT NULL DEFAULT gen_random_uuid(), ADD COLUMN is_deleted boolean NOT NULL DEFAULT false;
ALTER TABLE ledger.assignments ADD COLUMN created_by uuid REFERENCES identity.users("Id"), ADD COLUMN version uuid NOT NULL DEFAULT gen_random_uuid(), ADD COLUMN is_deleted boolean NOT NULL DEFAULT false;
ALTER TABLE ledger.operations ADD COLUMN created_by uuid REFERENCES identity.users("Id"), ADD COLUMN is_deleted boolean NOT NULL DEFAULT false;

-- Private predicate shared by protected commands and read capability flags.
-- Zero positions and immutable JSON references still constitute history.
CREATE FUNCTION ledger.record_in_use(resource text,record_id uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT CASE resource
        WHEN 'facilities' THEN
            EXISTS(SELECT 1 FROM ledger.locations WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM ledger.equipment WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM ledger.assignments WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM ledger.evidence WHERE facility_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.policies WHERE facility_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.compatibilities WHERE facility_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.operations WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM capture.procurements WHERE collection_site_id=record_id)
        WHEN 'locations' THEN
            EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.postings WHERE location_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.operations op WHERE NOT op.is_deleted AND (
                EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') x WHERE (x->>'locationId')::uuid=record_id)
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x WHERE (x->>'locationId')::uuid=record_id)))
            OR EXISTS(SELECT 1 FROM capture.business_events ev WHERE ev.event_type='PROCUREMENT_COMMITTED'
                AND (ev.payload->'result'->>'ReceivingLocationId')::uuid=record_id)
            OR EXISTS(SELECT 1 FROM ledger.events ev WHERE CASE ev.kind
                WHEN 'CLEARANCE' THEN translate(lower(ev.details->>'id'),'-{}','')=replace(record_id::text,'-','')
                WHEN 'TRANSFER' THEN translate(lower(ev.details->>'fromLocationId'),'-{}','')=replace(record_id::text,'-','')
                    OR translate(lower(ev.details->>'toLocationId'),'-{}','')=replace(record_id::text,'-','')
                ELSE false END)
            OR EXISTS(SELECT 1 FROM ledger.events ev WHERE
                EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN ev.kind='VARIANCE_CONFIRMATION'
                    AND jsonb_typeof(ev.details->'reviewedContent'->'inputs')='array' THEN ev.details->'reviewedContent'->'inputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN ev.kind='VARIANCE_CONFIRMATION'
                    AND jsonb_typeof(ev.details->'reviewedContent'->'outputs')='array' THEN ev.details->'reviewedContent'->'outputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN ev.kind='PHYSICAL_RECONCILIATION'
                    AND jsonb_typeof(ev.details->'reconciliation')='array' THEN ev.details->'reconciliation' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-','')))
            OR EXISTS(SELECT 1 FROM ledger.corrections c WHERE
                EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(c.replacement_content->'inputs')='array' THEN c.replacement_content->'inputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(c.replacement_content->'outputs')='array' THEN c.replacement_content->'outputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(c.reconciliation)='array' THEN c.reconciliation ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-','')))
        WHEN 'equipment' THEN EXISTS(SELECT 1 FROM ledger.operations WHERE equipment_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM ledger.events WHERE kind='VARIANCE_CONFIRMATION'
                AND translate(lower(details->'reviewedContent'->>'equipmentId'),'-{}','')=replace(record_id::text,'-',''))
            OR EXISTS(SELECT 1 FROM ledger.corrections WHERE translate(lower(replacement_content->>'equipmentId'),'-{}','')=replace(record_id::text,'-',''))
        WHEN 'operations' THEN
            EXISTS(SELECT 1 FROM ledger.lots WHERE operation_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.corrections WHERE operation_id=record_id OR replacement_operation_id=record_id)
            OR EXISTS(SELECT 1 FROM ledger.events WHERE operation_id=record_id
                OR kind IN ('PHYSICAL_RECONCILIATION','RECONCILIATION_INVALIDATED')
                AND translate(lower(details->>'replacementOperationId'),'-{}','')=replace(record_id::text,'-',''))
        ELSE true
    END
$$;
REVOKE ALL ON FUNCTION ledger.record_in_use(text,uuid) FROM PUBLIC;

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
    setup_record record;
    requested_version uuid;
    new_user uuid;
    new_facility uuid;
    resource text;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS NULL THEN
        RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501';
    END IF;
    command_id:=NULLIF(p->>'id','')::uuid;
    IF p ?| ARRAY['createdBy','created_by'] THEN
        RAISE EXCEPTION 'Creator derives from authenticated actor' USING ERRCODE='22023';
    END IF;

    IF action IN ('facilities_update','facilities_delete','locations_update','locations_delete',
                  'equipment_update','equipment_delete','assignments_update','assignments_delete') THEN
        IF ledger.role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        resource:=split_part(action,'_',1);
        CASE resource
            WHEN 'facilities' THEN SELECT * INTO STRICT setup_record FROM ledger.facilities WHERE id=command_id FOR UPDATE;
            WHEN 'locations' THEN SELECT * INTO STRICT setup_record FROM ledger.locations WHERE id=command_id FOR UPDATE;
            WHEN 'equipment' THEN SELECT * INTO STRICT setup_record FROM ledger.equipment WHERE id=command_id FOR UPDATE;
            WHEN 'assignments' THEN SELECT * INTO STRICT setup_record FROM ledger.assignments
                WHERE user_id=(p->>'userId')::uuid AND facility_id=(p->>'facilityId')::uuid FOR UPDATE;
        END CASE;
        IF setup_record.created_by IS DISTINCT FROM a THEN
            RAISE EXCEPTION 'Only creator may manage setup record' USING ERRCODE='42501';
        END IF;
        IF setup_record.is_deleted THEN
            RAISE EXCEPTION 'Removed setup record cannot be changed or restored' USING ERRCODE='23514';
        END IF;
        IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
            RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
        END IF;
        requested_version:=(p->>'version')::uuid;
        IF setup_record.version IS DISTINCT FROM requested_version THEN
            RAISE EXCEPTION 'Setup record version stale' USING ERRCODE='23514';
        END IF;
        IF right(action,7)='_delete' THEN
            IF resource<>'assignments' AND ledger.record_in_use(resource,command_id) THEN
                RAISE EXCEPTION 'Setup record referenced; deletion denied' USING ERRCODE='23514';
            END IF;
            CASE resource
                WHEN 'facilities' THEN UPDATE ledger.facilities SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
                WHEN 'locations' THEN UPDATE ledger.locations SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
                WHEN 'equipment' THEN UPDATE ledger.equipment SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
                WHEN 'assignments' THEN UPDATE ledger.assignments SET is_deleted=true,version=gen_random_uuid()
                    WHERE user_id=setup_record.user_id AND facility_id=setup_record.facility_id;
            END CASE;
            IF resource='assignments' THEN
                RETURN jsonb_build_object('userId',setup_record.user_id,'facilityId',setup_record.facility_id);
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF resource='assignments' THEN
            new_user:=CASE WHEN p ? 'newUserId' THEN (p->>'newUserId')::uuid ELSE setup_record.user_id END;
            new_facility:=CASE WHEN p ? 'newFacilityId' THEN (p->>'newFacilityId')::uuid ELSE setup_record.facility_id END;
            IF new_facility IS NULL OR NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=new_facility AND NOT is_deleted)
               OR NOT EXISTS(SELECT 1 FROM identity.users WHERE "Id"=new_user AND "IsActive" AND "Role" IN ('MillOperator','MillManager')) THEN
                RAISE EXCEPTION 'Active operational staff and existing facility required' USING ERRCODE='23514';
            END IF;
            UPDATE ledger.assignments SET user_id=new_user,facility_id=new_facility,version=gen_random_uuid()
            WHERE user_id=setup_record.user_id AND facility_id=setup_record.facility_id
            RETURNING * INTO setup_record;
            RETURN jsonb_build_object('userId',setup_record.user_id,'facilityId',setup_record.facility_id,
                'version',setup_record.version,'createdBy',setup_record.created_by,'canEdit',true,'canDelete',true);
        END IF;
        IF jsonb_typeof(p->'name') IS DISTINCT FROM 'string' OR length(trim(p->>'name')) NOT BETWEEN 1 AND 160 THEN
            RAISE EXCEPTION 'Name of 1–160 characters required' USING ERRCODE='22023';
        END IF;
        CASE resource
            WHEN 'facilities' THEN
                UPDATE ledger.facilities SET name=p->>'name',version=gen_random_uuid() WHERE id=command_id RETURNING * INTO setup_record;
            WHEN 'locations' THEN
                new_facility:=CASE WHEN p ? 'facilityId' THEN (p->>'facilityId')::uuid ELSE setup_record.facility_id END;
                IF NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=new_facility AND NOT is_deleted) THEN
                    RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
                END IF;
                IF (new_facility IS DISTINCT FROM setup_record.facility_id
                    OR CASE WHEN p ? 'kind' THEN p->>'kind' ELSE setup_record.kind END IS DISTINCT FROM setup_record.kind)
                    AND ledger.record_in_use(resource,command_id) THEN
                    RAISE EXCEPTION 'Referenced location facility/kind immutable' USING ERRCODE='23514';
                END IF;
                UPDATE ledger.locations SET name=p->>'name',facility_id=new_facility,
                    kind=CASE WHEN p ? 'kind' THEN p->>'kind' ELSE kind END,version=gen_random_uuid()
                WHERE id=command_id RETURNING * INTO setup_record;
            WHEN 'equipment' THEN
                new_facility:=CASE WHEN p ? 'facilityId' THEN (p->>'facilityId')::uuid ELSE setup_record.facility_id END;
                IF NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=new_facility AND NOT is_deleted) THEN
                    RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
                END IF;
                IF new_facility IS DISTINCT FROM setup_record.facility_id AND ledger.record_in_use(resource,command_id) THEN
                    RAISE EXCEPTION 'Referenced equipment facility immutable' USING ERRCODE='23514';
                END IF;
                UPDATE ledger.equipment SET name=p->>'name',facility_id=new_facility,version=gen_random_uuid()
                WHERE id=command_id RETURNING * INTO setup_record;
        END CASE;
        RETURN to_jsonb(setup_record)-'created_by'-'facility_id'-'owner_id' || jsonb_build_object(
            'createdBy',setup_record.created_by,'canEdit',true,'canDelete',NOT ledger.record_in_use(resource,command_id))
            || CASE WHEN resource='facilities' THEN '{}'::jsonb ELSE jsonb_build_object('facilityId',to_jsonb(setup_record)->'facility_id') END;
    END IF;

    IF action='draft_delete' THEN
        SELECT * INTO STRICT operation FROM ledger.operations WHERE id=command_id FOR UPDATE;
        PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillOperator']);
        IF operation.created_by IS DISTINCT FROM a THEN
            RAISE EXCEPTION 'Only creator may manage operation draft' USING ERRCODE='42501';
        END IF;
        IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
            RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
        END IF;
        IF operation.is_deleted OR operation.status<>'DRAFT' OR operation.version IS DISTINCT FROM (p->>'version')::uuid THEN
            RAISE EXCEPTION 'Only current draft version may be deleted' USING ERRCODE='23514';
        END IF;
        IF ledger.record_in_use('operations',command_id) THEN
            RAISE EXCEPTION 'Draft referenced by correction or history; deletion denied' USING ERRCODE='23514';
        END IF;
        UPDATE ledger.operations SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
        RETURN jsonb_build_object('id',command_id);
    END IF;

    -- Setup does not confer operational, reconciliation or HOLD authority.
    IF action IN ('facilities','locations','equipment','assignments','policies','compatibilities','clearance') THEN
        IF ledger.role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        IF action IN ('locations','equipment','assignments','policies','compatibilities') AND NOT EXISTS(
            SELECT 1 FROM ledger.facilities WHERE id=(p->>'facilityId')::uuid AND NOT is_deleted
        ) THEN
            RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
        END IF;
        CASE action
            WHEN 'facilities' THEN
                INSERT INTO ledger.facilities(id,name,created_by) VALUES(command_id,p->>'name',a);
            WHEN 'locations' THEN
                INSERT INTO ledger.locations(id,facility_id,name,kind,created_by)
                VALUES(command_id,(p->>'facilityId')::uuid,p->>'name',p->>'kind',a);
            WHEN 'equipment' THEN
                INSERT INTO ledger.equipment(id,facility_id,name,created_by) VALUES(command_id,(p->>'facilityId')::uuid,p->>'name',a);
            WHEN 'assignments' THEN
                IF EXISTS(SELECT 1 FROM ledger.assignments WHERE user_id=(p->>'userId')::uuid
                    AND facility_id=(p->>'facilityId')::uuid AND is_deleted) THEN
                    RAISE EXCEPTION 'Removed assignment cannot be recreated or restored' USING ERRCODE='23514';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM identity.users WHERE "Id"=(p->>'userId')::uuid
                      AND "IsActive" AND "Role" IN ('MillOperator','MillManager')
                ) THEN
                    RAISE EXCEPTION 'Only active operational mill staff may be assigned' USING ERRCODE='23514';
                END IF;
                INSERT INTO ledger.assignments(user_id,facility_id,created_by) VALUES((p->>'userId')::uuid,(p->>'facilityId')::uuid,a)
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
                       OR EXISTS (
                           SELECT 1 FROM ledger.catalog_commodities map JOIN catalog.commodities c ON c.id=map.catalog_commodity_id
                           WHERE map.ledger_commodity_id=split_part(product,':',1)::uuid AND c.is_archived
                       )
                ) THEN
                    RAISE EXCEPTION 'Blend compatibility requires explicit registered commodity/product identities'
                        USING ERRCODE='23514';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM ledger.catalog_commodities map JOIN catalog.commodities c ON c.id=map.catalog_commodity_id
                    WHERE map.ledger_commodity_id=(p->>'outputCommodityId')::uuid AND c.is_archived
                ) THEN
                    RAISE EXCEPTION 'Archived Catalog commodity unavailable for compatibility' USING ERRCODE='23514';
                END IF;
                INSERT INTO ledger.compatibilities VALUES(command_id,facility,
                    ARRAY(SELECT jsonb_array_elements_text(p->'inputProducts')),
                    (p->>'outputCommodityId')::uuid,(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid);
            WHEN 'clearance' THEN
                SELECT * INTO STRICT location FROM ledger.locations WHERE id=command_id FOR UPDATE;
                IF location.is_deleted OR NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=location.facility_id AND NOT is_deleted) THEN
                    RAISE EXCEPTION 'Active location and facility required' USING ERRCODE='23514';
                END IF;
                IF EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=location.id AND quantity_kg>0) THEN
                    RAISE EXCEPTION 'Clearance requires empty stock' USING ERRCODE='23514';
                END IF;
                SELECT owner_id INTO STRICT owner FROM ledger.evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=location.facility_id;
                PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,owner,location.facility_id);
                IF p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                    RAISE EXCEPTION 'Clearance confirmation reason required' USING ERRCODE='23514';
                END IF;
                UPDATE ledger.locations SET cleared=true,owner_id=NULL,version=gen_random_uuid() WHERE id=location.id;
                INSERT INTO ledger.events(kind,actor_id,details) VALUES('CLEARANCE',a,p);
        END CASE;
        IF action IN ('facilities','locations','equipment','assignments') THEN
            CASE action
                WHEN 'facilities' THEN SELECT * INTO STRICT setup_record FROM ledger.facilities WHERE id=command_id;
                WHEN 'locations' THEN SELECT * INTO STRICT setup_record FROM ledger.locations WHERE id=command_id;
                WHEN 'equipment' THEN SELECT * INTO STRICT setup_record FROM ledger.equipment WHERE id=command_id;
                WHEN 'assignments' THEN SELECT * INTO STRICT setup_record FROM ledger.assignments
                    WHERE user_id=(p->>'userId')::uuid AND facility_id=(p->>'facilityId')::uuid;
            END CASE;
            IF action='assignments' THEN
                RETURN jsonb_build_object('userId',setup_record.user_id,'facilityId',setup_record.facility_id,
                    'version',setup_record.version,'createdBy',setup_record.created_by,
                    'canEdit',setup_record.created_by IS NOT DISTINCT FROM a,'canDelete',setup_record.created_by IS NOT DISTINCT FROM a);
            END IF;
            RETURN to_jsonb(setup_record)-'created_by'-'facility_id'-'owner_id' || jsonb_build_object(
                'createdBy',setup_record.created_by,'canEdit',true,'canDelete',NOT ledger.record_in_use(action,command_id))
                || CASE WHEN action='facilities' THEN '{}'::jsonb ELSE jsonb_build_object('facilityId',to_jsonb(setup_record)->'facility_id') END;
        END IF;
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='evidence' THEN
        facility:=(p->>'facilityId')::uuid;
        owner:=(p->>'ownerId')::uuid;
        IF NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=facility AND NOT is_deleted) THEN
            RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
        END IF;
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
        SELECT * INTO operation FROM ledger.operations WHERE id=command_id FOR UPDATE;
        IF FOUND THEN
            IF operation.is_deleted OR operation.status<>'DRAFT' OR operation.owner_id<>owner OR operation.facility_id<>facility THEN
                RAISE EXCEPTION 'Committed operation immutable or draft scope conflict' USING ERRCODE='23514';
            END IF;
            IF operation.created_by IS DISTINCT FROM a THEN
                RAISE EXCEPTION 'Only creator may manage operation draft' USING ERRCODE='42501';
            END IF;
            IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
                RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
            END IF;
            IF operation.version IS DISTINCT FROM (p->>'version')::uuid THEN
                RAISE EXCEPTION 'Draft version stale' USING ERRCODE='23514';
            END IF;
        ELSIF p ? 'version' THEN
            RAISE EXCEPTION 'Operation draft no longer exists' USING ERRCODE='23514';
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
                  AND loc.id=(item->>'locationId')::uuid AND loc.facility_id=facility AND NOT loc.is_deleted
            ) THEN
                RAISE EXCEPTION 'Draft input outside persisted owner/facility scope' USING ERRCODE='42501';
            END IF;
            PERFORM ledger.kg(item->>'quantityKg');
        END LOOP;
        FOR item IN SELECT value FROM jsonb_array_elements(p->'outputs') LOOP
            PERFORM ledger.kg(item->>'quantityKg');
            IF EXISTS (
                SELECT 1 FROM ledger.catalog_commodities map JOIN catalog.commodities c ON c.id=map.catalog_commodity_id
                WHERE map.ledger_commodity_id=(item->>'commodityId')::uuid AND c.is_archived
            ) THEN
                RAISE EXCEPTION 'Archived Catalog commodity unavailable for draft output' USING ERRCODE='23514';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM ledger.locations WHERE id=(item->>'locationId')::uuid
                  AND NOT is_deleted AND ledger.assigned(a,facility_id)
            ) THEN
                RAISE EXCEPTION 'Draft output outside assigned facility' USING ERRCODE='42501';
            END IF;
        END LOOP;
        PERFORM ledger.kg(p->>'declaredLossKg');
        IF NOT EXISTS(SELECT 1 FROM ledger.equipment WHERE id=(p->>'equipmentId')::uuid AND facility_id=facility AND NOT is_deleted) THEN
            RAISE EXCEPTION 'Draft equipment outside assigned facility' USING ERRCODE='42501';
        END IF;
        INSERT INTO ledger.operations(id,owner_id,facility_id,kind,occurred_at,equipment_id,content,version,created_by)
        VALUES(command_id,owner,facility,p->>'kind',(p->>'occurredAt')::timestamptz,
               (p->>'equipmentId')::uuid,p-'version'-'canEdit'-'canDelete'-'isDeleted'-'is_deleted',gen_random_uuid(),a)
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
        RETURN ledger.operation_json(command_id) || jsonb_build_object('createdBy',a,'canEdit',true,
            'canDelete',NOT ledger.record_in_use('operations',command_id));
    END IF;

    IF action IN ('confirm','commit') THEN
        SELECT * INTO STRICT operation FROM ledger.operations WHERE id=command_id FOR UPDATE;
        PERFORM ledger.require_active_operation(command_id);
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
        PERFORM ledger.require_active_location((p->>'fromLocationId')::uuid);
        PERFORM ledger.require_active_location((p->>'toLocationId')::uuid);
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
        PERFORM ledger.require_active_operation(operation.id);
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
        PERFORM ledger.require_active_operation(operation.id);
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
        PERFORM ledger.require_active_operation(operation.id);
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

-- New source registration cannot reuse archived setup. Historical package validity is unchanged.
CREATE OR REPLACE FUNCTION ledger.register_pending_procurement(a uuid,r uuid) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; snapshot jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    IF ledger.role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT ledger.own(a,h.company_id) OR h.received_by<>a THEN RAISE EXCEPTION 'Procurement company authority denied' USING ERRCODE='42501'; END IF;
    IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=h.company_id AND "Kind"='Customer') THEN RAISE EXCEPTION 'Customer procurement owner required' USING ERRCODE='42501'; END IF;
    SELECT payload->'result' INTO snapshot FROM capture.business_events WHERE id=h.event_id;
    IF NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=(snapshot->>'CommodityId')::uuid AND code='PADDY' AND NOT is_archived) THEN
        RAISE EXCEPTION 'Active PADDY commodity required for new procurement' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=(snapshot->>'ReceivingLocationId')::uuid AND facility_id=h.collection_site_id AND NOT is_deleted
        AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared)))
        OR NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=h.collection_site_id AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM capture.farmers WHERE id=h.farmer_id AND company_id=h.company_id AND NOT is_deleted)
        OR EXISTS(SELECT 1 FROM capture.procurement_items i WHERE i.procurement_id=r AND NOT EXISTS(
            SELECT 1 FROM capture.plot_seasons ps JOIN capture.plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
            JOIN capture.crop_seasons season ON season.id=ps.season_id AND season.company_id=ps.company_id
            WHERE ps.id=i.plot_season_id AND ps.company_id=h.company_id AND p.farmer_id=h.farmer_id
                AND NOT ps.is_deleted AND NOT p.is_deleted AND NOT season.is_deleted
        )) THEN
        RAISE EXCEPTION 'Active farmer, plot and season references required for new procurement' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM capture.procurement_items i WHERE i.procurement_id=r AND NOT EXISTS(
        SELECT 1 FROM catalog.measurement_units u WHERE u.code=i.measured_unit AND u.quantity_kind='MASS' AND NOT u.is_deleted
    )) THEN
        RAISE EXCEPTION 'Active mass units required for new procurement' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.receiving(procurement_id,status) VALUES(r,'PENDING_ARRIVAL');
    IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Pending source package incomplete' USING ERRCODE='23514'; END IF;
END $$;
REVOKE ALL ON FUNCTION ledger.register_pending_procurement(uuid,uuid) FROM PUBLIC;
