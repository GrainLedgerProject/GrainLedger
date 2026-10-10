using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261009020000_InitialCapture")]
public sealed class InitialCapture : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("capture")
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA capture;
        CREATE TABLE capture.farmers (
            id uuid PRIMARY KEY, owner_id uuid NOT NULL REFERENCES identity.organizations("Id"), body jsonb NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(id, owner_id)
        );
        CREATE TABLE capture.plots (
            id uuid PRIMARY KEY, owner_id uuid NOT NULL, farmer_id uuid NOT NULL, body jsonb NOT NULL,
            latitude numeric(9,6) NOT NULL CHECK(latitude BETWEEN -90 AND 90),
            longitude numeric(9,6) NOT NULL CHECK(longitude BETWEEN -180 AND 180),
            FOREIGN KEY(farmer_id,owner_id) REFERENCES capture.farmers(id,owner_id),
            UNIQUE(id,owner_id,farmer_id)
        );
        CREATE TABLE capture.seasons (
            id uuid PRIMARY KEY, owner_id uuid NOT NULL REFERENCES identity.organizations("Id"), body jsonb NOT NULL,
            starts_on date NOT NULL, ends_on date NOT NULL CHECK(ends_on >= starts_on), UNIQUE(id,owner_id)
        );
        CREATE TABLE capture.plot_seasons (
            id uuid PRIMARY KEY, owner_id uuid NOT NULL, farmer_id uuid NOT NULL, plot_id uuid NOT NULL,
            season_id uuid NOT NULL, body jsonb NOT NULL,
            FOREIGN KEY(plot_id,owner_id,farmer_id) REFERENCES capture.plots(id,owner_id,farmer_id),
            FOREIGN KEY(season_id,owner_id) REFERENCES capture.seasons(id,owner_id),
            UNIQUE(plot_id,season_id), UNIQUE(id,owner_id,farmer_id,season_id)
        );
        CREATE TABLE capture.receipts (
            id uuid PRIMARY KEY, owner_id uuid NOT NULL, farmer_id uuid NOT NULL, season_id uuid NOT NULL,
            location_id uuid NOT NULL, body jsonb NOT NULL, status text NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','COMMITTED')),
            version uuid NOT NULL, replacement_for uuid REFERENCES capture.receipts(id),
            created_by uuid NOT NULL REFERENCES identity.users("Id"), created_at timestamptz NOT NULL DEFAULT now(), committed_at timestamptz,
            FOREIGN KEY(farmer_id,owner_id) REFERENCES capture.farmers(id,owner_id),
            FOREIGN KEY(season_id,owner_id) REFERENCES capture.seasons(id,owner_id),
            UNIQUE(id,owner_id,farmer_id,season_id), CHECK((status='COMMITTED')=(committed_at IS NOT NULL))
        );
        CREATE TABLE capture.receipt_lines (
            id uuid PRIMARY KEY, receipt_id uuid NOT NULL, owner_id uuid NOT NULL, farmer_id uuid NOT NULL,
            season_id uuid NOT NULL, plot_season_id uuid NOT NULL, commodity_id uuid NOT NULL, body jsonb NOT NULL,
            quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg > 0),
            FOREIGN KEY(receipt_id,owner_id,farmer_id,season_id) REFERENCES capture.receipts(id,owner_id,farmer_id,season_id),
            FOREIGN KEY(plot_season_id,owner_id,farmer_id,season_id) REFERENCES capture.plot_seasons(id,owner_id,farmer_id,season_id)
        );
        CREATE INDEX receipt_lines_receipt ON capture.receipt_lines(receipt_id);
        CREATE TABLE capture.receipt_lots (
            receipt_line_id uuid PRIMARY KEY REFERENCES capture.receipt_lines(id), lot_id uuid NOT NULL UNIQUE
        );
        CREATE TABLE capture.arrivals (
            id uuid PRIMARY KEY, receipt_line_id uuid NOT NULL REFERENCES capture.receipt_lines(id), body jsonb NOT NULL,
            quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg > 0), actor_id uuid NOT NULL REFERENCES identity.users("Id"),
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE TABLE capture.arrival_corrections (
            sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            id uuid NOT NULL UNIQUE, arrival_id uuid NOT NULL REFERENCES capture.arrivals(id), body jsonb NOT NULL,
            quantity_kg numeric(20,6) NOT NULL CHECK(quantity_kg > 0), actor_id uuid NOT NULL REFERENCES identity.users("Id"),
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX arrival_corrections_root ON capture.arrival_corrections(arrival_id,sequence);
        CREATE TABLE capture.arrival_investigations (
            id uuid PRIMARY KEY, arrival_id uuid NOT NULL REFERENCES capture.arrivals(id), body jsonb NOT NULL,
            actor_id uuid NOT NULL REFERENCES identity.users("Id"), created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE TABLE capture.receipt_corrections (
            id uuid PRIMARY KEY, receipt_id uuid NOT NULL UNIQUE REFERENCES capture.receipts(id),
            replacement_receipt_id uuid NOT NULL UNIQUE REFERENCES capture.receipts(id), body jsonb NOT NULL,
            status text NOT NULL DEFAULT 'REQUESTED' CHECK(status IN ('REQUESTED','RECONCILED','COMMITTED')),
            reconciliation jsonb, evidence_id uuid, reconciled_by uuid REFERENCES identity.users("Id"),
            created_by uuid NOT NULL REFERENCES identity.users("Id"), created_at timestamptz NOT NULL DEFAULT now(), committed_at timestamptz,
            CHECK((status='REQUESTED')=(reconciliation IS NULL)),
            CHECK((status='COMMITTED')=(committed_at IS NOT NULL))
        );
        CREATE TABLE capture.commands (
            kind text NOT NULL, id uuid NOT NULL, actor_id uuid NOT NULL REFERENCES identity.users("Id"),
            actor_organization_id uuid NOT NULL REFERENCES identity.organizations("Id"), actor_role text NOT NULL,
            content_hash text NOT NULL, response jsonb NOT NULL, PRIMARY KEY(kind,id)
        );
        CREATE FUNCTION capture.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Capture history is immutable' USING ERRCODE='23514'; END $$;
        DO $$ DECLARE name text; BEGIN
            FOREACH name IN ARRAY ARRAY['farmers','plots','seasons','plot_seasons','receipt_lots','arrivals','arrival_corrections','arrival_investigations','commands'] LOOP
                EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON capture.%I FOR EACH ROW EXECUTE FUNCTION capture.immutable()',name);
            END LOOP;
        END $$;
        CREATE FUNCTION capture.protect_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP='INSERT' THEN
                IF NEW.status<>'DRAFT' THEN RAISE EXCEPTION 'Receipt must begin as draft' USING ERRCODE='23514'; END IF;
                RETURN NEW;
            END IF;
            IF OLD.status='COMMITTED' THEN RAISE EXCEPTION 'Committed receipt is immutable' USING ERRCODE='23514'; END IF;
            IF TG_OP='DELETE' THEN
                IF OLD.replacement_for IS NOT NULL THEN RAISE EXCEPTION 'Correction replacement is immutable' USING ERRCODE='23514'; END IF;
                RETURN OLD;
            END IF;
            IF OLD.replacement_for IS NOT NULL AND (NEW.body IS DISTINCT FROM OLD.body OR
                NEW.owner_id<>OLD.owner_id OR NEW.farmer_id<>OLD.farmer_id OR NEW.season_id<>OLD.season_id OR
                NEW.location_id<>OLD.location_id OR NEW.replacement_for IS DISTINCT FROM OLD.replacement_for OR NEW.version<>OLD.version)
                THEN RAISE EXCEPTION 'Correction replacement is immutable' USING ERRCODE='23514'; END IF;
            IF NEW.status='COMMITTED' AND EXISTS (
                SELECT 1 FROM capture.receipt_lines l LEFT JOIN capture.receipt_lots m ON m.receipt_line_id=l.id
                WHERE l.receipt_id=NEW.id AND m.lot_id IS NULL
            ) THEN RAISE EXCEPTION 'Receipt requires one source lot per line' USING ERRCODE='23514'; END IF;
            IF NEW.status='COMMITTED' AND NOT EXISTS(SELECT 1 FROM capture.receipt_lines WHERE receipt_id=NEW.id)
                THEN RAISE EXCEPTION 'Receipt requires positive lines' USING ERRCODE='23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER protect_receipt BEFORE INSERT OR UPDATE OR DELETE ON capture.receipts FOR EACH ROW EXECUTE FUNCTION capture.protect_receipt();
        CREATE FUNCTION capture.protect_line() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE rid uuid; state text;
        BEGIN
            IF TG_OP='DELETE' THEN rid:=OLD.receipt_id; ELSE rid:=NEW.receipt_id; END IF;
            SELECT status INTO state FROM capture.receipts WHERE id=rid FOR UPDATE;
            IF state='COMMITTED' THEN RAISE EXCEPTION 'Committed receipt lines are immutable' USING ERRCODE='23514'; END IF;
            IF EXISTS(SELECT 1 FROM capture.receipt_corrections WHERE replacement_receipt_id=rid)
                THEN RAISE EXCEPTION 'Reviewed correction replacement lines are immutable' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' AND OLD.receipt_id<>NEW.receipt_id THEN RAISE EXCEPTION 'Receipt line cannot move receipts' USING ERRCODE='23514'; END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE TRIGGER protect_line BEFORE INSERT OR UPDATE OR DELETE ON capture.receipt_lines FOR EACH ROW EXECUTE FUNCTION capture.protect_line();
        CREATE FUNCTION capture.protect_correction() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP='DELETE' OR OLD.status='COMMITTED' THEN RAISE EXCEPTION 'Receipt correction history is immutable' USING ERRCODE='23514'; END IF;
            IF NEW.body<>OLD.body OR NEW.receipt_id<>OLD.receipt_id OR NEW.replacement_receipt_id<>OLD.replacement_receipt_id OR NEW.created_by<>OLD.created_by
                THEN RAISE EXCEPTION 'Receipt correction content is immutable' USING ERRCODE='23514'; END IF;
            IF OLD.status='RECONCILED' AND (NEW.reconciliation IS DISTINCT FROM OLD.reconciliation OR NEW.evidence_id IS DISTINCT FROM OLD.evidence_id OR NEW.reconciled_by IS DISTINCT FROM OLD.reconciled_by)
                THEN RAISE EXCEPTION 'Physical reconciliation is immutable' USING ERRCODE='23514'; END IF;
            IF (OLD.status='REQUESTED' AND NEW.status<>'RECONCILED') OR (OLD.status='RECONCILED' AND NEW.status<>'COMMITTED')
                THEN RAISE EXCEPTION 'Invalid correction transition' USING ERRCODE='23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER protect_correction BEFORE UPDATE OR DELETE ON capture.receipt_corrections FOR EACH ROW EXECUTE FUNCTION capture.protect_correction();

        CREATE FUNCTION capture.document_kg(body jsonb) RETURNS numeric
        LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
        DECLARE original numeric; kg numeric;
        BEGIN
            IF body->>'quantity' IS NULL OR length(body->>'quantity')>40 OR body->>'quantity' !~ '^[0-9]+(\.[0-9]{1,6})?$' OR
                body->>'unit' IS NULL OR body->>'unit' NOT IN ('kg','g','t')
                THEN RAISE EXCEPTION 'Unsupported original decimal measure' USING ERRCODE='23514'; END IF;
            original:=(body->>'quantity')::numeric;
            kg:=CASE body->>'unit' WHEN 'kg' THEN original WHEN 'g' THEN original/1000 WHEN 't' THEN original*1000 END;
            IF kg<=0 OR kg>99999999999999.999999 OR trunc(kg,6)<>kg
                THEN RAISE EXCEPTION 'Original measure must convert exactly to positive numeric(20,6) kg' USING ERRCODE='23514'; END IF;
            RETURN kg;
        END $$;

        CREATE FUNCTION capture.receipt_document_complete(rid uuid) RETURNS boolean
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
            SELECT EXISTS(
                SELECT 1 FROM capture.receipts r WHERE r.id=rid AND
                    (r.body->>'id')::uuid=r.id AND (r.body->>'ownerOrganizationId')::uuid=r.owner_id AND
                    (r.body->>'farmerId')::uuid=r.farmer_id AND (r.body->>'seasonId')::uuid=r.season_id AND
                    (r.body->>'locationId')::uuid=r.location_id AND (r.body->>'occurredAt')::timestamptz IS NOT NULL AND
                    jsonb_array_length(r.body->'lines') BETWEEN 1 AND 200 AND
                    jsonb_array_length(r.body->'lines')=(SELECT count(*) FROM capture.receipt_lines WHERE receipt_id=r.id) AND
                    NOT EXISTS(
                        SELECT 1 FROM jsonb_array_elements(r.body->'lines') documented
                        LEFT JOIN capture.receipt_lines l ON l.id=(documented->>'id')::uuid AND l.receipt_id=r.id
                        WHERE l.id IS NULL OR l.body IS DISTINCT FROM documented OR
                            l.plot_season_id IS DISTINCT FROM (documented->>'plotSeasonId')::uuid OR
                            l.commodity_id IS DISTINCT FROM (documented->>'commodityId')::uuid OR
                            l.quantity_kg IS DISTINCT FROM capture.document_kg(documented) OR
                            documented->>'lotCode' IS NULL OR length(trim(documented->>'lotCode')) NOT BETWEEN 1 AND 120
                    ) AND (SELECT count(DISTINCT (documented->>'id')::uuid) FROM jsonb_array_elements(r.body->'lines') documented)=
                        jsonb_array_length(r.body->'lines')
            );
        $$;
        REVOKE ALL ON FUNCTION capture.document_kg(jsonb),capture.receipt_document_complete(uuid) FROM PUBLIC;
        CREATE FUNCTION capture.check_receipt_document() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
        DECLARE rid uuid;
        BEGIN
            IF TG_TABLE_NAME='receipts' THEN rid:=NEW.id;
            ELSIF TG_OP='DELETE' THEN rid:=OLD.receipt_id;
            ELSE rid:=NEW.receipt_id; END IF;
            IF EXISTS(SELECT 1 FROM capture.receipts WHERE id=rid) AND NOT capture.receipt_document_complete(rid)
                THEN RAISE EXCEPTION 'Receipt document and normalized source lines must agree at commit' USING ERRCODE='23514'; END IF;
            RETURN NULL;
        END $$;
        REVOKE ALL ON FUNCTION capture.check_receipt_document() FROM PUBLIC;
        CREATE CONSTRAINT TRIGGER receipt_document AFTER INSERT OR UPDATE ON capture.receipts DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_receipt_document();
        CREATE CONSTRAINT TRIGGER receipt_line_document AFTER INSERT OR UPDATE OR DELETE ON capture.receipt_lines DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_receipt_document();

        CREATE FUNCTION capture.receipt_stock_eligible(a uuid,p jsonb) RETURNS boolean
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
        DECLARE r capture.receipts; l capture.receipt_lines; role text; owner uuid;
        BEGIN
            SELECT * INTO r FROM capture.receipts WHERE id=(p->>'receiptId')::uuid FOR UPDATE;
            IF NOT FOUND OR r.status<>'DRAFT' OR NOT capture.receipt_document_complete(r.id) THEN RETURN false; END IF;
            SELECT * INTO l FROM capture.receipt_lines WHERE id=(p->>'receiptLineId')::uuid AND receipt_id=r.id FOR UPDATE;
            IF NOT FOUND THEN RETURN false; END IF;
            SELECT "Role","OrganizationId" INTO role,owner FROM identity.users WHERE "Id"=a AND "IsActive";
            IF role IS NULL OR NOT (role='PurchasingStaff' AND owner=r.owner_id OR role='MillManager' AND r.replacement_for IS NOT NULL)
                THEN RETURN false; END IF;
            IF l.owner_id IS DISTINCT FROM (p->>'ownerId')::uuid OR
                l.commodity_id IS DISTINCT FROM (p->>'commodityId')::uuid OR
                r.location_id IS DISTINCT FROM (p->>'locationId')::uuid OR
                l.quantity_kg IS DISTINCT FROM (p->>'quantityKg')::numeric OR
                l.body->>'lotCode' IS DISTINCT FROM p->>'code' OR
                l.body->>'quantity' IS DISTINCT FROM p->>'originalQuantity' OR
                l.body->>'unit' IS DISTINCT FROM p->>'originalUnit' OR
                (r.body->>'occurredAt')::timestamptz IS DISTINCT FROM (p->>'occurredAt')::timestamptz OR
                r.replacement_for IS DISTINCT FROM (p->>'replacementForReceiptId')::uuid
                THEN RETURN false; END IF;
            IF r.replacement_for IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM capture.receipt_corrections c WHERE c.replacement_receipt_id=r.id AND
                    c.receipt_id=r.replacement_for AND c.status='RECONCILED' AND c.reconciliation IS NOT NULL AND c.evidence_id IS NOT NULL AND
                    c.body->'replacement'=r.body
            ) THEN RETURN false; END IF;
            RETURN true;
        END $$;

        CREATE FUNCTION capture.receipt_stock_complete(rid uuid) RETURNS boolean
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
            SELECT EXISTS(SELECT 1 FROM capture.receipts WHERE id=rid AND status='COMMITTED')
                AND capture.receipt_document_complete(rid)
                AND EXISTS(SELECT 1 FROM capture.receipt_lines WHERE receipt_id=rid)
                AND (SELECT count(*) FROM capture.receipt_lines WHERE receipt_id=rid)=
                    (SELECT count(*) FROM ledger.lots WHERE receipt_id=rid)
                AND NOT EXISTS(
                    SELECT 1 FROM capture.receipt_lines l
                    JOIN capture.receipts r ON r.id=l.receipt_id
                    LEFT JOIN capture.receipt_lots m ON m.receipt_line_id=l.id
                    LEFT JOIN ledger.lots lot ON lot.id=m.lot_id
                    WHERE r.id=rid AND (m.lot_id IS NULL OR lot.id IS NULL OR lot.receipt_line_id<>l.id OR
                        lot.receipt_id<>r.id OR lot.owner_id<>l.owner_id OR lot.commodity_id<>l.commodity_id OR
                        lot.stage<>'RAW_PADDY' OR lot.product_identity<>'RAW_PADDY' OR lot.code<>l.body->>'lotCode' OR
                        NOT EXISTS(SELECT 1 FROM ledger.postings posting JOIN ledger.events event ON event.id=posting.event_id
                            WHERE posting.lot_id=lot.id AND posting.location_id=r.location_id AND posting.quantity_kg=l.quantity_kg AND
                            posting.reverses_posting_id IS NULL AND event.kind='RECEIPT' AND
                            event.details->>'originalQuantity'=l.body->>'quantity' AND
                            event.details->>'originalUnit'=l.body->>'unit' AND
                            event.occurred_at=(r.body->>'occurredAt')::timestamptz))
                );
        $$;

        CREATE FUNCTION capture.receipt_correction_complete(cid uuid) RETURNS boolean
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
            SELECT EXISTS(
                SELECT 1 FROM capture.receipt_corrections c
                JOIN capture.receipts original ON original.id=c.receipt_id
                JOIN capture.receipts replacement ON replacement.id=c.replacement_receipt_id
                JOIN ledger.receipt_reversals reversal ON reversal.receipt_id=c.receipt_id AND reversal.correction_id=c.id
                WHERE c.id=cid AND c.status='COMMITTED' AND c.reconciliation IS NOT NULL AND
                    c.evidence_id=reversal.evidence_id AND original.owner_id=replacement.owner_id AND
                    original.location_id=replacement.location_id AND replacement.replacement_for=original.id AND
                    original.farmer_id=replacement.farmer_id AND original.season_id=replacement.season_id AND
                    c.body->'replacement'=replacement.body AND
                    jsonb_array_length(original.body->'lines')=jsonb_array_length(replacement.body->'lines') AND
                    NOT EXISTS(
                        SELECT 1 FROM jsonb_array_elements(original.body->'lines') WITH ORDINALITY old_line(line,ordinal)
                        JOIN jsonb_array_elements(replacement.body->'lines') WITH ORDINALITY new_line(line,ordinal)
                            ON old_line.ordinal=new_line.ordinal
                        WHERE old_line.line->>'plotSeasonId' IS DISTINCT FROM new_line.line->>'plotSeasonId' OR
                            old_line.line->>'commodityId' IS DISTINCT FROM new_line.line->>'commodityId'
                    ) AND
                    NOT EXISTS(
                        SELECT 1 FROM jsonb_array_elements(original.body->'lines') WITH ORDINALITY old_line(line,ordinal)
                        JOIN jsonb_array_elements(replacement.body->'lines') WITH ORDINALITY new_line(line,ordinal)
                            ON old_line.ordinal=new_line.ordinal
                        JOIN capture.receipt_lines new_source ON new_source.id=(new_line.line->>'id')::uuid
                        JOIN capture.arrivals arrival ON arrival.receipt_line_id=(old_line.line->>'id')::uuid
                        WHERE coalesce((SELECT observation.quantity_kg FROM capture.arrival_corrections observation
                            WHERE observation.arrival_id=arrival.id ORDER BY observation.sequence DESC LIMIT 1),arrival.quantity_kg)<>new_source.quantity_kg OR
                            EXISTS(SELECT 1 FROM capture.arrival_investigations investigation WHERE investigation.arrival_id=arrival.id AND
                                investigation.body->>'cause'='REAL_DIFFERENCE')
                    ) AND
                    capture.receipt_stock_complete(replacement.id) AND
                    jsonb_array_length(c.reconciliation->'verifiedLines')=jsonb_array_length(replacement.body->'lines') AND
                    NOT EXISTS(SELECT 1 FROM capture.receipt_lines l WHERE l.receipt_id=replacement.id AND
                        NOT EXISTS(SELECT 1 FROM jsonb_array_elements(c.reconciliation->'verifiedLines') verified
                            WHERE (verified->>'receiptLineId')::uuid=l.id AND (verified->>'quantityKg')::numeric=l.quantity_kg))
            );
        $$;

        CREATE FUNCTION capture.receiving_clear_complete(d uuid,l uuid,e uuid,reason text) RETURNS boolean
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
            SELECT EXISTS(
                SELECT 1 FROM capture.arrivals arrival
                JOIN capture.receipt_lines source ON source.id=arrival.receipt_line_id
                JOIN capture.receipt_lots map ON map.receipt_line_id=source.id
                WHERE arrival.id=d AND map.lot_id=l AND NOT EXISTS(
                    SELECT 1 FROM capture.arrival_investigations investigation
                    WHERE investigation.arrival_id=arrival.id AND investigation.body->>'cause'='REAL_DIFFERENCE'
                ) AND (
                    reason='ARRIVAL_MEASUREMENT_CORRECTED' AND EXISTS(
                        SELECT 1 FROM capture.arrival_corrections observation WHERE observation.sequence=(
                            SELECT max(latest.sequence) FROM capture.arrival_corrections latest WHERE latest.arrival_id=arrival.id
                        ) AND observation.arrival_id=arrival.id AND observation.quantity_kg=source.quantity_kg AND
                            (observation.body->>'evidenceId')::uuid=e
                    ) OR
                    reason='RECEIPT_CORRECTED' AND EXISTS(
                        SELECT 1 FROM capture.receipt_corrections correction WHERE correction.receipt_id=source.receipt_id AND
                            correction.status='COMMITTED' AND correction.evidence_id=e AND capture.receipt_correction_complete(correction.id)
                    )
                )
            );
        $$;

        CREATE FUNCTION capture.receiving_block_complete(d uuid,l uuid,e uuid,reason text) RETURNS boolean
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
            SELECT EXISTS(
                SELECT 1 FROM capture.arrivals arrival
                JOIN capture.receipt_lines source ON source.id=arrival.receipt_line_id
                JOIN capture.receipt_lots map ON map.receipt_line_id=source.id
                LEFT JOIN LATERAL (
                    SELECT observation.* FROM capture.arrival_corrections observation
                    WHERE observation.arrival_id=arrival.id ORDER BY observation.sequence DESC LIMIT 1
                ) latest ON true
                WHERE arrival.id=d AND map.lot_id=l AND coalesce(latest.quantity_kg,arrival.quantity_kg)<>source.quantity_kg AND (
                    reason='ARRIVAL_MISMATCH' AND latest.id IS NULL AND (arrival.body->>'evidenceId')::uuid=e OR
                    reason='CORRECTED_ARRIVAL_STILL_DIFFERS' AND latest.id IS NOT NULL AND (latest.body->>'evidenceId')::uuid=e OR
                    reason IN ('REAL_DIFFERENCE','UNRESOLVED') AND EXISTS(
                        SELECT 1 FROM capture.arrival_investigations investigation WHERE investigation.arrival_id=arrival.id AND
                            investigation.body->>'cause'=reason AND (investigation.body->>'evidenceId')::uuid=e
                    )
                )
            );
        $$;

        REVOKE ALL ON FUNCTION capture.receipt_stock_eligible(uuid,jsonb),capture.receipt_stock_complete(uuid),
            capture.receipt_correction_complete(uuid),capture.receiving_clear_complete(uuid,uuid,uuid,text),
            capture.receiving_block_complete(uuid,uuid,uuid,text) FROM PUBLIC;
        CREATE FUNCTION capture.check_package() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
        BEGIN
            IF TG_TABLE_NAME='receipts' AND EXISTS(SELECT 1 FROM capture.receipts WHERE id=NEW.id AND status='COMMITTED') AND
                (NOT capture.receipt_document_complete(NEW.id) OR NOT capture.receipt_stock_complete(NEW.id))
                THEN RAISE EXCEPTION 'Receipt and source stock package must commit together' USING ERRCODE='23514'; END IF;
            IF TG_TABLE_NAME='receipt_corrections' THEN
                IF NOT EXISTS(
                    SELECT 1 FROM capture.receipt_corrections c JOIN capture.receipts r ON r.id=c.receipt_id
                    JOIN capture.receipts replacement ON replacement.id=c.replacement_receipt_id AND replacement.replacement_for=r.id
                    JOIN ledger.receipt_correction_blocks block ON block.correction_id=c.id AND block.receipt_id=c.receipt_id AND block.owner_id=r.owner_id
                    WHERE c.id=NEW.id AND block.committed=(c.status='COMMITTED') AND r.status='COMMITTED' AND
                        (c.body->>'id')::uuid=c.id AND c.body->'replacement'=replacement.body AND
                        coalesce(length(trim(c.body->>'reason')),0) BETWEEN 1 AND 2000 AND
                        capture.receipt_document_complete(replacement.id) AND (
                            c.status='REQUESTED' OR
                            (c.reconciliation->>'evidenceId')::uuid=c.evidence_id AND
                            coalesce(length(trim(c.reconciliation->>'reason')),0) BETWEEN 1 AND 2000 AND
                            jsonb_array_length(c.reconciliation->'verifiedLines')=(SELECT count(*) FROM capture.receipt_lines WHERE receipt_id=replacement.id) AND
                            NOT EXISTS(SELECT 1 FROM capture.receipt_lines line WHERE line.receipt_id=replacement.id AND
                                NOT EXISTS(SELECT 1 FROM jsonb_array_elements(c.reconciliation->'verifiedLines') verified
                                    WHERE (verified->>'receiptLineId')::uuid=line.id AND (verified->>'quantityKg')::numeric=line.quantity_kg))
                        )
                ) THEN RAISE EXCEPTION 'Receipt correction request and stock restrictions must commit together' USING ERRCODE='23514'; END IF;
                IF EXISTS(SELECT 1 FROM capture.receipt_corrections WHERE id=NEW.id AND status='COMMITTED') AND NOT capture.receipt_correction_complete(NEW.id)
                    THEN RAISE EXCEPTION 'Receipt compensation and replacement package must commit together' USING ERRCODE='23514'; END IF;
            END IF;
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION capture.check_package() FROM PUBLIC;
        CREATE CONSTRAINT TRIGGER receipt_package AFTER INSERT OR UPDATE ON capture.receipts DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_package();
        CREATE CONSTRAINT TRIGGER correction_package AFTER INSERT OR UPDATE ON capture.receipt_corrections DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_package();

        CREATE FUNCTION capture.validate_observation() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
        BEGIN
            IF NEW.id IS DISTINCT FROM (NEW.body->>'id')::uuid OR (NEW.body->>'evidenceId')::uuid IS NULL
                THEN RAISE EXCEPTION 'Observation identity and evidence must match immutable document' USING ERRCODE='23514'; END IF;
            IF TG_TABLE_NAME='arrivals' THEN
                IF NEW.receipt_line_id IS DISTINCT FROM (NEW.body->>'receiptLineId')::uuid OR
                    NEW.quantity_kg IS DISTINCT FROM capture.document_kg(NEW.body) OR (NEW.body->>'observedAt')::timestamptz IS NULL
                    THEN RAISE EXCEPTION 'Arrival fields must match immutable original document' USING ERRCODE='23514'; END IF;
            ELSIF TG_TABLE_NAME='arrival_corrections' THEN
                IF NEW.quantity_kg IS DISTINCT FROM capture.document_kg(NEW.body) OR coalesce(length(trim(NEW.body->>'reason')),0) NOT BETWEEN 1 AND 2000
                    THEN RAISE EXCEPTION 'Corrected measure and reason must match immutable document' USING ERRCODE='23514'; END IF;
            ELSE
                IF NEW.body->>'cause' IS NULL OR NEW.body->>'cause' NOT IN ('REAL_DIFFERENCE','UNRESOLVED') OR
                    coalesce(length(trim(NEW.body->>'reason')),0) NOT BETWEEN 1 AND 2000
                    THEN RAISE EXCEPTION 'Investigation cause and reason are required' USING ERRCODE='23514'; END IF;
            END IF;
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION capture.validate_observation() FROM PUBLIC;
        CREATE TRIGGER validate_arrival BEFORE INSERT ON capture.arrivals FOR EACH ROW EXECUTE FUNCTION capture.validate_observation();
        CREATE TRIGGER validate_arrival_correction BEFORE INSERT ON capture.arrival_corrections FOR EACH ROW EXECUTE FUNCTION capture.validate_observation();
        CREATE TRIGGER validate_arrival_investigation BEFORE INSERT ON capture.arrival_investigations FOR EACH ROW EXECUTE FUNCTION capture.validate_observation();

        CREATE FUNCTION capture.check_receiving_package() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture AS $$
        DECLARE root uuid; observed numeric; expected numeric; current_measure numeric; source_lot uuid; genuine boolean; required boolean; block ledger.receiving_blocks;
        BEGIN
            IF TG_TABLE_NAME='arrivals' THEN root:=NEW.id; ELSE root:=NEW.arrival_id; END IF;
            SELECT arrival.quantity_kg,line.quantity_kg,map.lot_id,
                coalesce((SELECT correction.quantity_kg FROM capture.arrival_corrections correction
                    WHERE correction.arrival_id=arrival.id ORDER BY correction.sequence DESC LIMIT 1),arrival.quantity_kg)
                INTO observed,expected,source_lot,current_measure
                FROM capture.arrivals arrival JOIN capture.receipt_lines line ON line.id=arrival.receipt_line_id
                JOIN capture.receipts receipt ON receipt.id=line.receipt_id AND receipt.status='COMMITTED'
                JOIN capture.receipt_lots map ON map.receipt_line_id=line.id WHERE arrival.id=root;
            IF NOT FOUND THEN RAISE EXCEPTION 'Receiving requires committed source stock' USING ERRCODE='23514'; END IF;
            genuine:=EXISTS(SELECT 1 FROM capture.arrival_investigations WHERE arrival_id=root AND body->>'cause'='REAL_DIFFERENCE');
            required:=observed<>expected OR EXISTS(SELECT 1 FROM capture.arrival_corrections WHERE arrival_id=root AND quantity_kg<>expected) OR
                EXISTS(SELECT 1 FROM capture.arrival_investigations WHERE arrival_id=root);
            IF required THEN
                SELECT * INTO block FROM ledger.receiving_blocks WHERE discrepancy_id=root AND lot_id=source_lot;
                IF NOT FOUND THEN RAISE EXCEPTION 'Receiving difference and protected stock restriction must commit together' USING ERRCODE='23514'; END IF;
                IF current_measure<>expected THEN
                    IF NOT block.blocked OR genuine AND NOT block.real_difference OR
                        NOT capture.receiving_block_complete(root,source_lot,block.evidence_id,block.reason)
                        THEN RAISE EXCEPTION 'Current receiving difference requires durable blocked stock package' USING ERRCODE='23514'; END IF;
                ELSE
                    IF genuine OR block.blocked OR NOT capture.receiving_clear_complete(root,source_lot,block.evidence_id,block.reason)
                        THEN RAISE EXCEPTION 'Receiving resolution requires immutable equal-mass corrected observation' USING ERRCODE='23514'; END IF;
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION capture.check_receiving_package() FROM PUBLIC;
        CREATE CONSTRAINT TRIGGER arrival_package AFTER INSERT ON capture.arrivals DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_receiving_package();
        CREATE CONSTRAINT TRIGGER arrival_correction_package AFTER INSERT ON capture.arrival_corrections DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_receiving_package();
        CREATE CONSTRAINT TRIGGER arrival_investigation_package AFTER INSERT ON capture.arrival_investigations DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION capture.check_receiving_package();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP SCHEMA capture CASCADE;");
}
