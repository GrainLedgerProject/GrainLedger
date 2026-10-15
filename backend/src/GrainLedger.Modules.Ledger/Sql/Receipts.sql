CREATE OR REPLACE FUNCTION ledger.receipt(a uuid,p jsonb) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    destination ledger.locations;
    existing ledger.lots;
    quantity numeric;
    original_quantity numeric;
    event_id uuid:=gen_random_uuid();
    original_receipt uuid;
    original_hold record;
    inherited_hold uuid;
BEGIN
    IF to_regnamespace('capture_mock') IS NULL THEN
        RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
            USING ERRCODE='23514';
    END IF;
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT destination FROM ledger.locations
    WHERE id=(p->>'locationId')::uuid FOR UPDATE;
    IF NOT (ledger.role(a)='PurchasingStaff' AND ledger.own(a,(p->>'ownerId')::uuid)) THEN
        PERFORM ledger.require_staff(a,destination.facility_id,ARRAY['MillOperator','MillManager']);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM identity.organizations WHERE "Id"=(p->>'ownerId')::uuid AND "Kind"='Customer'
    ) THEN
        RAISE EXCEPTION 'Receipt owner must be customer' USING ERRCODE='23514';
    END IF;

    -- Source records are prerequisites, not client-created opening stock.
    IF capture_mock.receipt_stock_eligible(a,p) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Persisted procurement source missing or receipt line stock payload mismatch'
            USING ERRCODE='23514';
    END IF;
    quantity:=ledger.kg(p->>'quantityKg');
    IF p->>'originalQuantity' IS NULL
       OR length(p->>'originalQuantity')>40
       OR p->>'originalQuantity' !~ '^[0-9]+(\.[0-9]{1,6})?$' THEN
        RAISE EXCEPTION 'Unsupported original measurement precision or overflow' USING ERRCODE='23514';
    END IF;
    original_quantity:=(p->>'originalQuantity')::numeric;
    IF quantity<=0 OR original_quantity<=0
       OR p->>'originalUnit' IS NULL OR p->>'originalUnit' NOT IN ('kg','g','t')
       OR quantity<>(CASE p->>'originalUnit'
           WHEN 'kg' THEN original_quantity
           WHEN 'g' THEN original_quantity/1000
           WHEN 't' THEN original_quantity*1000
       END) THEN
        RAISE EXCEPTION 'Invalid original measurement or exact conversion' USING ERRCODE='23514';
    END IF;
    SELECT * INTO existing FROM ledger.lots WHERE receipt_line_id=(p->>'receiptLineId')::uuid;
    IF FOUND THEN
        IF existing.id<>(p->>'lotId')::uuid OR existing.owner_id<>(p->>'ownerId')::uuid
           OR existing.commodity_id<>(p->>'commodityId')::uuid OR existing.code<>p->>'code'
           OR NOT EXISTS (
               SELECT 1 FROM ledger.events ev
               WHERE ev.lot_id=existing.id AND ev.kind='RECEIPT' AND ev.details=p
           ) THEN
            RAISE EXCEPTION 'Receipt retry conflicts with committed content' USING ERRCODE='23514';
        END IF;
        RETURN existing.id;
    END IF;
    IF p->>'replacementForReceiptId' IS NOT NULL THEN
        original_receipt:=(p->>'replacementForReceiptId')::uuid;
        IF NOT EXISTS (
            SELECT 1 FROM ledger.receipt_reversals
            WHERE receipt_id=original_receipt AND owner_id=(p->>'ownerId')::uuid
        ) OR current_setting('ledger.reversed_receipt',true) IS DISTINCT FROM original_receipt::text THEN
            RAISE EXCEPTION 'Receipt replacement requires same-transaction compensation' USING ERRCODE='23514';
        END IF;
    END IF;
    IF EXISTS(SELECT 1 FROM ledger.lots WHERE receipt_id=(p->>'receiptId')::uuid AND code=p->>'code') THEN
        RAISE EXCEPTION 'Receipt replacement cannot contain duplicate active lot codes' USING ERRCODE='23505';
    END IF;
    PERFORM ledger.credit_location(destination.id,(p->>'ownerId')::uuid);
    INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id)
    VALUES (
        (p->>'lotId')::uuid,p->>'code',(p->>'ownerId')::uuid,(p->>'commodityId')::uuid,
        'RAW_PADDY','RAW_PADDY',(p->>'receiptId')::uuid,(p->>'receiptLineId')::uuid,
        ledger.register_code(p->>'code',(p->>'lotId')::uuid,original_receipt,NULL)
    );
    INSERT INTO ledger.events(id,kind,actor_id,lot_id,details,occurred_at)
    VALUES(event_id,'RECEIPT',a,(p->>'lotId')::uuid,p,(p->>'occurredAt')::timestamptz);
    PERFORM ledger.post(event_id,(p->>'lotId')::uuid,destination.id,quantity);
    INSERT INTO ledger.contributions VALUES((p->>'lotId')::uuid,(p->>'lotId')::uuid,quantity);

    -- Receipt compensation must not become an accidental HOLD release.
    IF original_receipt IS NOT NULL THEN
        FOR original_hold IN
            SELECT hh.* FROM ledger.holds hh JOIN ledger.lots ll ON ll.id=hh.lot_id
            WHERE ll.receipt_id=original_receipt
        LOOP
            inherited_hold:=gen_random_uuid();
            INSERT INTO ledger.holds(id,lot_id,reason)
            VALUES(inherited_hold,(p->>'lotId')::uuid,'Inherited HOLD from receipt correction: '||original_hold.reason);
            INSERT INTO ledger.notifications(user_id,hold_id,lot_id,message)
            SELECT u."Id",inherited_hold,(p->>'lotId')::uuid,'Receipt replacement retains active HOLD'
            FROM identity.users u WHERE u."IsActive" AND (
                u."OrganizationId"=(p->>'ownerId')::uuid AND u."Role"='CustomerManager'
                OR u."Role"='MillManager' AND ledger.assigned(u."Id",destination.facility_id)
            );
        END LOOP;
    END IF;
    RETURN (p->>'lotId')::uuid;
