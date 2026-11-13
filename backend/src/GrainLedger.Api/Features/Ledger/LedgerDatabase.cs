using Microsoft.EntityFrameworkCore;
using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Catalogs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GrainLedger.Api.Features.Ledger;

public static class LedgerDatabase
{
    public static async Task ConfigureRuntimeRoleAsync(string connectionString, string runtimeRole, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeRole)) throw new InvalidOperationException("Ledger:RuntimeRole is required.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1) AND to_regnamespace('grainledger') IS NOT NULL", connection, transaction);
        exists.Parameters.AddWithValue(runtimeRole);
        if (!Equals(await exists.ExecuteScalarAsync(ct), true))
            throw new InvalidOperationException("Runtime role or grainledger schema missing. Run owner migrations and configure an existing restricted role.");

        // Table-level REVOKE does not remove column-level grants.
        await using (var setRole = new NpgsqlCommand("SELECT set_config('grainledger.runtime_role', $1, true)", connection, transaction))
        {
            setRole.Parameters.AddWithValue(runtimeRole);
            await setRole.ExecuteNonQueryAsync(ct);
        }

        await using var columns = new NpgsqlCommand("""
            DO $revoke$
            DECLARE item record;
            BEGIN
                FOR item IN
                    SELECT n.nspname, c.relname, string_agg(quote_ident(a.attname), ', ' ORDER BY a.attnum) AS columns
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                    WHERE n.nspname = 'grainledger' AND c.relkind IN ('r','p','v','m','f')
                    GROUP BY n.nspname, c.relname
                LOOP
                    EXECUTE format('REVOKE SELECT (%1$s), INSERT (%1$s), UPDATE (%1$s), REFERENCES (%1$s) ON TABLE %2$I.%3$I FROM PUBLIC, %4$I',
                        item.columns, item.nspname, item.relname, current_setting('grainledger.runtime_role'));
                END LOOP;
            END
            $revoke$;
            """, connection, transaction);
        await columns.ExecuteNonQueryAsync(ct);
        var role = new NpgsqlCommandBuilder().QuoteIdentifier(runtimeRole);
        await using var grant = new NpgsqlCommand($"""
            REVOKE ALL ON SCHEMA grainledger FROM PUBLIC, {role};

            REVOKE ALL ON ALL TABLES IN SCHEMA grainledger FROM PUBLIC, {role};

            REVOKE ALL ON ALL SEQUENCES IN SCHEMA grainledger FROM PUBLIC, {role};

            REVOKE ALL ON ALL ROUTINES IN SCHEMA grainledger FROM PUBLIC, {role};

            GRANT USAGE ON SCHEMA grainledger TO {role};

            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
                grainledger."capture_business_events",
                grainledger."capture_code_counters",
                grainledger."capture_crop_seasons",
                grainledger."capture_farmers",
                grainledger."capture_lab_confirmed_associations",
                grainledger."capture_lab_report_confirmations",
                grainledger."capture_lab_reports",
                grainledger."capture_lab_result_sets",
                grainledger."capture_lab_results",
                grainledger."capture_offline_operations",
                grainledger."capture_plot_seasons",
                grainledger."capture_plots",
                grainledger."capture_procurement_items",
                grainledger."capture_procurements",
                grainledger."capture_sample_handovers",
                grainledger."capture_samples",
                grainledger."capture_source_lots",
                grainledger."capture_spray_declarations",
                grainledger."capture_spray_items",
                grainledger."catalog_active_substances",
                grainledger."catalog_catalog_events",
                grainledger."catalog_commodities",
                grainledger."catalog_markets",
                grainledger."catalog_measurement_units",
                grainledger."catalog_mrl_limits",
                grainledger."catalog_mrl_versions",
                grainledger."catalog_quality_attributes",
                grainledger."catalog_rice_varieties",
                grainledger."exports_blend_plan_drafts",
                grainledger."exports_blend_plan_events",
                grainledger."exports_blend_plan_previews",
                grainledger."exports_blend_plan_proposals",
                grainledger."exports_code_counters",
                grainledger."exports_containers",
                grainledger."exports_events",
                grainledger."exports_export_orders",
                grainledger."exports_idempotent_operations",
                grainledger."exports_order_specifications",
                grainledger."exports_specification_limits",
                grainledger."identity_audit_events",
                grainledger."identity_organizations",
                grainledger."identity_password_reset_codes",
                grainledger."identity_registration_attempts",
                grainledger."identity_sessions",
                grainledger."identity_users"
                TO {role};

            GRANT SELECT, INSERT ON TABLE
                grainledger."capture_lab_report_retirements"
                TO {role};

            GRANT SELECT ON TABLE
                grainledger."capture_residue_report_selections",
                grainledger."capture_residue_source_group_versions",
                grainledger."catalog_blend_assessment_policies",
                grainledger."catalog_residue_policy_setting_versions",
                grainledger."catalog_residue_rule_versions",
                grainledger."ledger_arrival_measurement_sets",
                grainledger."ledger_arrival_measurements",
                grainledger."ledger_arrival_operations",
                grainledger."ledger_arrival_reviews",
                grainledger."ledger_assignments",
                grainledger."ledger_catalog_commodities",
                grainledger."ledger_commodities",
                grainledger."ledger_compatibilities",
                grainledger."ledger_contributions",
                grainledger."ledger_corrections",
                grainledger."ledger_equipment",
                grainledger."ledger_events",
                grainledger."ledger_evidence",
                grainledger."ledger_facilities",
                grainledger."ledger_holds",
                grainledger."ledger_locations",
                grainledger."ledger_lot_code_reservations",
                grainledger."ledger_lot_codes",
                grainledger."ledger_lots",
                grainledger."ledger_notifications",
                grainledger."ledger_operations",
                grainledger."ledger_policies",
                grainledger."ledger_positions",
                grainledger."ledger_postings",
                grainledger."ledger_receipt_correction_blocks",
                grainledger."ledger_receipt_reversals",
                grainledger."ledger_receiving",
                grainledger."ledger_receiving_blocks"
                TO {role};

            GRANT USAGE, SELECT ON SEQUENCE grainledger."identity_audit_events_Id_seq" TO {role};

            GRANT EXECUTE ON FUNCTION 
                            grainledger.ledger_command(uuid,text,jsonb),grainledger.ledger_read(uuid,text,jsonb),grainledger.ledger_trace_lot(uuid,uuid,text,text),grainledger.ledger_trace_starts(uuid,text,text,uuid,uuid,integer,integer),grainledger.ledger_lock_writes(),
                            grainledger.ledger_provision_catalog_commodities(uuid),
                            grainledger.ledger_register_pending_procurement(uuid,uuid),grainledger.ledger_reserve_source_lot_code(uuid,uuid,uuid,uuid),
                            grainledger.ledger_receiving_detail(uuid,uuid),grainledger.ledger_receiving_tasks(uuid,uuid,integer,integer),
                            grainledger.ledger_procurement_stock(uuid,uuid),grainledger.ledger_procurement_stock_bulk(uuid,uuid[]),
                            grainledger.ledger_planning_candidates(uuid,uuid,uuid),grainledger.ledger_planning_compatibilities(uuid,uuid,uuid),
                            grainledger.ledger_publish_planning_yield_rule(uuid,jsonb),grainledger.ledger_planning_yield_rule(uuid,uuid,uuid,uuid,timestamptz),
                            grainledger.ledger_confirm_planning_quality_observation(uuid,jsonb),
                            grainledger.ledger_planning_quality_observations(uuid,uuid,uuid,uuid[],timestamptz),
                            grainledger.ledger_procurement_source_lineage(uuid,uuid),
                            grainledger.ledger_arrival_command(uuid,uuid,text,jsonb),
                            grainledger.ledger_receiving_threshold_read(uuid,text,jsonb),grainledger.ledger_receiving_threshold_command(uuid,text,jsonb),
                            grainledger.ledger_residue_graph(uuid,uuid),grainledger.ledger_residue_history(uuid,uuid,uuid),
                            grainledger.ledger_residue_replay(uuid,uuid,uuid,jsonb),grainledger.ledger_residue_append(uuid,uuid,uuid,jsonb,jsonb),
                            grainledger.catalog_publish_residue_rule(uuid,jsonb),grainledger.catalog_publish_residue_policy_setting(uuid,jsonb),grainledger.capture_publish_residue_group(uuid,jsonb),
                            grainledger.catalog_publish_blend_assessment_policy(uuid,jsonb),
                            grainledger.capture_publish_residue_selection(uuid,jsonb),
                            grainledger.ledger_can_read_lot(uuid,uuid),grainledger.ledger_assigned(uuid,uuid),
                            grainledger.ledger_own(uuid,uuid),grainledger.ledger_role(uuid)
             TO {role};
            """, connection, transaction);
        await grant.ExecuteNonQueryAsync(ct);
        await using var approvalGrants = new NpgsqlCommand("""
            DO $grants$
            DECLARE runtime_role text := current_setting('grainledger.runtime_role');
            BEGIN
                IF to_regprocedure('grainledger.ledger_container_action(uuid,text,jsonb)') IS NOT NULL THEN
                    EXECUTE format('GRANT EXECUTE ON FUNCTION grainledger.ledger_container_action(uuid,text,jsonb) TO %I', runtime_role);
                END IF;
                IF to_regclass('grainledger.exports_blend_plan_decisions') IS NOT NULL THEN
                    EXECUTE format('GRANT SELECT, INSERT ON grainledger.exports_blend_plan_decisions TO %I', runtime_role);
                    EXECUTE format('GRANT SELECT, INSERT, DELETE ON grainledger.exports_active_blend_approvals TO %I', runtime_role);
                    EXECUTE format('GRANT SELECT ON grainledger.ledger_blend_reservations, grainledger.ledger_blend_reservation_events TO %I', runtime_role);
                END IF;
                IF to_regprocedure('grainledger.ledger_reserve_approved_blend(uuid,uuid)') IS NOT NULL THEN
                    EXECUTE format('GRANT EXECUTE ON FUNCTION grainledger.ledger_reserve_approved_blend(uuid,uuid) TO %I', runtime_role);
                END IF;
                IF to_regprocedure('grainledger.ledger_release_revoked_blend(uuid,uuid)') IS NOT NULL THEN
                    EXECUTE format('GRANT EXECUTE ON FUNCTION grainledger.ledger_release_revoked_blend(uuid,uuid) TO %I', runtime_role);
                END IF;
            END $grants$;
            """, connection, transaction);
        await approvalGrants.ExecuteNonQueryAsync(ct);
        await EnsureRuntimeRoleAsync(connection, transaction, runtimeRole, ct);
        await transaction.CommitAsync(ct);
    }

    public static async Task ValidateRuntimeAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var identities = new NpgsqlCommand("SELECT session_user, current_user", connection);
        await using var reader = await identities.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var sessionRole = reader.GetString(0);
        var currentRole = reader.GetString(1);
        await reader.CloseAsync();
        await EnsureRuntimeRoleAsync(connection, null, sessionRole, ct);
        if (currentRole != sessionRole)
            await EnsureRuntimeRoleAsync(connection, null, currentRole, ct);
    }

    private static async Task EnsureRuntimeRoleAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string role, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            WITH RECURSIVE expected_table(name,can_select,can_insert,can_update,can_delete) AS (VALUES
              ('capture_business_events',true,true,true,true),
              ('capture_code_counters',true,true,true,true),
              ('capture_crop_seasons',true,true,true,true),
              ('capture_farmers',true,true,true,true),
              ('capture_lab_confirmed_associations',true,true,true,true),
              ('capture_lab_report_confirmations',true,true,true,true),
              ('capture_lab_report_retirements',true,true,false,false),
              ('capture_lab_reports',true,true,true,true),
              ('capture_lab_result_sets',true,true,true,true),
              ('capture_lab_results',true,true,true,true),
              ('capture_offline_operations',true,true,true,true),
              ('capture_plot_seasons',true,true,true,true),
              ('capture_plots',true,true,true,true),
              ('capture_procurement_items',true,true,true,true),
              ('capture_procurements',true,true,true,true),
              ('capture_residue_report_selections',true,false,false,false),
              ('capture_residue_source_group_versions',true,false,false,false),
              ('capture_sample_handovers',true,true,true,true),
              ('capture_samples',true,true,true,true),
              ('capture_source_lots',true,true,true,true),
              ('capture_spray_declarations',true,true,true,true),
              ('capture_spray_items',true,true,true,true),
              ('catalog_active_substances',true,true,true,true),
              ('catalog_blend_assessment_policies',true,false,false,false),
              ('catalog_catalog_events',true,true,true,true),
              ('catalog_commodities',true,true,true,true),
              ('catalog_markets',true,true,true,true),
              ('catalog_measurement_units',true,true,true,true),
              ('catalog_mrl_limits',true,true,true,true),
              ('catalog_mrl_versions',true,true,true,true),
              ('catalog_quality_attributes',true,true,true,true),
              ('catalog_residue_policy_setting_versions',true,false,false,false),
              ('catalog_residue_rule_versions',true,false,false,false),
              ('catalog_rice_varieties',true,true,true,true),
              ('exports_blend_plan_drafts',true,true,true,true),
              ('exports_blend_plan_events',true,true,true,true),
              ('exports_blend_plan_previews',true,true,true,true),
              ('exports_blend_plan_proposals',true,true,true,true),
              ('exports_code_counters',true,true,true,true),
              ('exports_containers',true,true,true,true),
              ('exports_events',true,true,true,true),
              ('exports_export_orders',true,true,true,true),
              ('exports_idempotent_operations',true,true,true,true),
              ('exports_order_specifications',true,true,true,true),
              ('exports_specification_limits',true,true,true,true),
              ('identity_audit_events',true,true,true,true),
              ('identity_organizations',true,true,true,true),
              ('identity_password_reset_codes',true,true,true,true),
              ('identity_registration_attempts',true,true,true,true),
              ('identity_sessions',true,true,true,true),
              ('identity_users',true,true,true,true),
              ('ledger_arrival_measurement_sets',true,false,false,false),
              ('ledger_arrival_measurements',true,false,false,false),
              ('ledger_arrival_operations',true,false,false,false),
              ('ledger_arrival_reviews',true,false,false,false),
              ('ledger_assignments',true,false,false,false),
              ('ledger_catalog_commodities',true,false,false,false),
              ('ledger_commodities',true,false,false,false),
              ('ledger_compatibilities',true,false,false,false),
              ('ledger_contributions',true,false,false,false),
              ('ledger_corrections',true,false,false,false),
              ('ledger_custody_allocations',false,false,false,false),
              ('ledger_custody_edges',false,false,false,false),
              ('ledger_custody_events',false,false,false,false),
              ('ledger_custody_processing_allocations',false,false,false,false),
              ('ledger_equipment',true,false,false,false),
              ('ledger_events',true,false,false,false),
              ('ledger_evidence',true,false,false,false),
              ('ledger_facilities',true,false,false,false),
              ('ledger_holds',true,false,false,false),
              ('ledger_locations',true,false,false,false),
              ('ledger_lot_code_reservations',true,false,false,false),
              ('ledger_lot_codes',true,false,false,false),
              ('ledger_lots',true,false,false,false),
              ('ledger_mixed_bulk_command_results',false,false,false,false),
              ('ledger_mixed_bulk_policies',false,false,false,false),
              ('ledger_mixed_bulk_pools',false,false,false,false),
              ('ledger_notifications',true,false,false,false),
              ('ledger_operations',true,false,false,false),
              ('ledger_planning_quality_observations',false,false,false,false),
              ('ledger_planning_yield_rules',false,false,false,false),
              ('ledger_policies',true,false,false,false),
              ('ledger_positions',true,false,false,false),
              ('ledger_postings',true,false,false,false),
              ('ledger_receipt_correction_blocks',true,false,false,false),
              ('ledger_receipt_reversals',true,false,false,false),
              ('ledger_receiving',true,false,false,false),
              ('ledger_receiving_blocks',true,false,false,false),
              ('ledger_receiving_threshold_default',false,false,false,false),
              ('ledger_receiving_threshold_history',false,false,false,false),
              ('ledger_receiving_threshold_overrides',false,false,false,false),
              ('ledger_residue_evaluations',false,false,false,false)
            ), optional_table(name,can_select,can_insert,can_update,can_delete) AS (VALUES
              ('ledger_containers',false,false,false,false),
              ('ledger_container_loading_drafts',false,false,false,false),
              ('ledger_container_events',false,false,false,false),
              ('ledger_container_requests',false,false,false,false),
              ('exports_blend_plan_decisions',true,true,false,false),
              ('exports_active_blend_approvals',true,true,false,true),
              ('ledger_blend_reservations',true,false,false,false),
              ('ledger_blend_reservation_events',true,false,false,false)
            ), expected_function(signature) AS (VALUES
              ('grainledger.ledger_command(uuid,text,jsonb)'),
              ('grainledger.ledger_read(uuid,text,jsonb)'),
              ('grainledger.ledger_trace_lot(uuid,uuid,text,text)'),
              ('grainledger.ledger_trace_starts(uuid,text,text,uuid,uuid,integer,integer)'),
              ('grainledger.ledger_lock_writes()'),
              ('grainledger.ledger_provision_catalog_commodities(uuid)'),
              ('grainledger.ledger_register_pending_procurement(uuid,uuid)'),
              ('grainledger.ledger_reserve_source_lot_code(uuid,uuid,uuid,uuid)'),
              ('grainledger.ledger_receiving_detail(uuid,uuid)'),
              ('grainledger.ledger_receiving_tasks(uuid,uuid,integer,integer)'),
              ('grainledger.ledger_procurement_stock(uuid,uuid)'),
              ('grainledger.ledger_procurement_stock_bulk(uuid,uuid[])'),
              ('grainledger.ledger_planning_candidates(uuid,uuid,uuid)'),
              ('grainledger.ledger_planning_compatibilities(uuid,uuid,uuid)'),
              ('grainledger.ledger_publish_planning_yield_rule(uuid,jsonb)'),
              ('grainledger.ledger_planning_yield_rule(uuid,uuid,uuid,uuid,timestamptz)'),
              ('grainledger.ledger_confirm_planning_quality_observation(uuid,jsonb)'),
              ('grainledger.ledger_planning_quality_observations(uuid,uuid,uuid,uuid[],timestamptz)'),
              ('grainledger.ledger_procurement_source_lineage(uuid,uuid)'),
              ('grainledger.ledger_arrival_command(uuid,uuid,text,jsonb)'),
              ('grainledger.ledger_receiving_threshold_read(uuid,text,jsonb)'),
              ('grainledger.ledger_receiving_threshold_command(uuid,text,jsonb)'),
              ('grainledger.ledger_residue_graph(uuid,uuid)'),
              ('grainledger.ledger_residue_history(uuid,uuid,uuid)'),
              ('grainledger.ledger_residue_replay(uuid,uuid,uuid,jsonb)'),
              ('grainledger.ledger_residue_append(uuid,uuid,uuid,jsonb,jsonb)'),
              ('grainledger.catalog_publish_residue_rule(uuid,jsonb)'),
              ('grainledger.catalog_publish_residue_policy_setting(uuid,jsonb)'),
              ('grainledger.capture_publish_residue_group(uuid,jsonb)'),
              ('grainledger.catalog_publish_blend_assessment_policy(uuid,jsonb)'),
              ('grainledger.capture_publish_residue_selection(uuid,jsonb)'),
              ('grainledger.ledger_can_read_lot(uuid,uuid)'),
              ('grainledger.ledger_assigned(uuid,uuid)'),
              ('grainledger.ledger_own(uuid,uuid)'),
              ('grainledger.ledger_role(uuid)')
            ), optional_function(signature) AS (VALUES ('grainledger.ledger_reserve_approved_blend(uuid,uuid)'),
              ('grainledger.ledger_release_revoked_blend(uuid,uuid)'),
              ('grainledger.ledger_container_action(uuid,text,jsonb)')
            ), expected_sequence(name,can_usage,can_select,can_update) AS (VALUES
              ('identity_audit_events_Id_seq',true,true,false),
              ('ledger_lot_code_serial',false,false,false)
            ), app_tables AS (
              SELECT c.oid,c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='grainledger' AND c.relkind IN ('r','p','v','m','f')
            ), app_functions AS (
              SELECT p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='grainledger'
            ), app_sequences AS (
              SELECT c.oid,c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='grainledger' AND c.relkind='S'
            ), reachable_roles(oid) AS (
              SELECT oid FROM pg_roles WHERE rolname=$1
              UNION
              SELECT m.roleid FROM reachable_roles r
              JOIN pg_auth_members m ON (m.member=r.oid AND m.set_option)
                OR (m.admin_option AND pg_has_role(r.oid,m.member,'USAGE'))
            )
            SELECT NOT EXISTS (SELECT 1 FROM pg_roles r WHERE r.rolname=$1
                AND NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolbypassrls
                AND NOT EXISTS (SELECT 1 FROM pg_roles e WHERE (e.rolsuper OR e.rolcreatedb OR e.rolcreaterole OR e.rolbypassrls) AND pg_has_role(r.oid,e.oid,'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_database x WHERE x.datname=current_database() AND pg_has_role(r.oid,x.datdba,'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_namespace n WHERE n.nspname='grainledger' AND pg_has_role(r.oid,n.nspowner,'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='grainledger' AND pg_has_role(r.oid,c.relowner,'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='grainledger' AND pg_has_role(r.oid,p.proowner,'MEMBER')))
             OR to_regnamespace('grainledger') IS NULL
             OR NOT coalesce(has_schema_privilege($1,to_regnamespace('grainledger'),'USAGE'),false)
             OR coalesce(has_schema_privilege($1,to_regnamespace('grainledger'),'CREATE'),false)
             OR EXISTS (SELECT 1 FROM expected_table e LEFT JOIN app_tables t ON t.relname=e.name WHERE t.oid IS NULL)
             OR EXISTS (SELECT 1 FROM app_tables t LEFT JOIN (SELECT * FROM expected_table UNION ALL SELECT * FROM optional_table) e ON e.name=t.relname WHERE
                has_table_privilege($1,t.oid,'SELECT') IS DISTINCT FROM coalesce(e.can_select,false)
                OR has_table_privilege($1,t.oid,'INSERT') IS DISTINCT FROM coalesce(e.can_insert,false)
                OR has_table_privilege($1,t.oid,'UPDATE') IS DISTINCT FROM coalesce(e.can_update,false)
                OR has_table_privilege($1,t.oid,'DELETE') IS DISTINCT FROM coalesce(e.can_delete,false)
                OR has_table_privilege($1,t.oid,'TRUNCATE,REFERENCES,TRIGGER')
                OR (NOT coalesce(e.can_select,false) AND has_any_column_privilege($1,t.oid,'SELECT'))
                OR (NOT coalesce(e.can_insert,false) AND has_any_column_privilege($1,t.oid,'INSERT'))
                OR (NOT coalesce(e.can_update,false) AND has_any_column_privilege($1,t.oid,'UPDATE'))
                OR has_any_column_privilege($1,t.oid,'REFERENCES'))
             OR EXISTS (SELECT 1 FROM expected_function e WHERE to_regprocedure(e.signature) IS NULL OR NOT has_function_privilege($1,to_regprocedure(e.signature),'EXECUTE'))
             OR EXISTS (SELECT 1 FROM optional_function e WHERE to_regprocedure(e.signature) IS NOT NULL AND NOT has_function_privilege($1,to_regprocedure(e.signature),'EXECUTE'))
             OR EXISTS (SELECT 1 FROM app_functions f WHERE has_function_privilege($1,f.oid,'EXECUTE') AND NOT EXISTS (SELECT 1 FROM (SELECT * FROM expected_function UNION ALL SELECT * FROM optional_function) e WHERE to_regprocedure(e.signature)=f.oid))
             OR EXISTS (SELECT 1 FROM expected_sequence e LEFT JOIN app_sequences s ON s.relname=e.name WHERE s.oid IS NULL)
             OR EXISTS (SELECT 1 FROM app_sequences s LEFT JOIN expected_sequence e ON e.name=s.relname WHERE
                has_sequence_privilege($1,s.oid,'USAGE') IS DISTINCT FROM coalesce(e.can_usage,false)
                OR has_sequence_privilege($1,s.oid,'SELECT') IS DISTINCT FROM coalesce(e.can_select,false)
                OR has_sequence_privilege($1,s.oid,'UPDATE') IS DISTINCT FROM coalesce(e.can_update,false))
             -- ADMIN can grant SET to the login role, including through inherited
             -- administrative grants. Follow those edges as well as SET edges.
             OR EXISTS (
                SELECT 1 FROM reachable_roles reachable
                WHERE (
                    coalesce(has_schema_privilege(reachable.oid,to_regnamespace('grainledger'),'CREATE'),false)
                    OR EXISTS (SELECT 1 FROM app_tables t LEFT JOIN (SELECT * FROM expected_table UNION ALL SELECT * FROM optional_table) e ON e.name=t.relname WHERE
                        (NOT coalesce(e.can_select,false) AND (has_table_privilege(reachable.oid,t.oid,'SELECT') OR has_any_column_privilege(reachable.oid,t.oid,'SELECT')))
                        OR (NOT coalesce(e.can_insert,false) AND (has_table_privilege(reachable.oid,t.oid,'INSERT') OR has_any_column_privilege(reachable.oid,t.oid,'INSERT')))
                        OR (NOT coalesce(e.can_update,false) AND (has_table_privilege(reachable.oid,t.oid,'UPDATE') OR has_any_column_privilege(reachable.oid,t.oid,'UPDATE')))
                        OR (NOT coalesce(e.can_delete,false) AND has_table_privilege(reachable.oid,t.oid,'DELETE'))
                        OR has_table_privilege(reachable.oid,t.oid,'TRUNCATE,REFERENCES,TRIGGER')
                        OR has_any_column_privilege(reachable.oid,t.oid,'REFERENCES'))
                    OR EXISTS (SELECT 1 FROM app_functions f WHERE has_function_privilege(reachable.oid,f.oid,'EXECUTE')
                        AND NOT EXISTS (SELECT 1 FROM (SELECT * FROM expected_function UNION ALL SELECT * FROM optional_function) e WHERE to_regprocedure(e.signature)=f.oid))
                    OR EXISTS (SELECT 1 FROM app_sequences s LEFT JOIN expected_sequence e ON e.name=s.relname WHERE
                        (NOT coalesce(e.can_usage,false) AND has_sequence_privilege(reachable.oid,s.oid,'USAGE'))
                        OR (NOT coalesce(e.can_select,false) AND has_sequence_privilege(reachable.oid,s.oid,'SELECT'))
                        OR (NOT coalesce(e.can_update,false) AND has_sequence_privilege(reachable.oid,s.oid,'UPDATE')))
                ));
            """, connection, transaction);
        command.Parameters.AddWithValue(role);
        if (!Equals(await command.ExecuteScalarAsync(ct), false))
            throw new InvalidOperationException("Runtime role is elevated, owns application objects, has unsafe inherited privileges, or is missing required database objects or grants. Run owner migrations and runtime-role configuration with a restricted role; normal startup does not repair grants.");
    }
}

public static class LedgerModuleRegistration
{
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LedgerService>();
        services.AddScoped<ResidueEvaluationService>();
        services.AddScoped<ProcurementLedgerPort>();
        services.AddScoped<CommodityLedgerPort>();
        return services;
    }
}
