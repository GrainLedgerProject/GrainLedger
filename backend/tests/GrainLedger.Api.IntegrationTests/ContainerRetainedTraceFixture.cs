using System.Globalization;
using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.IntegrationTests;

// Isolated read/performance fixtures only. Current business mutations remain covered through real API handlers.
// Bulk history reuses an actually evaluated template; no public route can accept this fixture payload.
internal static class ContainerRetainedTraceFixture
{
    internal static async Task<Guid[]> SeedAsync(ApiFactory factory, ContainerExecutionScenario template, Guid lot, decimal quantity,
        int count, JsonElement assessment, bool legacyWithoutCapture = false)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var sql = """
            SELECT set_config('grainledger.fixture_template',$1,true),set_config('grainledger.fixture_proposal',$2,true),
              set_config('grainledger.fixture_lot',$3,true),set_config('grainledger.fixture_quantity',$4,true),
              set_config('grainledger.fixture_count',$5,true),set_config('grainledger.fixture_assessment',$6,true),
              set_config('grainledger.fixture_legacy',$7,true);
            DO $fixture$
            DECLARE original grainledger.exports_containers; plan grainledger.exports_blend_plan_drafts;
             preview grainledger.exports_blend_plan_previews; proposal grainledger.exports_blend_plan_proposals;
             chosen uuid:=current_setting('grainledger.fixture_lot')::uuid; actor uuid; customer uuid; facility uuid;
             amount numeric:=current_setting('grainledger.fixture_quantity')::numeric;
             benchmark_count integer:=current_setting('grainledger.fixture_count')::integer;
             basis jsonb:=current_setting('grainledger.fixture_assessment')::jsonb;
             target uuid; draft uuid; previous uuid; proposed uuid; event uuid; line uuid; root record;
             source_count integer; physical numeric; assessment_snapshot jsonb; seq integer;
             made uuid[]:=ARRAY[]::uuid[]; legacy boolean:=current_setting('grainledger.fixture_legacy')::boolean;
             historical_order uuid:=gen_random_uuid(); historical_spec uuid:=gen_random_uuid();
            BEGIN
             PERFORM grainledger.ledger_lock_writes();
             SELECT * INTO STRICT original FROM grainledger.exports_containers WHERE id=current_setting('grainledger.fixture_template')::uuid;
             SELECT * INTO STRICT proposal FROM grainledger.exports_blend_plan_proposals WHERE id=current_setting('grainledger.fixture_proposal')::uuid;
             SELECT * INTO STRICT plan FROM grainledger.exports_blend_plan_drafts WHERE id=proposal.plan_id;
             SELECT * INTO STRICT preview FROM grainledger.exports_blend_plan_previews WHERE id=proposal.preview_id;
             actor:=proposal.actor_id; facility:=(preview.request_json->>'FacilityId')::uuid;
             SELECT actor_id INTO customer FROM grainledger.exports_blend_plan_decisions WHERE proposal_id=proposal.id AND version=1;
             IF legacy THEN
              INSERT INTO grainledger.exports_export_orders(id,company_id,code,market_id,commodity_id,required_mass_kg,original_mass,original_unit,shipping_from,shipping_to,created_by,created_at)
               SELECT historical_order,company_id,'SYNTHETIC-LEGACY-'||historical_order::text,market_id,commodity_id,amount*benchmark_count,(amount*benchmark_count)::text,'KG',shipping_from,shipping_to,actor,clock_timestamp()
               FROM grainledger.exports_export_orders WHERE id=original.order_id;
              INSERT INTO grainledger.exports_order_specifications(id,order_id,company_id,version_no,recorded_by,recorded_at)
               VALUES(historical_spec,historical_order,original.company_id,1,actor,clock_timestamp());
              INSERT INTO grainledger.exports_specification_limits(id,specification_id,attribute_id,attribute_code,attribute_name,unit,min_value,max_value)
               SELECT gen_random_uuid(),historical_spec,attribute_id,attribute_code,attribute_name,unit,min_value,max_value
               FROM grainledger.exports_specification_limits WHERE specification_id=plan.specification_id;
              original.order_id:=historical_order; plan.specification_id:=historical_spec;
              plan.demand_snapshot:=plan.demand_snapshot||jsonb_build_object('orderId',historical_order,'specificationId',historical_spec,'orderVersion',1,'specificationVersion',1);
              -- New synthetic opening balance, never alter or delete valid Capture provenance.
              INSERT INTO grainledger.ledger_lots(id,code,code_origin_id,owner_id,commodity_id,stage,product_identity)
               SELECT chosen,'SYNTHETIC-LEGACY-'||chosen::text,grainledger.ledger_register_code('SYNTHETIC-LEGACY-'||chosen::text,chosen),
                original.company_id,l.commodity_id,'HEAD_RICE','HEAD_RICE' FROM grainledger.ledger_lots l WHERE l.id=(proposal.snapshot_json->'selection'->'allocations'->0->>'lotId')::uuid;
              INSERT INTO grainledger.ledger_contributions VALUES(chosen,chosen,amount*benchmark_count);
              event:=gen_random_uuid();
              INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details) VALUES(event,'SYNTHETIC_LEGACY_OPENING',actor,chosen,jsonb_build_object('testOnly',true));
              PERFORM grainledger.ledger_post(event,chosen,(proposal.snapshot_json->'selection'->'allocations'->0->>'locationId')::uuid,amount*benchmark_count);
              proposal.snapshot_json:=proposal.snapshot_json||jsonb_build_object('outcome','INCOMPLETE','assessmentStatus','INCOMPLETE','dependencyRevision','LEGACY_UNASSESSED',
               'selection',jsonb_build_object('mode','DIRECT_STOCK','compatibilityPolicyId',NULL,'allocations',jsonb_build_array(jsonb_build_object('lotId',chosen,
                'locationId',proposal.snapshot_json->'selection'->'allocations'->0->>'locationId','quantityKg',amount::numeric(20,6)::text,'lotCode','Synthetic legacy source','candidateRevision','LEGACY'))));
             END IF;
             SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=chosen AND location_id=(proposal.snapshot_json->'selection'->'allocations'->0->>'locationId')::uuid;
             IF physical IS NULL OR physical<amount*benchmark_count THEN RAISE EXCEPTION 'Benchmark source balance insufficient'; END IF;
             FOR seq IN 1..benchmark_count LOOP
              IF seq=1 AND NOT legacy THEN target:=original.id; draft:=plan.id; previous:=preview.id; proposed:=proposal.id;
              ELSE
               target:=gen_random_uuid(); draft:=gen_random_uuid(); previous:=gen_random_uuid(); proposed:=gen_random_uuid();
               INSERT INTO grainledger.exports_containers(id,order_id,company_id,internal_code,target_mass_kg,original_mass,original_unit,status,created_at)
                VALUES(target,original.order_id,original.company_id,'SYNTHETIC-TRACE-'||target::text,amount,amount::text,'KG','PLANNED',clock_timestamp());
               INSERT INTO grainledger.exports_blend_plan_drafts(id,company_id,order_id,container_id,specification_id,version_no,version,status,target_mass_kg,demand_snapshot,created_by,created_at)
                VALUES(draft,original.company_id,original.order_id,target,plan.specification_id,1,1,'DRAFT',amount,
                 plan.demand_snapshot||jsonb_build_object('containerId',target,'targetMassKg',amount::numeric(20,6)::text,'containerTargetMassKg',amount::numeric(20,6)::text),actor,clock_timestamp());
               assessment_snapshot:=jsonb_set(proposal.snapshot_json,'{planId}',to_jsonb(draft));
               INSERT INTO grainledger.exports_blend_plan_previews(id,company_id,plan_id,actor_id,request_json,snapshot_json,fingerprint,created_at,expires_at)
                VALUES(previous,original.company_id,draft,actor,preview.request_json,assessment_snapshot,preview.fingerprint,preview.created_at,preview.expires_at);
               INSERT INTO grainledger.exports_blend_plan_proposals(id,company_id,plan_id,preview_id,actor_id,snapshot_json,proposed_at)
                VALUES(proposed,original.company_id,draft,previous,actor,assessment_snapshot,clock_timestamp());
               IF NOT legacy THEN
                INSERT INTO grainledger.exports_blend_plan_decisions(proposal_id,version,company_id,container_id,plan_id,action,actor_id,recorded_at)
                 VALUES(proposed,1,original.company_id,target,draft,'APPROVED',customer,clock_timestamp());
                INSERT INTO grainledger.ledger_blend_reservations VALUES(proposed,chosen,(proposal.snapshot_json->'selection'->'allocations'->0->>'locationId')::uuid,amount);
               END IF;
              END IF;
              event:=gen_random_uuid();
              INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details) VALUES(event,'CONTAINER_LOADING',actor,chosen,jsonb_build_object('containerId',target,'ownerId',original.company_id,'facilityId',facility,'syntheticTraceFixture',true));
              INSERT INTO grainledger.ledger_container_loads(id,container_id,proposal_id,loaded_at,actor_id,assessment) VALUES(event,target,proposed,clock_timestamp(),actor,basis);
              INSERT INTO grainledger.ledger_container_load_lines(load_id,lot_id,location_id,quantity_kg)
               VALUES(event,chosen,(proposal.snapshot_json->'selection'->'allocations'->0->>'locationId')::uuid,amount) RETURNING id INTO line;
              INSERT INTO grainledger.ledger_container_load_sources(line_id,source_lot_id,quantity_kg)
               SELECT line,s.source_lot_id,s.quantity_kg FROM grainledger.ledger_container_allocate_sources(amount,
                (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=chosen),
                (SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text)) FROM grainledger.ledger_contributions WHERE lot_id=chosen)) s;
              IF NOT legacy THEN INSERT INTO grainledger.ledger_blend_reservation_events VALUES(gen_random_uuid(),proposed,chosen,(proposal.snapshot_json->'selection'->'allocations'->0->>'locationId')::uuid,'CONSUME',amount,actor,clock_timestamp()); END IF;
              PERFORM grainledger.ledger_post(event,chosen,(proposal.snapshot_json->'selection'->'allocations'->0->>'locationId')::uuid,-amount);
              INSERT INTO grainledger.ledger_container_execution(container_id,facility_id,proposal_id) VALUES(target,facility,proposed);
              UPDATE grainledger.exports_containers SET status='LOADING',version=version+1 WHERE id=target;
              made:=array_append(made,target);
             END LOOP;
             PERFORM set_config('grainledger.fixture_result',to_jsonb(made)::text,true);
            END $fixture$;
            SELECT current_setting('grainledger.fixture_result');
            """;
        var split = sql.IndexOf("DO $fixture$", StringComparison.Ordinal);
        var finish = sql.IndexOf("SELECT current_setting('grainledger.fixture_result')", StringComparison.Ordinal);
        await using var command = new NpgsqlCommand(sql[..split], connection, transaction);
        foreach (var value in new[] { template.ContainerId.ToString(), template.ProposalId.ToString(), lot.ToString(), quantity.ToString(CultureInfo.InvariantCulture), count.ToString(CultureInfo.InvariantCulture), assessment.GetRawText(), legacyWithoutCapture.ToString().ToLowerInvariant() })
            command.Parameters.AddWithValue(NpgsqlDbType.Text, value);
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
        command.Parameters.Clear();
        command.CommandText = sql[split..finish];
        await command.ExecuteNonQueryAsync();
        command.CommandText = sql[finish..];
        var result = (string)(await command.ExecuteScalarAsync())!;
        await transaction.CommitAsync();
        return JsonSerializer.Deserialize<Guid[]>(result!)!;
    }
}