END $$;

CREATE OR REPLACE FUNCTION ledger.receipt_correction_request(a uuid,c uuid,r uuid,o uuid) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
BEGIN
    IF to_regnamespace('capture_mock') IS NULL THEN
        RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
            USING ERRCODE='23514';
    END IF;
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT ledger.own(a,o) THEN
        RAISE EXCEPTION 'Receipt correction requester denied' USING ERRCODE='42501';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM ledger.lots WHERE receipt_id=r AND owner_id=o)
       OR EXISTS(SELECT 1 FROM ledger.lots WHERE receipt_id=r AND owner_id<>o) THEN
        RAISE EXCEPTION 'Receipt source ownership mismatch' USING ERRCODE='23514';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ledger.lots l JOIN ledger.postings p ON p.lot_id=l.id
        JOIN ledger.events e ON e.id=p.event_id WHERE l.receipt_id=r AND e.kind<>'RECEIPT'
    ) THEN
        RAISE EXCEPTION 'Moved or processed receipt requires investigation' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.receipt_correction_blocks VALUES(c,r,o,false);
END $$;

CREATE OR REPLACE FUNCTION ledger.reverse_receipt(a uuid,c uuid,r uuid,o uuid,ev uuid,reason text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    original_posting record;
    event_id uuid:=gen_random_uuid();
BEGIN
    IF to_regnamespace('capture_mock') IS NULL THEN
        RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
            USING ERRCODE='23514';
    END IF;
    PERFORM ledger.lock_writes();
    IF NOT EXISTS (
        SELECT 1 FROM ledger.receipt_correction_blocks
        WHERE correction_id=c AND receipt_id=r AND owner_id=o AND NOT committed
    ) THEN
        RAISE EXCEPTION 'Receipt correction request missing or already committed' USING ERRCODE='23514';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ledger.receiving_blocks rb JOIN ledger.lots ll ON ll.id=rb.lot_id
        WHERE ll.receipt_id=r AND rb.real_difference
    ) THEN
        RAISE EXCEPTION 'Genuine transport loss/gain requires separately approved adjustment'
            USING ERRCODE='23514';
    END IF;
    FOR original_posting IN
        SELECT pp.*,l.owner_id,loc.facility_id FROM ledger.lots l
        JOIN ledger.postings pp ON pp.lot_id=l.id
        JOIN ledger.locations loc ON loc.id=pp.location_id
        WHERE l.receipt_id=r ORDER BY pp.lot_id,pp.location_id
    LOOP
        PERFORM ledger.require_staff(a,original_posting.facility_id,ARRAY['MillManager']);
        PERFORM ledger.require_evidence(a,ev,o,original_posting.facility_id);
        IF original_posting.owner_id<>o OR EXISTS (
            SELECT 1 FROM ledger.postings pp JOIN ledger.events ee ON ee.id=pp.event_id
            WHERE pp.lot_id=original_posting.lot_id AND ee.kind<>'RECEIPT'
        ) OR NOT EXISTS (
            SELECT 1 FROM ledger.positions
            WHERE lot_id=original_posting.lot_id AND location_id=original_posting.location_id
              AND quantity_kg=original_posting.quantity_kg
        ) THEN
            RAISE EXCEPTION 'Receipt correction requires unused original source stock' USING ERRCODE='23514';
        END IF;
    END LOOP;
    INSERT INTO ledger.receipt_reversals VALUES(r,c,o,ev);
    INSERT INTO ledger.events(id,kind,actor_id,details)
    VALUES(event_id,'RECEIPT_COMPENSATION',a,jsonb_build_object('receiptId',r,'correctionId',c,'evidenceId',ev,'reason',reason));
    FOR original_posting IN
        SELECT pp.* FROM ledger.postings pp JOIN ledger.lots l ON l.id=pp.lot_id WHERE l.receipt_id=r
    LOOP
        PERFORM ledger.post(event_id,original_posting.lot_id,original_posting.location_id,
                            -original_posting.quantity_kg,original_posting.id);
    END LOOP;
    UPDATE ledger.receipt_correction_blocks SET committed=true WHERE correction_id=c;
    PERFORM set_config('ledger.reversed_receipt',r::text,true);
