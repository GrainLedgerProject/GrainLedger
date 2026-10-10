CREATE FUNCTION ledger.can_read_lot(a uuid,l uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger,identity AS $$
    SELECT EXISTS (
        SELECT 1 FROM ledger.lots x WHERE x.id=l AND (
            ledger.role(a) IN ('CustomerManager','ExportStaff','PurchasingStaff')
            AND ledger.own(a,x.owner_id)
            OR ledger.role(a) IN ('MillManager','MillOperator') AND EXISTS (
                SELECT 1 FROM ledger.positions p
                JOIN ledger.locations loc ON loc.id=p.location_id
                WHERE p.lot_id=l AND ledger.assigned(a,loc.facility_id)
            )
        )
    )
$$;

CREATE FUNCTION ledger.can_read_operation(a uuid,op uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger,identity AS $$
    SELECT EXISTS (
        SELECT 1 FROM ledger.operations x WHERE x.id=op AND (
            ledger.role(a) IN ('CustomerManager','ExportStaff','PurchasingStaff')
            AND ledger.own(a,x.owner_id)
            OR ledger.role(a) IN ('MillManager','MillOperator')
            AND ledger.assigned(a,x.facility_id)
        )
    )
$$;

CREATE FUNCTION ledger.lot_json(a uuid,l uuid,detail boolean DEFAULT false) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT jsonb_build_object(
        'id',x.id,'code',x.code,'ownerId',x.owner_id,'commodityId',x.commodity_id,
        'stage',x.stage,'productIdentity',x.product_identity,
        'positions',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'locationId',p.location_id,'facilityId',lc.facility_id,
                'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN ledger.blocked(l) THEN '0.000000' ELSE p.quantity_kg::text END
            ))
            FROM ledger.positions p JOIN ledger.locations lc ON lc.id=p.location_id
            WHERE p.lot_id=l AND (ledger.own(a,x.owner_id) OR ledger.assigned(a,lc.facility_id))
        ),'[]'::jsonb),
        'restrictions',to_jsonb(array_remove(ARRAY[
            CASE WHEN EXISTS(SELECT 1 FROM ledger.holds WHERE lot_id=l) THEN 'HOLD' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM ledger.corrections
                WHERE operation_id=x.operation_id AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS (
                SELECT 1 FROM ledger.receipt_correction_blocks WHERE receipt_id=x.receipt_id
            ) THEN 'CORRECTION' END,
            CASE WHEN ledger.receiving_restricted(l) THEN 'RECEIVING_DISCREPANCY' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM ledger.receipt_reversals WHERE receipt_id=x.receipt_id
            ) OR EXISTS (
                SELECT 1 FROM ledger.operations WHERE id=x.operation_id AND status='CORRECTED'
            ) THEN 'SUPERSEDED' END
        ],NULL))
    ) || CASE WHEN detail AND ledger.role(a)<>'MillOperator' THEN
        jsonb_build_object('contributions',coalesce((
            SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
            FROM ledger.contributions WHERE lot_id=l
        ),'[]'::jsonb))
    ELSE '{}'::jsonb END
    FROM ledger.lots x WHERE x.id=l AND ledger.can_read_lot(a,l)
$$;

CREATE FUNCTION ledger.read(a uuid,action text,p jsonb) RETURNS jsonb
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
                SELECT jsonb_agg(jsonb_build_object('id',id,'name',name)) FROM ledger.commodities
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
        WHERE (requested_lot IS NULL OR coalesce(pp.lot_id,ee.lot_id)=requested_lot
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
        RETURN result;
    END IF;
    RAISE EXCEPTION 'Unsupported ledger read' USING ERRCODE='23514';
END $$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