END $$;

CREATE OR REPLACE FUNCTION ledger.receiving_block(a uuid,d uuid,l uuid,b boolean,ev uuid,reason text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    source_lot ledger.lots;
    observation_facility uuid;
BEGIN
    IF to_regnamespace('capture_mock') IS NULL THEN
        RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
            USING ERRCODE='23514';
    END IF;
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT source_lot FROM ledger.lots WHERE id=l FOR UPDATE;
    IF source_lot.receipt_id IS NULL THEN
        RAISE EXCEPTION 'Receiving observation must identify registered source receipt lot' USING ERRCODE='23514';
    END IF;
    IF reason IS NULL OR length(trim(reason))=0 THEN
        RAISE EXCEPTION 'Investigation reason required' USING ERRCODE='23514';
    END IF;
    -- An arrival observation belongs to its original receiving facility, even after stock moved.
    SELECT loc.facility_id INTO STRICT observation_facility
    FROM ledger.postings pp JOIN ledger.events ee ON ee.id=pp.event_id
    JOIN ledger.locations loc ON loc.id=pp.location_id
    WHERE pp.lot_id=l AND ee.kind='RECEIPT';
    IF NOT (b AND ledger.role(a)='PurchasingStaff' AND ledger.own(a,source_lot.owner_id)) THEN
        PERFORM ledger.require_staff(a,observation_facility,
            CASE WHEN b THEN ARRAY['MillOperator','MillManager'] ELSE ARRAY['MillManager'] END);
    END IF;
    PERFORM ledger.require_evidence(a,ev,source_lot.owner_id,observation_facility);
    IF NOT b AND reason NOT IN ('ARRIVAL_MEASUREMENT_CORRECTED','RECEIPT_CORRECTED') THEN
        RAISE EXCEPTION 'Unresolved loss or gain cannot clear receiving block' USING ERRCODE='23514';
    END IF;
    IF NOT b AND EXISTS(SELECT 1 FROM ledger.receiving_blocks WHERE discrepancy_id=d AND real_difference) THEN
        RAISE EXCEPTION 'Genuine receiving difference cannot be cleared by entry correction' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM ledger.receiving_blocks WHERE discrepancy_id=d AND lot_id<>l) THEN
        RAISE EXCEPTION 'Discrepancy identity conflict' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.receiving_blocks VALUES(d,l,b,ev,reason,reason='REAL_DIFFERENCE')
    ON CONFLICT(discrepancy_id) DO UPDATE SET
        blocked=excluded.blocked,evidence_id=excluded.evidence_id,reason=excluded.reason,
        real_difference=ledger.receiving_blocks.real_difference OR excluded.real_difference;
    INSERT INTO ledger.events(kind,actor_id,lot_id,details)
    VALUES('RECEIVING_RESTRICTION',a,l,jsonb_build_object('discrepancyId',d,'blocked',b,'evidenceId',ev,'reason',reason));
END $$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
