ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_IX_lab_report_retirements_company_id_report_id" UNIQUE (company_id, report_id);

ALTER TABLE ONLY grainledger.capture_business_events
    ADD CONSTRAINT "capture_PK_business_events" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_crop_seasons
    ADD CONSTRAINT "capture_PK_crop_seasons" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_farmers
    ADD CONSTRAINT "capture_PK_farmers" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_offline_operations
    ADD CONSTRAINT "capture_PK_offline_operations" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_plot_seasons
    ADD CONSTRAINT "capture_PK_plot_seasons" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_plots
    ADD CONSTRAINT "capture_PK_plots" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_procurement_items
    ADD CONSTRAINT "capture_PK_procurement_items" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_procurements
    ADD CONSTRAINT "capture_PK_procurements" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_sample_handovers
    ADD CONSTRAINT "capture_PK_sample_handovers" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_samples
    ADD CONSTRAINT "capture_PK_samples" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_source_lots
    ADD CONSTRAINT "capture_PK_source_lots" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_spray_declarations
    ADD CONSTRAINT "capture_PK_spray_declarations" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_spray_items
    ADD CONSTRAINT "capture_PK_spray_items" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_lab_confirmed_associations
    ADD CONSTRAINT "capture_lab_confirmed_associations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_lab_confirmed_associations
    ADD CONSTRAINT "capture_lab_confirmed_associations_result_set_id_key" UNIQUE (result_set_id);

ALTER TABLE ONLY grainledger.capture_lab_report_confirmations
    ADD CONSTRAINT "capture_lab_report_confirmations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_lab_report_confirmations
    ADD CONSTRAINT "capture_lab_report_confirmations_report_id_key" UNIQUE (report_id);

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_lab_report_retirements_pkey" PRIMARY KEY (report_id);

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_lab_report_retirements_replacement_report_id_key" UNIQUE (replacement_report_id);

ALTER TABLE ONLY grainledger.capture_lab_reports
    ADD CONSTRAINT "capture_lab_reports_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_lab_result_sets
    ADD CONSTRAINT "capture_lab_result_sets_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_lab_results
    ADD CONSTRAINT "capture_lab_results_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_code_counters
    ADD CONSTRAINT "capture_pk_code_counters" PRIMARY KEY (company_id, kind, scope);

ALTER TABLE ONLY grainledger.capture_residue_report_selections
    ADD CONSTRAINT "capture_residue_report_selections_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_residue_source_group_versions
    ADD CONSTRAINT "capture_residue_source_group_versions_company_id_c_789bca3ba9c8" UNIQUE (company_id, code, version_no);

ALTER TABLE ONLY grainledger.capture_residue_source_group_versions
    ADD CONSTRAINT "capture_residue_source_group_versions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.capture_lab_report_confirmations
    ADD CONSTRAINT "capture_ux_lab_confirmations_company_id" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_reports
    ADD CONSTRAINT "capture_ux_lab_reports_company_id" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_result_sets
    ADD CONSTRAINT "capture_ux_lab_result_sets_company_id" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.catalog_active_substances
    ADD CONSTRAINT "catalog_PK_active_substances" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_catalog_events
    ADD CONSTRAINT "catalog_PK_catalog_events" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_commodities
    ADD CONSTRAINT "catalog_PK_commodities" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_markets
    ADD CONSTRAINT "catalog_PK_markets" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_measurement_units
    ADD CONSTRAINT "catalog_PK_measurement_units" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_mrl_limits
    ADD CONSTRAINT "catalog_PK_mrl_limits" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_mrl_versions
    ADD CONSTRAINT "catalog_PK_mrl_versions" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_quality_attributes
    ADD CONSTRAINT "catalog_PK_quality_attributes" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_rice_varieties
    ADD CONSTRAINT "catalog_PK_rice_varieties" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_blend_assessment_policies_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_residue_policy_setting_versions
    ADD CONSTRAINT "catalog_residue_policy_setting_versio_company_id_r_af1894e1c62d" UNIQUE (company_id, rule_code, version_no);

ALTER TABLE ONLY grainledger.catalog_residue_policy_setting_versions
    ADD CONSTRAINT "catalog_residue_policy_setting_versions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_residue_rule_versions
    ADD CONSTRAINT "catalog_residue_rule_versions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_uq_blend_assessment_version" UNIQUE (company_id, market_id, commodity_id, version_no);

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_PK_blend_plan_drafts" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_blend_plan_events
    ADD CONSTRAINT "exports_PK_blend_plan_events" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_blend_plan_previews
    ADD CONSTRAINT "exports_blend_plan_previews_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_blend_plan_proposals
    ADD CONSTRAINT "exports_blend_plan_proposals_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_blend_plan_proposals
    ADD CONSTRAINT "exports_blend_plan_proposals_plan_id_key" UNIQUE (plan_id);

ALTER TABLE ONLY grainledger.exports_code_counters
    ADD CONSTRAINT "exports_code_counters_pkey" PRIMARY KEY (company_id, kind);

ALTER TABLE ONLY grainledger.exports_containers
    ADD CONSTRAINT "exports_containers_company_id_id_key" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.exports_containers
    ADD CONSTRAINT "exports_containers_company_id_internal_code_key" UNIQUE (company_id, internal_code);

ALTER TABLE ONLY grainledger.exports_containers
    ADD CONSTRAINT "exports_containers_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_events
    ADD CONSTRAINT "exports_events_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_company_id_code_key" UNIQUE (company_id, code);

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_company_id_id_key" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_idempotent_operations
    ADD CONSTRAINT "exports_idempotent_operations_company_id_actor_id__ad815d67311d" UNIQUE (company_id, actor_id, operation_id);

ALTER TABLE ONLY grainledger.exports_idempotent_operations
    ADD CONSTRAINT "exports_idempotent_operations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_order_specifications
    ADD CONSTRAINT "exports_order_specifications_order_id_id_key" UNIQUE (order_id, id);

ALTER TABLE ONLY grainledger.exports_order_specifications
    ADD CONSTRAINT "exports_order_specifications_order_id_version_no_key" UNIQUE (order_id, version_no);

ALTER TABLE ONLY grainledger.exports_order_specifications
    ADD CONSTRAINT "exports_order_specifications_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_specification_limits
    ADD CONSTRAINT "exports_specification_limits_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.exports_specification_limits
    ADD CONSTRAINT "exports_specification_limits_specification_id_attribute_id_key" UNIQUE (specification_id, attribute_id);

ALTER TABLE ONLY grainledger.exports_blend_plan_previews
    ADD CONSTRAINT "exports_uq_blend_preview_company_id" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_proposals
    ADD CONSTRAINT "exports_uq_blend_proposal_company_id" UNIQUE (company_id, id);

ALTER TABLE ONLY grainledger.exports_containers
    ADD CONSTRAINT "exports_uq_container_order" UNIQUE (order_id, id);

ALTER TABLE ONLY grainledger.identity_audit_events
    ADD CONSTRAINT "identity_PK_audit_events" PRIMARY KEY ("Id");

ALTER TABLE ONLY grainledger.identity_organizations
    ADD CONSTRAINT "identity_PK_organizations" PRIMARY KEY ("Id");

ALTER TABLE ONLY grainledger.identity_password_reset_codes
    ADD CONSTRAINT "identity_PK_password_reset_codes" PRIMARY KEY ("Id");

ALTER TABLE ONLY grainledger.identity_registration_attempts
    ADD CONSTRAINT "identity_PK_registration_attempts" PRIMARY KEY ("Id");

ALTER TABLE ONLY grainledger.identity_sessions
    ADD CONSTRAINT "identity_PK_sessions" PRIMARY KEY ("Id");

ALTER TABLE ONLY grainledger.identity_users
    ADD CONSTRAINT "identity_PK_users" PRIMARY KEY ("Id");

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_operation_id_key" UNIQUE (operation_id);

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_procurement_id_id_key" UNIQUE (procurement_id, id);

ALTER TABLE ONLY grainledger.ledger_arrival_measurements
    ADD CONSTRAINT "ledger_arrival_measurements_pkey" PRIMARY KEY (measurement_set_id, procurement_item_id);

ALTER TABLE ONLY grainledger.ledger_arrival_operations
    ADD CONSTRAINT "ledger_arrival_operations_pkey" PRIMARY KEY (operation_id);

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_operation_id_key" UNIQUE (operation_id);

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_procurement_id_id_key" UNIQUE (procurement_id, id);

ALTER TABLE ONLY grainledger.ledger_assignments
    ADD CONSTRAINT "ledger_assignments_pkey" PRIMARY KEY (user_id, facility_id);

ALTER TABLE ONLY grainledger.ledger_catalog_commodities
    ADD CONSTRAINT "ledger_catalog_commodities_ledger_commodity_id_key" UNIQUE (ledger_commodity_id);

ALTER TABLE ONLY grainledger.ledger_catalog_commodities
    ADD CONSTRAINT "ledger_catalog_commodities_pkey" PRIMARY KEY (catalog_commodity_id);

ALTER TABLE ONLY grainledger.ledger_commodities
    ADD CONSTRAINT "ledger_commodities_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_compatibilities
    ADD CONSTRAINT "ledger_compatibilities_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_contributions
    ADD CONSTRAINT "ledger_contributions_pkey" PRIMARY KEY (lot_id, source_lot_id);

ALTER TABLE ONLY grainledger.ledger_corrections
    ADD CONSTRAINT "ledger_corrections_operation_id_key" UNIQUE (operation_id);

ALTER TABLE ONLY grainledger.ledger_corrections
    ADD CONSTRAINT "ledger_corrections_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_corrections
    ADD CONSTRAINT "ledger_corrections_replacement_operation_id_key" UNIQUE (replacement_operation_id);

ALTER TABLE ONLY grainledger.ledger_custody_allocations
    ADD CONSTRAINT "ledger_custody_allocations_pkey" PRIMARY KEY (event_id, input_lot_id, output_lot_id, source_lot_id);

ALTER TABLE ONLY grainledger.ledger_custody_edges
    ADD CONSTRAINT "ledger_custody_edges_pkey" PRIMARY KEY (event_id, input_lot_id, output_lot_id);

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_pkey" PRIMARY KEY (event_id);

ALTER TABLE ONLY grainledger.ledger_custody_processing_allocations
    ADD CONSTRAINT "ledger_custody_processing_allocations_pkey" PRIMARY KEY (event_id, input_lot_id, output_lot_id, source_lot_id);

ALTER TABLE ONLY grainledger.ledger_equipment
    ADD CONSTRAINT "ledger_equipment_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_events
    ADD CONSTRAINT "ledger_events_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_evidence
    ADD CONSTRAINT "ledger_evidence_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_facilities
    ADD CONSTRAINT "ledger_facilities_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_holds
    ADD CONSTRAINT "ledger_holds_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_locations
    ADD CONSTRAINT "ledger_locations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_lot_code_reservations
    ADD CONSTRAINT "ledger_lot_code_reservations_code_key" UNIQUE (code);

ALTER TABLE ONLY grainledger.ledger_lot_code_reservations
    ADD CONSTRAINT "ledger_lot_code_reservations_pkey" PRIMARY KEY (lot_id);

ALTER TABLE ONLY grainledger.ledger_lot_codes
    ADD CONSTRAINT "ledger_lot_codes_code_origin_lot_id_key" UNIQUE (code, origin_lot_id);

ALTER TABLE ONLY grainledger.ledger_lot_codes
    ADD CONSTRAINT "ledger_lot_codes_pkey" PRIMARY KEY (code);

ALTER TABLE ONLY grainledger.ledger_lots
    ADD CONSTRAINT "ledger_lots_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_lots
    ADD CONSTRAINT "ledger_lots_receipt_line_id_key" UNIQUE (receipt_line_id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_command_results
    ADD CONSTRAINT "ledger_mixed_bulk_command_results_pkey" PRIMARY KEY (action, command_id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_policies
    ADD CONSTRAINT "ledger_mixed_bulk_policies_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_pools
    ADD CONSTRAINT "ledger_mixed_bulk_pools_lot_id_key" UNIQUE (lot_id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_pools
    ADD CONSTRAINT "ledger_mixed_bulk_pools_pkey" PRIMARY KEY (location_id);

ALTER TABLE ONLY grainledger.ledger_notifications
    ADD CONSTRAINT "ledger_notifications_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_planning_yield_rules
    ADD CONSTRAINT "ledger_planning_yield_rules_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_policies
    ADD CONSTRAINT "ledger_policies_facility_id_kind_effective_from_key" UNIQUE (facility_id, kind, effective_from);

ALTER TABLE ONLY grainledger.ledger_policies
    ADD CONSTRAINT "ledger_policies_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_positions
    ADD CONSTRAINT "ledger_positions_pkey" PRIMARY KEY (lot_id, location_id);

ALTER TABLE ONLY grainledger.ledger_postings
    ADD CONSTRAINT "ledger_postings_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_postings
    ADD CONSTRAINT "ledger_postings_reverses_posting_id_key" UNIQUE (reverses_posting_id);

ALTER TABLE ONLY grainledger.ledger_receipt_correction_blocks
    ADD CONSTRAINT "ledger_receipt_correction_blocks_pkey" PRIMARY KEY (correction_id);

ALTER TABLE ONLY grainledger.ledger_receipt_correction_blocks
    ADD CONSTRAINT "ledger_receipt_correction_blocks_receipt_id_key" UNIQUE (receipt_id);

ALTER TABLE ONLY grainledger.ledger_receipt_reversals
    ADD CONSTRAINT "ledger_receipt_reversals_correction_id_key" UNIQUE (correction_id);

ALTER TABLE ONLY grainledger.ledger_receipt_reversals
    ADD CONSTRAINT "ledger_receipt_reversals_pkey" PRIMARY KEY (receipt_id);

ALTER TABLE ONLY grainledger.ledger_receiving_blocks
    ADD CONSTRAINT "ledger_receiving_blocks_pkey" PRIMARY KEY (discrepancy_id);

ALTER TABLE ONLY grainledger.ledger_receiving
    ADD CONSTRAINT "ledger_receiving_pkey" PRIMARY KEY (procurement_id);

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_default
    ADD CONSTRAINT "ledger_receiving_threshold_default_pkey" PRIMARY KEY (singleton);

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_default
    ADD CONSTRAINT "ledger_receiving_threshold_default_revision_key" UNIQUE (revision);

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_history
    ADD CONSTRAINT "ledger_receiving_threshold_history_pkey" PRIMARY KEY (revision);

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_overrides
    ADD CONSTRAINT "ledger_receiving_threshold_overrides_pkey" PRIMARY KEY (company_id);

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_overrides
    ADD CONSTRAINT "ledger_receiving_threshold_overrides_revision_key" UNIQUE (revision);

ALTER TABLE ONLY grainledger.ledger_residue_evaluations
    ADD CONSTRAINT "ledger_residue_evaluations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_uq_planning_quality_version" UNIQUE (owner_id, lot_id, attribute_id, version_no);

ALTER TABLE ONLY grainledger.ledger_planning_yield_rules
    ADD CONSTRAINT "ledger_uq_planning_yield_version" UNIQUE (facility_id, compatibility_id, version_no);

CREATE INDEX "capture_IX_business_events_actor_id" ON grainledger.capture_business_events USING btree (actor_id);

CREATE INDEX "capture_IX_business_events_company_id" ON grainledger.capture_business_events USING btree (company_id);

CREATE INDEX "capture_IX_farmers_company_id_created_at_id" ON grainledger.capture_farmers USING btree (company_id, created_at, id);

CREATE INDEX "capture_IX_lab_confirmed_associations_confirmation_id" ON grainledger.capture_lab_confirmed_associations USING btree (confirmation_id);

CREATE INDEX "capture_IX_lab_confirmed_associations_sample_id" ON grainledger.capture_lab_confirmed_associations USING btree (sample_id);

CREATE INDEX "capture_IX_lab_reports_company_id_recorded_at_id" ON grainledger.capture_lab_reports USING btree (company_id, recorded_at, id);

CREATE INDEX "capture_IX_lab_reports_related_report_id" ON grainledger.capture_lab_reports USING btree (related_report_id);

CREATE UNIQUE INDEX "capture_IX_lab_result_sets_report_id_position" ON grainledger.capture_lab_result_sets USING btree (report_id, "position");

CREATE INDEX "capture_IX_lab_result_sets_sample_id" ON grainledger.capture_lab_result_sets USING btree (sample_id);

CREATE UNIQUE INDEX "capture_IX_lab_results_result_set_id_position" ON grainledger.capture_lab_results USING btree (result_set_id, "position");

CREATE INDEX "capture_IX_lab_results_substance_id" ON grainledger.capture_lab_results USING btree (substance_id);

CREATE UNIQUE INDEX "capture_IX_offline_operations_applied_event_id" ON grainledger.capture_offline_operations USING btree (applied_event_id);

CREATE UNIQUE INDEX "capture_IX_offline_operations_company_id_id" ON grainledger.capture_offline_operations USING btree (company_id, id);

CREATE INDEX "capture_IX_offline_operations_submitted_by" ON grainledger.capture_offline_operations USING btree (submitted_by);

CREATE INDEX "capture_IX_plot_seasons_season_id" ON grainledger.capture_plot_seasons USING btree (season_id);

CREATE INDEX "capture_IX_procurement_items_plot_season_id" ON grainledger.capture_procurement_items USING btree (plot_season_id);

CREATE INDEX "capture_IX_procurements_company_id_received_at_id" ON grainledger.capture_procurements USING btree (company_id, received_at, id);

CREATE UNIQUE INDEX "capture_IX_procurements_event_id" ON grainledger.capture_procurements USING btree (event_id);

CREATE INDEX "capture_IX_sample_handovers_recorded_by" ON grainledger.capture_sample_handovers USING btree (recorded_by);

CREATE INDEX "capture_IX_sample_handovers_sample_id" ON grainledger.capture_sample_handovers USING btree (sample_id);

CREATE INDEX "capture_IX_samples_collected_by" ON grainledger.capture_samples USING btree (collected_by);

CREATE INDEX "capture_IX_samples_source_lot_id" ON grainledger.capture_samples USING btree (source_lot_id);

CREATE UNIQUE INDEX "capture_IX_source_lots_lot_id" ON grainledger.capture_source_lots USING btree (lot_id);

CREATE UNIQUE INDEX "capture_IX_source_lots_procurement_item_id" ON grainledger.capture_source_lots USING btree (procurement_item_id);

CREATE INDEX "capture_IX_spray_declarations_plot_season_id" ON grainledger.capture_spray_declarations USING btree (plot_season_id);

CREATE INDEX "capture_IX_spray_declarations_recorded_by" ON grainledger.capture_spray_declarations USING btree (recorded_by);

CREATE INDEX "capture_IX_spray_items_declaration_id" ON grainledger.capture_spray_items USING btree (declaration_id);

CREATE INDEX "capture_IX_spray_items_substance_id" ON grainledger.capture_spray_items USING btree (substance_id);

CREATE INDEX "capture_receiving_facility_queue" ON grainledger.capture_procurements USING btree (collection_site_id, received_at DESC, id) WHERE (receiving_workflow IS NOT NULL);

CREATE INDEX "capture_residue_groups_company" ON grainledger.capture_residue_source_group_versions USING btree (company_id, substance_id, published_at);

CREATE INDEX "capture_residue_groups_members" ON grainledger.capture_residue_source_group_versions USING gin (source_ids);

CREATE INDEX "capture_residue_selection_source" ON grainledger.capture_residue_report_selections USING btree (company_id, source_id, substance_id, published_at);

CREATE UNIQUE INDEX "capture_ux_crop_seasons_company_id" ON grainledger.capture_crop_seasons USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_farmers_company_code" ON grainledger.capture_farmers USING btree (company_id, code);

CREATE UNIQUE INDEX "capture_ux_farmers_company_code_number" ON grainledger.capture_farmers USING btree (company_id, code_number);

CREATE UNIQUE INDEX "capture_ux_farmers_company_id" ON grainledger.capture_farmers USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_offline_operations_company_device_operation" ON grainledger.capture_offline_operations USING btree (company_id, device_id, client_operation_id);

CREATE UNIQUE INDEX "capture_ux_plot_seasons_company_id" ON grainledger.capture_plot_seasons USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_plot_seasons_plot_season" ON grainledger.capture_plot_seasons USING btree (plot_id, season_id);

CREATE UNIQUE INDEX "capture_ux_plots_company_id" ON grainledger.capture_plots USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_plots_farmer_code" ON grainledger.capture_plots USING btree (farmer_id, code);

CREATE UNIQUE INDEX "capture_ux_procurement_items_company_id" ON grainledger.capture_procurement_items USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_procurement_items_receipt_line" ON grainledger.capture_procurement_items USING btree (procurement_id, line_no);

CREATE UNIQUE INDEX "capture_ux_procurements_company_code" ON grainledger.capture_procurements USING btree (company_id, code);

CREATE UNIQUE INDEX "capture_ux_procurements_company_id" ON grainledger.capture_procurements USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_sample_handovers_company_id" ON grainledger.capture_sample_handovers USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_samples_company_code" ON grainledger.capture_samples USING btree (company_id, code);

CREATE UNIQUE INDEX "capture_ux_samples_company_id" ON grainledger.capture_samples USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_source_lots_company_id" ON grainledger.capture_source_lots USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_spray_declarations_company_id" ON grainledger.capture_spray_declarations USING btree (company_id, id);

CREATE UNIQUE INDEX "capture_ux_spray_items_company_id" ON grainledger.capture_spray_items USING btree (company_id, id);

CREATE UNIQUE INDEX "catalog_IX_active_substances_code" ON grainledger.catalog_active_substances USING btree (code);

CREATE INDEX "catalog_IX_catalog_events_actor_id" ON grainledger.catalog_catalog_events USING btree (actor_id);

CREATE INDEX "catalog_IX_catalog_events_correlation_id" ON grainledger.catalog_catalog_events USING btree (correlation_id);

CREATE INDEX "catalog_IX_catalog_events_resource_type_resource_id_occurred_at" ON grainledger.catalog_catalog_events USING btree (resource_type, resource_id, occurred_at);

CREATE UNIQUE INDEX "catalog_IX_commodities_code" ON grainledger.catalog_commodities USING btree (code);

CREATE UNIQUE INDEX "catalog_IX_markets_code" ON grainledger.catalog_markets USING btree (code);

CREATE UNIQUE INDEX "catalog_IX_measurement_units_code" ON grainledger.catalog_measurement_units USING btree (code);

CREATE UNIQUE INDEX "catalog_IX_mrl_limits_mrl_version_id_substance_id" ON grainledger.catalog_mrl_limits USING btree (mrl_version_id, substance_id);

CREATE INDEX "catalog_IX_mrl_limits_substance_id" ON grainledger.catalog_mrl_limits USING btree (substance_id);

CREATE INDEX "catalog_IX_mrl_versions_commodity_id" ON grainledger.catalog_mrl_versions USING btree (commodity_id);

CREATE UNIQUE INDEX "catalog_IX_mrl_versions_market_id_commodity_id_version_no" ON grainledger.catalog_mrl_versions USING btree (market_id, commodity_id, version_no);

CREATE INDEX "catalog_IX_mrl_versions_published_by" ON grainledger.catalog_mrl_versions USING btree (published_by);

CREATE UNIQUE INDEX "catalog_IX_quality_attributes_code" ON grainledger.catalog_quality_attributes USING btree (code);

CREATE UNIQUE INDEX "catalog_IX_rice_varieties_code" ON grainledger.catalog_rice_varieties USING btree (code);

CREATE INDEX "catalog_ix_blend_assessment_current" ON grainledger.catalog_blend_assessment_policies USING btree (company_id, market_id, commodity_id, effective_from DESC, version_no DESC);

CREATE UNIQUE INDEX "catalog_residue_rule_scope_version" ON grainledger.catalog_residue_rule_versions USING btree (company_id, code, version_no) NULLS NOT DISTINCT;

CREATE INDEX "catalog_residue_rule_substance" ON grainledger.catalog_residue_rule_versions USING btree (substance_id, published_at);

CREATE INDEX "catalog_residue_setting_company_snapshot" ON grainledger.catalog_residue_policy_setting_versions USING btree (company_id, published_at);

CREATE UNIQUE INDEX "exports_IX_blend_plan_drafts_company_id_id" ON grainledger.exports_blend_plan_drafts USING btree (company_id, id);

CREATE INDEX "exports_IX_blend_plan_drafts_company_id_order_id" ON grainledger.exports_blend_plan_drafts USING btree (company_id, order_id);

CREATE UNIQUE INDEX "exports_IX_blend_plan_drafts_container_id_version_no" ON grainledger.exports_blend_plan_drafts USING btree (container_id, version_no);

CREATE INDEX "exports_IX_blend_plan_events_plan_id_recorded_at_id" ON grainledger.exports_blend_plan_events USING btree (plan_id, recorded_at, id);

CREATE INDEX "exports_ix_blend_preview_scope" ON grainledger.exports_blend_plan_previews USING btree (company_id, plan_id, expires_at);

CREATE INDEX "exports_ix_export_containers_order" ON grainledger.exports_containers USING btree (order_id, id);

CREATE INDEX "exports_ix_export_events_order" ON grainledger.exports_events USING btree (order_id, recorded_at, id);

CREATE INDEX "exports_ix_export_orders_company_created" ON grainledger.exports_export_orders USING btree (company_id, created_at DESC, id);

CREATE INDEX "identity_IX_audit_events_OccurredAt" ON grainledger.identity_audit_events USING btree ("OccurredAt");

CREATE UNIQUE INDEX "identity_IX_organizations_Kind" ON grainledger.identity_organizations USING btree ("Kind") WHERE (("Kind")::text = 'Mill'::text);

CREATE UNIQUE INDEX "identity_IX_organizations_Name" ON grainledger.identity_organizations USING btree ("Name");

CREATE INDEX "identity_IX_password_reset_codes_UserId_ConsumedAt" ON grainledger.identity_password_reset_codes USING btree ("UserId", "ConsumedAt");

CREATE INDEX "identity_IX_registration_attempts_NormalizedEmail_InvalidatedAt" ON grainledger.identity_registration_attempts USING btree ("NormalizedEmail", "InvalidatedAt");

CREATE INDEX "identity_IX_sessions_FamilyId" ON grainledger.identity_sessions USING btree ("FamilyId");

CREATE UNIQUE INDEX "identity_IX_sessions_RefreshTokenHash" ON grainledger.identity_sessions USING btree ("RefreshTokenHash") WHERE ("RefreshTokenHash" IS NOT NULL);

CREATE INDEX "identity_IX_sessions_UserId_RevokedAt" ON grainledger.identity_sessions USING btree ("UserId", "RevokedAt");

CREATE INDEX "identity_IX_users_Email" ON grainledger.identity_users USING btree ("Email");

CREATE UNIQUE INDEX "identity_IX_users_NormalizedEmail" ON grainledger.identity_users USING btree ("NormalizedEmail") WHERE (("Provider")::text = 'local'::text);

CREATE INDEX "identity_IX_users_OrganizationId" ON grainledger.identity_users USING btree ("OrganizationId");

CREATE UNIQUE INDEX "identity_IX_users_Provider_ProviderSubject" ON grainledger.identity_users USING btree ("Provider", "ProviderSubject");

CREATE INDEX "ledger_contributions_source_lot_lot_idx" ON grainledger.ledger_contributions USING btree (source_lot_id, lot_id);

CREATE INDEX "ledger_custody_allocations_output" ON grainledger.ledger_custody_allocations USING btree (output_lot_id, source_lot_id);

CREATE INDEX "ledger_custody_allocations_source" ON grainledger.ledger_custody_allocations USING btree (source_lot_id, output_lot_id);

CREATE UNIQUE INDEX "ledger_custody_command_idempotency" ON grainledger.ledger_custody_events USING btree (command_id, command_kind, action, location_id, COALESCE(input_lot_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE INDEX "ledger_custody_edges_input" ON grainledger.ledger_custody_edges USING btree (input_lot_id, event_id);

CREATE INDEX "ledger_custody_edges_output" ON grainledger.ledger_custody_edges USING btree (output_lot_id, event_id);

CREATE INDEX "ledger_custody_events_location_revision" ON grainledger.ledger_custody_events USING btree (location_id, revision);

CREATE INDEX "ledger_custody_processing_allocations_input" ON grainledger.ledger_custody_processing_allocations USING btree (input_lot_id, output_lot_id);

CREATE INDEX "ledger_custody_processing_allocations_output" ON grainledger.ledger_custody_processing_allocations USING btree (output_lot_id, source_lot_id);

CREATE INDEX "ledger_events_lot_idx" ON grainledger.ledger_events USING btree (lot_id);

CREATE INDEX "ledger_events_operation_idx" ON grainledger.ledger_events USING btree (operation_id);

CREATE INDEX "ledger_holds_lot_idx" ON grainledger.ledger_holds USING btree (lot_id);

CREATE INDEX "ledger_ix_planning_quality_current" ON grainledger.ledger_planning_quality_observations USING btree (owner_id, lot_id, attribute_id, version_no DESC);

CREATE INDEX "ledger_ix_planning_yield_current" ON grainledger.ledger_planning_yield_rules USING btree (facility_id, compatibility_id, version_no DESC);

CREATE INDEX "ledger_lot_code_reservations_context" ON grainledger.ledger_lot_code_reservations USING btree (context_kind, context_id);

CREATE UNIQUE INDEX "ledger_lots_owner_id_id" ON grainledger.ledger_lots USING btree (owner_id, id);

CREATE INDEX "ledger_mixed_bulk_policies_match" ON grainledger.ledger_mixed_bulk_policies USING btree (location_id, owner_id, commodity_id, stage, product_identity, effective_from DESC);

CREATE INDEX "ledger_postings_event_idx" ON grainledger.ledger_postings USING btree (event_id);

CREATE INDEX "ledger_postings_lot_event_idx" ON grainledger.ledger_postings USING btree (lot_id, event_id);

CREATE INDEX "ledger_receiving_blocks_blocked_lot_idx" ON grainledger.ledger_receiving_blocks USING btree (lot_id) WHERE blocked;

CREATE INDEX "ledger_receiving_threshold_history_company" ON grainledger.ledger_receiving_threshold_history USING btree (company_id, recorded_at DESC, revision DESC);

CREATE INDEX "ledger_receiving_threshold_history_page" ON grainledger.ledger_receiving_threshold_history USING btree (recorded_at DESC, revision DESC);

CREATE INDEX "ledger_residue_evaluations_lot" ON grainledger.ledger_residue_evaluations USING btree (lot_id, persisted_at DESC);

CREATE TRIGGER "capture_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_residue_report_selections FOR EACH ROW EXECUTE FUNCTION grainledger.capture_residue_immutable();

CREATE TRIGGER "capture_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_residue_source_group_versions FOR EACH ROW EXECUTE FUNCTION grainledger.capture_residue_immutable();

CREATE TRIGGER "capture_immutable_truncate" BEFORE TRUNCATE ON grainledger.capture_residue_report_selections FOR EACH STATEMENT EXECUTE FUNCTION grainledger.capture_residue_immutable();

CREATE TRIGGER "capture_immutable_truncate" BEFORE TRUNCATE ON grainledger.capture_residue_source_group_versions FOR EACH STATEMENT EXECUTE FUNCTION grainledger.capture_residue_immutable();

CREATE TRIGGER "capture_lab_association_insert_guard" BEFORE INSERT ON grainledger.capture_lab_confirmed_associations FOR EACH ROW EXECUTE FUNCTION grainledger.capture_guard_lab_association();

CREATE TRIGGER "capture_lab_associations_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_lab_confirmed_associations FOR EACH ROW EXECUTE FUNCTION grainledger.capture_reject_lab_evidence_mutation();

CREATE TRIGGER "capture_lab_confirmation_active_guard" BEFORE INSERT ON grainledger.capture_lab_report_confirmations FOR EACH ROW EXECUTE FUNCTION grainledger.capture_guard_lab_active_report();

CREATE CONSTRAINT TRIGGER "capture_lab_confirmation_complete" AFTER INSERT ON grainledger.capture_lab_report_confirmations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.capture_check_lab_confirmation_complete();

CREATE TRIGGER "capture_lab_confirmation_transaction_stamp" BEFORE INSERT ON grainledger.capture_lab_report_confirmations FOR EACH ROW EXECUTE FUNCTION grainledger.capture_stamp_lab_evidence_transaction();

CREATE TRIGGER "capture_lab_confirmations_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_lab_report_confirmations FOR EACH ROW EXECUTE FUNCTION grainledger.capture_reject_lab_evidence_mutation();

CREATE TRIGGER "capture_lab_related_active_guard" BEFORE INSERT ON grainledger.capture_lab_reports FOR EACH ROW WHEN ((new.related_report_id IS NOT NULL)) EXECUTE FUNCTION grainledger.capture_guard_lab_active_report();

CREATE TRIGGER "capture_lab_report_transaction_stamp" BEFORE INSERT ON grainledger.capture_lab_reports FOR EACH ROW EXECUTE FUNCTION grainledger.capture_stamp_lab_evidence_transaction();

CREATE TRIGGER "capture_lab_reports_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_lab_reports FOR EACH ROW EXECUTE FUNCTION grainledger.capture_reject_lab_evidence_mutation();

CREATE TRIGGER "capture_lab_result_insert_guard" BEFORE INSERT ON grainledger.capture_lab_results FOR EACH ROW EXECUTE FUNCTION grainledger.capture_guard_lab_original_insert();

CREATE TRIGGER "capture_lab_result_sets_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_lab_result_sets FOR EACH ROW EXECUTE FUNCTION grainledger.capture_reject_lab_evidence_mutation();

CREATE TRIGGER "capture_lab_results_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_lab_results FOR EACH ROW EXECUTE FUNCTION grainledger.capture_reject_lab_evidence_mutation();

CREATE TRIGGER "capture_lab_retirement_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_lab_report_retirements FOR EACH ROW EXECUTE FUNCTION grainledger.capture_reject_lab_evidence_mutation();

CREATE TRIGGER "capture_lab_retirement_insert_guard" BEFORE INSERT ON grainledger.capture_lab_report_retirements FOR EACH ROW EXECUTE FUNCTION grainledger.capture_guard_lab_retirement();

CREATE TRIGGER "capture_lab_set_insert_guard" BEFORE INSERT ON grainledger.capture_lab_result_sets FOR EACH ROW EXECUTE FUNCTION grainledger.capture_guard_lab_original_insert();

CREATE TRIGGER "capture_procurement_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_business_events FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_immutable();

CREATE TRIGGER "capture_procurement_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_offline_operations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_immutable();

CREATE TRIGGER "capture_procurement_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_procurement_items FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_immutable();

CREATE TRIGGER "capture_procurement_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_procurements FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_immutable();

CREATE TRIGGER "capture_procurement_immutable" BEFORE DELETE OR UPDATE ON grainledger.capture_source_lots FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_immutable();

CREATE CONSTRAINT TRIGGER "capture_procurement_package" AFTER INSERT OR UPDATE ON grainledger.capture_business_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_package();

CREATE CONSTRAINT TRIGGER "capture_procurement_package" AFTER INSERT OR UPDATE ON grainledger.capture_offline_operations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_package();

CREATE CONSTRAINT TRIGGER "capture_procurement_package" AFTER INSERT OR UPDATE ON grainledger.capture_procurement_items DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_package();

CREATE CONSTRAINT TRIGGER "capture_procurement_package" AFTER INSERT OR UPDATE ON grainledger.capture_procurements DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_package();

CREATE CONSTRAINT TRIGGER "capture_procurement_package" AFTER INSERT OR UPDATE ON grainledger.capture_source_lots DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_capture_procurement_package();

CREATE TRIGGER "catalog_immutable" BEFORE DELETE OR UPDATE ON grainledger.catalog_residue_policy_setting_versions FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_residue_immutable();

CREATE TRIGGER "catalog_immutable" BEFORE DELETE OR UPDATE ON grainledger.catalog_residue_rule_versions FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_residue_immutable();

CREATE TRIGGER "catalog_immutable_blend_assessment_policy" BEFORE DELETE OR UPDATE ON grainledger.catalog_blend_assessment_policies FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_reject_blend_assessment_change();

CREATE TRIGGER "catalog_immutable_truncate" BEFORE TRUNCATE ON grainledger.catalog_residue_policy_setting_versions FOR EACH STATEMENT EXECUTE FUNCTION grainledger.catalog_residue_immutable();

CREATE TRIGGER "catalog_immutable_truncate" BEFORE TRUNCATE ON grainledger.catalog_residue_rule_versions FOR EACH STATEMENT EXECUTE FUNCTION grainledger.catalog_residue_immutable();

CREATE TRIGGER "catalog_trg_catalog_events_append_only" BEFORE DELETE OR UPDATE ON grainledger.catalog_catalog_events FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_reject_catalog_event_mutation();

CREATE TRIGGER "exports_immutable_blend_preview" BEFORE DELETE OR UPDATE ON grainledger.exports_blend_plan_previews FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_immutable_blend_proposal" BEFORE DELETE OR UPDATE ON grainledger.exports_blend_plan_proposals FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_immutable_events" BEFORE DELETE OR UPDATE ON grainledger.exports_events FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_immutable_limits" BEFORE DELETE OR UPDATE ON grainledger.exports_specification_limits FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_immutable_operations" BEFORE DELETE OR UPDATE ON grainledger.exports_idempotent_operations FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_immutable_plan_events" BEFORE DELETE OR UPDATE ON grainledger.exports_blend_plan_events FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_immutable_specifications" BEFORE DELETE OR UPDATE ON grainledger.exports_order_specifications FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

CREATE TRIGGER "exports_protect_plan_draft" BEFORE INSERT OR DELETE OR UPDATE ON grainledger.exports_blend_plan_drafts FOR EACH ROW EXECUTE FUNCTION grainledger.exports_protect_plan_draft();

CREATE CONSTRAINT TRIGGER "ledger_arrival_package" AFTER INSERT OR UPDATE ON grainledger.ledger_arrival_measurement_sets DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_arrival_package_guard();

CREATE CONSTRAINT TRIGGER "ledger_arrival_package" AFTER INSERT OR UPDATE ON grainledger.ledger_arrival_measurements DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_arrival_package_guard();

CREATE CONSTRAINT TRIGGER "ledger_arrival_package" AFTER INSERT OR UPDATE ON grainledger.ledger_arrival_operations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_arrival_package_guard();

CREATE CONSTRAINT TRIGGER "ledger_arrival_package" AFTER INSERT OR UPDATE ON grainledger.ledger_arrival_reviews DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_arrival_package_guard();

CREATE CONSTRAINT TRIGGER "ledger_arrival_package" AFTER INSERT OR UPDATE ON grainledger.ledger_receiving DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_arrival_package_guard();

CREATE CONSTRAINT TRIGGER "ledger_custody_allocation_guard" AFTER INSERT ON grainledger.ledger_custody_allocations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_custody_allocation_guard();

CREATE CONSTRAINT TRIGGER "ledger_custody_edge_allocation_guard" AFTER INSERT ON grainledger.ledger_custody_edges DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_custody_allocation_guard();

CREATE CONSTRAINT TRIGGER "ledger_custody_processing_allocation_guard" AFTER INSERT ON grainledger.ledger_custody_processing_allocations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_custody_processing_allocation_guard();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_arrival_measurement_sets FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_arrival_measurements FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_arrival_operations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_arrival_reviews FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_catalog_commodities FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_compatibilities FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_contributions FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_custody_allocations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_custody_edges FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_custody_events FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_custody_processing_allocations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_events FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_evidence FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_lot_code_reservations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_lot_codes FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_lots FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_mixed_bulk_command_results FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_mixed_bulk_policies FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_policies FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_postings FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_receipt_reversals FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_receiving_threshold_history FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable" BEFORE DELETE OR UPDATE ON grainledger.ledger_residue_evaluations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_immutable_planning_quality_observation" BEFORE DELETE OR UPDATE ON grainledger.ledger_planning_quality_observations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_planning_quality_observation();

CREATE TRIGGER "ledger_immutable_planning_yield_rule" BEFORE DELETE OR UPDATE ON grainledger.ledger_planning_yield_rules FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_planning_yield_rule();

CREATE TRIGGER "ledger_immutable_truncate" BEFORE TRUNCATE ON grainledger.ledger_residue_evaluations FOR EACH STATEMENT EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_mixed_bulk_location_guard" BEFORE INSERT OR UPDATE ON grainledger.ledger_locations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_mixed_bulk_location_guard();

CREATE CONSTRAINT TRIGGER "ledger_mixed_bulk_pool_guard" AFTER INSERT OR UPDATE ON grainledger.ledger_mixed_bulk_pools DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_mixed_bulk_pool_deferred_guard();

CREATE TRIGGER "ledger_mixed_bulk_pool_row" AFTER INSERT OR UPDATE OF mixed_bulk ON grainledger.ledger_locations FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_mixed_bulk_pool_row();

CREATE CONSTRAINT TRIGGER "ledger_mixed_bulk_position_guard" AFTER INSERT OR DELETE OR UPDATE ON grainledger.ledger_positions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_mixed_bulk_pool_deferred_guard();

CREATE TRIGGER "ledger_preserve_receipt_restrictions" BEFORE INSERT OR DELETE OR UPDATE ON grainledger.ledger_receipt_correction_blocks FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER "ledger_preserve_receiving_restrictions" BEFORE DELETE OR UPDATE ON grainledger.ledger_receiving_blocks FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE CONSTRAINT TRIGGER "ledger_receipt_correction_package" AFTER INSERT ON grainledger.ledger_receipt_reversals DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_complete_receipt_package();

CREATE CONSTRAINT TRIGGER "ledger_receipt_package" AFTER INSERT ON grainledger.ledger_lots DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_complete_receipt_package();

CREATE CONSTRAINT TRIGGER "ledger_receiving_resolution_package" AFTER INSERT OR UPDATE ON grainledger.ledger_receiving_blocks DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_complete_receiving_resolution();

CREATE TRIGGER "ledger_receiving_threshold_snapshot" BEFORE INSERT ON grainledger.ledger_arrival_measurement_sets FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_receiving_threshold_insert_guard();

ALTER TABLE ONLY grainledger.capture_crop_seasons
    ADD CONSTRAINT "capture_fk_crop_seasons_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_crop_seasons
    ADD CONSTRAINT "capture_fk_crop_seasons_created_by" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_business_events
    ADD CONSTRAINT "capture_fk_events_actor" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_business_events
    ADD CONSTRAINT "capture_fk_events_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_farmers
    ADD CONSTRAINT "capture_fk_farmers_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_farmers
    ADD CONSTRAINT "capture_fk_farmers_created_by" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_lab_confirmed_associations
    ADD CONSTRAINT "capture_fk_lab_associations_confirmation" FOREIGN KEY (company_id, confirmation_id) REFERENCES grainledger.capture_lab_report_confirmations(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_confirmed_associations
    ADD CONSTRAINT "capture_fk_lab_associations_sample" FOREIGN KEY (company_id, sample_id) REFERENCES grainledger.capture_samples(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_confirmed_associations
    ADD CONSTRAINT "capture_fk_lab_associations_set" FOREIGN KEY (company_id, result_set_id) REFERENCES grainledger.capture_lab_result_sets(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_report_confirmations
    ADD CONSTRAINT "capture_fk_lab_confirmations_report" FOREIGN KEY (company_id, report_id) REFERENCES grainledger.capture_lab_reports(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_reports
    ADD CONSTRAINT "capture_fk_lab_reports_related" FOREIGN KEY (company_id, related_report_id) REFERENCES grainledger.capture_lab_reports(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_result_sets
    ADD CONSTRAINT "capture_fk_lab_result_sets_report" FOREIGN KEY (company_id, report_id) REFERENCES grainledger.capture_lab_reports(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_result_sets
    ADD CONSTRAINT "capture_fk_lab_result_sets_sample" FOREIGN KEY (company_id, sample_id) REFERENCES grainledger.capture_samples(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_results
    ADD CONSTRAINT "capture_fk_lab_results_set" FOREIGN KEY (company_id, result_set_id) REFERENCES grainledger.capture_lab_result_sets(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_fk_lab_retirement_replacement" FOREIGN KEY (company_id, replacement_report_id) REFERENCES grainledger.capture_lab_reports(company_id, id);

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_fk_lab_retirement_report" FOREIGN KEY (company_id, report_id) REFERENCES grainledger.capture_lab_reports(company_id, id);

ALTER TABLE ONLY grainledger.capture_offline_operations
    ADD CONSTRAINT "capture_fk_operations_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_offline_operations
    ADD CONSTRAINT "capture_fk_operations_event" FOREIGN KEY (applied_event_id) REFERENCES grainledger.capture_business_events(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_offline_operations
    ADD CONSTRAINT "capture_fk_operations_user" FOREIGN KEY (submitted_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plot_seasons
    ADD CONSTRAINT "capture_fk_plot_seasons_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plot_seasons
    ADD CONSTRAINT "capture_fk_plot_seasons_company_plot" FOREIGN KEY (company_id, plot_id) REFERENCES grainledger.capture_plots(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plot_seasons
    ADD CONSTRAINT "capture_fk_plot_seasons_company_season" FOREIGN KEY (company_id, season_id) REFERENCES grainledger.capture_crop_seasons(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plot_seasons
    ADD CONSTRAINT "capture_fk_plot_seasons_created_by" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plots
    ADD CONSTRAINT "capture_fk_plots_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plots
    ADD CONSTRAINT "capture_fk_plots_company_farmer" FOREIGN KEY (company_id, farmer_id) REFERENCES grainledger.capture_farmers(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_plots
    ADD CONSTRAINT "capture_fk_plots_created_by" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_procurement_items
    ADD CONSTRAINT "capture_fk_procurement_items_company_plot_season" FOREIGN KEY (company_id, plot_season_id) REFERENCES grainledger.capture_plot_seasons(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_procurement_items
    ADD CONSTRAINT "capture_fk_procurement_items_company_procurement" FOREIGN KEY (company_id, procurement_id) REFERENCES grainledger.capture_procurements(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_procurements
    ADD CONSTRAINT "capture_fk_procurements_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_procurements
    ADD CONSTRAINT "capture_fk_procurements_company_farmer" FOREIGN KEY (company_id, farmer_id) REFERENCES grainledger.capture_farmers(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_procurements
    ADD CONSTRAINT "capture_fk_procurements_event" FOREIGN KEY (event_id) REFERENCES grainledger.capture_business_events(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_procurements
    ADD CONSTRAINT "capture_fk_procurements_received_by" FOREIGN KEY (received_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_sample_handovers
    ADD CONSTRAINT "capture_fk_sample_handovers_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_sample_handovers
    ADD CONSTRAINT "capture_fk_sample_handovers_recorded_by" FOREIGN KEY (recorded_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_sample_handovers
    ADD CONSTRAINT "capture_fk_sample_handovers_sample" FOREIGN KEY (company_id, sample_id) REFERENCES grainledger.capture_samples(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_samples
    ADD CONSTRAINT "capture_fk_samples_collected_by" FOREIGN KEY (collected_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_samples
    ADD CONSTRAINT "capture_fk_samples_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_samples
    ADD CONSTRAINT "capture_fk_samples_source_lot" FOREIGN KEY (company_id, source_lot_id) REFERENCES grainledger.capture_source_lots(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_source_lots
    ADD CONSTRAINT "capture_fk_source_lots_company_item" FOREIGN KEY (company_id, procurement_item_id) REFERENCES grainledger.capture_procurement_items(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_spray_declarations
    ADD CONSTRAINT "capture_fk_spray_declarations_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_spray_declarations
    ADD CONSTRAINT "capture_fk_spray_declarations_company_plot_season" FOREIGN KEY (company_id, plot_season_id) REFERENCES grainledger.capture_plot_seasons(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_spray_declarations
    ADD CONSTRAINT "capture_fk_spray_declarations_recorded_by" FOREIGN KEY (recorded_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_spray_items
    ADD CONSTRAINT "capture_fk_spray_items_company" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_spray_items
    ADD CONSTRAINT "capture_fk_spray_items_company_declaration" FOREIGN KEY (company_id, declaration_id) REFERENCES grainledger.capture_spray_declarations(company_id, id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_spray_items
    ADD CONSTRAINT "capture_fk_spray_items_substance" FOREIGN KEY (substance_id) REFERENCES grainledger.catalog_active_substances(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.capture_lab_confirmed_associations
    ADD CONSTRAINT "capture_lab_confirmed_associations_confirmation_id_fkey" FOREIGN KEY (confirmation_id) REFERENCES grainledger.capture_lab_report_confirmations(id);

ALTER TABLE ONLY grainledger.capture_lab_report_confirmations
    ADD CONSTRAINT "capture_lab_report_confirmations_confirmed_by_fkey" FOREIGN KEY (confirmed_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_lab_report_retirements_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_lab_report_retirements_recorded_by_fkey" FOREIGN KEY (recorded_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_lab_report_retirements_replacement_report_id_fkey" FOREIGN KEY (replacement_report_id) REFERENCES grainledger.capture_lab_reports(id);

ALTER TABLE ONLY grainledger.capture_lab_report_retirements
    ADD CONSTRAINT "capture_lab_report_retirements_report_id_fkey" FOREIGN KEY (report_id) REFERENCES grainledger.capture_lab_reports(id);

ALTER TABLE ONLY grainledger.capture_lab_reports
    ADD CONSTRAINT "capture_lab_reports_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.capture_lab_reports
    ADD CONSTRAINT "capture_lab_reports_recorded_by_fkey" FOREIGN KEY (recorded_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.capture_lab_result_sets
    ADD CONSTRAINT "capture_lab_result_sets_report_id_fkey" FOREIGN KEY (report_id) REFERENCES grainledger.capture_lab_reports(id);

ALTER TABLE ONLY grainledger.capture_lab_results
    ADD CONSTRAINT "capture_lab_results_result_set_id_fkey" FOREIGN KEY (result_set_id) REFERENCES grainledger.capture_lab_result_sets(id);

ALTER TABLE ONLY grainledger.capture_lab_results
    ADD CONSTRAINT "capture_lab_results_substance_id_fkey" FOREIGN KEY (substance_id) REFERENCES grainledger.catalog_active_substances(id);

ALTER TABLE ONLY grainledger.capture_residue_report_selections
    ADD CONSTRAINT "capture_residue_report_selections_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.capture_residue_report_selections
    ADD CONSTRAINT "capture_residue_report_selections_published_by_fkey" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.capture_residue_report_selections
    ADD CONSTRAINT "capture_residue_report_selections_source_id_fkey" FOREIGN KEY (source_id) REFERENCES grainledger.capture_source_lots(id);

ALTER TABLE ONLY grainledger.capture_residue_report_selections
    ADD CONSTRAINT "capture_residue_report_selections_substance_id_fkey" FOREIGN KEY (substance_id) REFERENCES grainledger.catalog_active_substances(id);

ALTER TABLE ONLY grainledger.capture_residue_source_group_versions
    ADD CONSTRAINT "capture_residue_source_group_versions_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.capture_residue_source_group_versions
    ADD CONSTRAINT "capture_residue_source_group_versions_published_by_fkey" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.capture_residue_source_group_versions
    ADD CONSTRAINT "capture_residue_source_group_versions_substance_id_fkey" FOREIGN KEY (substance_id) REFERENCES grainledger.catalog_active_substances(id);

ALTER TABLE ONLY grainledger.catalog_mrl_limits
    ADD CONSTRAINT "catalog_FK_mrl_limits_active_substances_substance_id" FOREIGN KEY (substance_id) REFERENCES grainledger.catalog_active_substances(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.catalog_mrl_limits
    ADD CONSTRAINT "catalog_FK_mrl_limits_mrl_versions_mrl_version_id" FOREIGN KEY (mrl_version_id) REFERENCES grainledger.catalog_mrl_versions(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.catalog_mrl_versions
    ADD CONSTRAINT "catalog_FK_mrl_versions_commodities_commodity_id" FOREIGN KEY (commodity_id) REFERENCES grainledger.catalog_commodities(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.catalog_mrl_versions
    ADD CONSTRAINT "catalog_FK_mrl_versions_markets_market_id" FOREIGN KEY (market_id) REFERENCES grainledger.catalog_markets(id) ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_blend_assessment_policies_commodity_id_fkey" FOREIGN KEY (commodity_id) REFERENCES grainledger.catalog_commodities(id);

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_blend_assessment_policies_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_blend_assessment_policies_market_id_fkey" FOREIGN KEY (market_id) REFERENCES grainledger.catalog_markets(id);

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_blend_assessment_policies_published_by_fkey" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.catalog_blend_assessment_policies
    ADD CONSTRAINT "catalog_blend_assessment_policies_supersedes_id_fkey" FOREIGN KEY (supersedes_id) REFERENCES grainledger.catalog_blend_assessment_policies(id);

ALTER TABLE ONLY grainledger.catalog_catalog_events
    ADD CONSTRAINT "catalog_fk_catalog_events_actor" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.catalog_mrl_versions
    ADD CONSTRAINT "catalog_fk_mrl_versions_published_by" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.catalog_residue_policy_setting_versions
    ADD CONSTRAINT "catalog_residue_policy_setting_versions_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.catalog_residue_policy_setting_versions
    ADD CONSTRAINT "catalog_residue_policy_setting_versions_published_by_fkey" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.catalog_residue_rule_versions
    ADD CONSTRAINT "catalog_residue_rule_versions_commodity_id_fkey" FOREIGN KEY (commodity_id) REFERENCES grainledger.catalog_commodities(id);

ALTER TABLE ONLY grainledger.catalog_residue_rule_versions
    ADD CONSTRAINT "catalog_residue_rule_versions_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.catalog_residue_rule_versions
    ADD CONSTRAINT "catalog_residue_rule_versions_published_by_fkey" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.catalog_residue_rule_versions
    ADD CONSTRAINT "catalog_residue_rule_versions_substance_id_fkey" FOREIGN KEY (substance_id) REFERENCES grainledger.catalog_active_substances(id);

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_blend_plan_drafts_cancelled_by_fkey" FOREIGN KEY (cancelled_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_blend_plan_drafts_company_id_container_id_fkey" FOREIGN KEY (company_id, container_id) REFERENCES grainledger.exports_containers(company_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_blend_plan_drafts_company_id_order_id_fkey" FOREIGN KEY (company_id, order_id) REFERENCES grainledger.exports_export_orders(company_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_blend_plan_drafts_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_blend_plan_drafts_order_id_container_id_fkey" FOREIGN KEY (order_id, container_id) REFERENCES grainledger.exports_containers(order_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_drafts
    ADD CONSTRAINT "exports_blend_plan_drafts_order_id_specification_id_fkey" FOREIGN KEY (order_id, specification_id) REFERENCES grainledger.exports_order_specifications(order_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_events
    ADD CONSTRAINT "exports_blend_plan_events_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_blend_plan_events
    ADD CONSTRAINT "exports_blend_plan_events_company_id_plan_id_fkey" FOREIGN KEY (company_id, plan_id) REFERENCES grainledger.exports_blend_plan_drafts(company_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_previews
    ADD CONSTRAINT "exports_blend_plan_previews_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_blend_plan_proposals
    ADD CONSTRAINT "exports_blend_plan_proposals_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_code_counters
    ADD CONSTRAINT "exports_code_counters_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.exports_containers
    ADD CONSTRAINT "exports_containers_company_id_order_id_fkey" FOREIGN KEY (company_id, order_id) REFERENCES grainledger.exports_export_orders(company_id, id);

ALTER TABLE ONLY grainledger.exports_events
    ADD CONSTRAINT "exports_events_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_events
    ADD CONSTRAINT "exports_events_company_id_order_id_fkey" FOREIGN KEY (company_id, order_id) REFERENCES grainledger.exports_export_orders(company_id, id);

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_commodity_id_fkey" FOREIGN KEY (commodity_id) REFERENCES grainledger.catalog_commodities(id);

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_export_orders
    ADD CONSTRAINT "exports_export_orders_market_id_fkey" FOREIGN KEY (market_id) REFERENCES grainledger.catalog_markets(id);

ALTER TABLE ONLY grainledger.exports_blend_plan_previews
    ADD CONSTRAINT "exports_fk_blend_preview_plan" FOREIGN KEY (company_id, plan_id) REFERENCES grainledger.exports_blend_plan_drafts(company_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_proposals
    ADD CONSTRAINT "exports_fk_blend_proposal_plan" FOREIGN KEY (company_id, plan_id) REFERENCES grainledger.exports_blend_plan_drafts(company_id, id);

ALTER TABLE ONLY grainledger.exports_blend_plan_proposals
    ADD CONSTRAINT "exports_fk_blend_proposal_preview" FOREIGN KEY (company_id, preview_id) REFERENCES grainledger.exports_blend_plan_previews(company_id, id);

ALTER TABLE ONLY grainledger.exports_idempotent_operations
    ADD CONSTRAINT "exports_idempotent_operations_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_idempotent_operations
    ADD CONSTRAINT "exports_idempotent_operations_company_id_container_id_fkey" FOREIGN KEY (company_id, container_id) REFERENCES grainledger.exports_containers(company_id, id);

ALTER TABLE ONLY grainledger.exports_idempotent_operations
    ADD CONSTRAINT "exports_idempotent_operations_company_id_order_id_fkey" FOREIGN KEY (company_id, order_id) REFERENCES grainledger.exports_export_orders(company_id, id);

ALTER TABLE ONLY grainledger.exports_order_specifications
    ADD CONSTRAINT "exports_order_specifications_company_id_order_id_fkey" FOREIGN KEY (company_id, order_id) REFERENCES grainledger.exports_export_orders(company_id, id);

ALTER TABLE ONLY grainledger.exports_order_specifications
    ADD CONSTRAINT "exports_order_specifications_recorded_by_fkey" FOREIGN KEY (recorded_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.exports_specification_limits
    ADD CONSTRAINT "exports_specification_limits_attribute_id_fkey" FOREIGN KEY (attribute_id) REFERENCES grainledger.catalog_quality_attributes(id);

ALTER TABLE ONLY grainledger.exports_specification_limits
    ADD CONSTRAINT "exports_specification_limits_specification_id_fkey" FOREIGN KEY (specification_id) REFERENCES grainledger.exports_order_specifications(id);

ALTER TABLE ONLY grainledger.identity_password_reset_codes
    ADD CONSTRAINT "identity_FK_password_reset_codes_users_UserId" FOREIGN KEY ("UserId") REFERENCES grainledger.identity_users("Id") ON DELETE CASCADE;

ALTER TABLE ONLY grainledger.identity_sessions
    ADD CONSTRAINT "identity_FK_sessions_users_UserId" FOREIGN KEY ("UserId") REFERENCES grainledger.identity_users("Id") ON DELETE CASCADE;

ALTER TABLE ONLY grainledger.identity_users
    ADD CONSTRAINT "identity_FK_users_organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES grainledger.identity_organizations("Id") ON DELETE RESTRICT;

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_operator_id_fkey" FOREIGN KEY (operator_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_procurement_id_fkey" FOREIGN KEY (procurement_id) REFERENCES grainledger.ledger_receiving(procurement_id);

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_threshold_company_id_fkey" FOREIGN KEY (threshold_company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_arrival_measurement_sets
    ADD CONSTRAINT "ledger_arrival_measurement_sets_threshold_revision_fkey" FOREIGN KEY (threshold_revision) REFERENCES grainledger.ledger_receiving_threshold_history(revision);

ALTER TABLE ONLY grainledger.ledger_arrival_measurements
    ADD CONSTRAINT "ledger_arrival_measurements_measurement_set_id_fkey" FOREIGN KEY (measurement_set_id) REFERENCES grainledger.ledger_arrival_measurement_sets(id);

ALTER TABLE ONLY grainledger.ledger_arrival_measurements
    ADD CONSTRAINT "ledger_arrival_measurements_procurement_item_id_fkey" FOREIGN KEY (procurement_item_id) REFERENCES grainledger.capture_procurement_items(id);

ALTER TABLE ONLY grainledger.ledger_arrival_operations
    ADD CONSTRAINT "ledger_arrival_operations_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_arrival_operations
    ADD CONSTRAINT "ledger_arrival_operations_procurement_id_fkey" FOREIGN KEY (procurement_id) REFERENCES grainledger.ledger_receiving(procurement_id);

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_manager_id_fkey" FOREIGN KEY (manager_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_procurement_id_fkey" FOREIGN KEY (procurement_id) REFERENCES grainledger.ledger_receiving(procurement_id);

ALTER TABLE ONLY grainledger.ledger_arrival_reviews
    ADD CONSTRAINT "ledger_arrival_reviews_procurement_id_measurement_set_id_fkey" FOREIGN KEY (procurement_id, measurement_set_id) REFERENCES grainledger.ledger_arrival_measurement_sets(procurement_id, id);

ALTER TABLE ONLY grainledger.ledger_assignments
    ADD CONSTRAINT "ledger_assignments_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_assignments
    ADD CONSTRAINT "ledger_assignments_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_assignments
    ADD CONSTRAINT "ledger_assignments_user_id_fkey" FOREIGN KEY (user_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_catalog_commodities
    ADD CONSTRAINT "ledger_catalog_commodities_catalog_commodity_id_fkey" FOREIGN KEY (catalog_commodity_id) REFERENCES grainledger.catalog_commodities(id);

ALTER TABLE ONLY grainledger.ledger_catalog_commodities
    ADD CONSTRAINT "ledger_catalog_commodities_ledger_commodity_id_fkey" FOREIGN KEY (ledger_commodity_id) REFERENCES grainledger.ledger_commodities(id);

ALTER TABLE ONLY grainledger.ledger_compatibilities
    ADD CONSTRAINT "ledger_compatibilities_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_compatibilities
    ADD CONSTRAINT "ledger_compatibilities_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_compatibilities
    ADD CONSTRAINT "ledger_compatibilities_output_commodity_id_fkey" FOREIGN KEY (output_commodity_id) REFERENCES grainledger.ledger_commodities(id);

ALTER TABLE ONLY grainledger.ledger_contributions
    ADD CONSTRAINT "ledger_contributions_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_contributions
    ADD CONSTRAINT "ledger_contributions_source_lot_id_fkey" FOREIGN KEY (source_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_corrections
    ADD CONSTRAINT "ledger_corrections_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_corrections
    ADD CONSTRAINT "ledger_corrections_operation_id_fkey" FOREIGN KEY (operation_id) REFERENCES grainledger.ledger_operations(id);

ALTER TABLE ONLY grainledger.ledger_corrections
    ADD CONSTRAINT "ledger_corrections_replacement_operation_id_fkey" FOREIGN KEY (replacement_operation_id) REFERENCES grainledger.ledger_operations(id);

ALTER TABLE ONLY grainledger.ledger_custody_allocations
    ADD CONSTRAINT "ledger_custody_allocations_event_id_fkey" FOREIGN KEY (event_id) REFERENCES grainledger.ledger_custody_events(event_id);

ALTER TABLE ONLY grainledger.ledger_custody_allocations
    ADD CONSTRAINT "ledger_custody_allocations_input_lot_id_fkey" FOREIGN KEY (input_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_allocations
    ADD CONSTRAINT "ledger_custody_allocations_output_lot_id_fkey" FOREIGN KEY (output_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_allocations
    ADD CONSTRAINT "ledger_custody_allocations_source_lot_id_fkey" FOREIGN KEY (source_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_edges
    ADD CONSTRAINT "ledger_custody_edges_event_id_fkey" FOREIGN KEY (event_id) REFERENCES grainledger.ledger_custody_events(event_id);

ALTER TABLE ONLY grainledger.ledger_custody_edges
    ADD CONSTRAINT "ledger_custody_edges_input_lot_id_fkey" FOREIGN KEY (input_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_edges
    ADD CONSTRAINT "ledger_custody_edges_output_lot_id_fkey" FOREIGN KEY (output_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_event_id_fkey" FOREIGN KEY (event_id) REFERENCES grainledger.ledger_events(id);

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_input_lot_id_fkey" FOREIGN KEY (input_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_location_id_fkey" FOREIGN KEY (location_id) REFERENCES grainledger.ledger_locations(id);

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_output_lot_id_fkey" FOREIGN KEY (output_lot_id) REFERENCES grainledger.ledger_lots(id) DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE ONLY grainledger.ledger_custody_events
    ADD CONSTRAINT "ledger_custody_events_policy_id_fkey" FOREIGN KEY (policy_id) REFERENCES grainledger.ledger_mixed_bulk_policies(id);

ALTER TABLE ONLY grainledger.ledger_custody_processing_allocations
    ADD CONSTRAINT "ledger_custody_processing_allocations_event_id_fkey" FOREIGN KEY (event_id) REFERENCES grainledger.ledger_events(id);

ALTER TABLE ONLY grainledger.ledger_custody_processing_allocations
    ADD CONSTRAINT "ledger_custody_processing_allocations_input_lot_id_fkey" FOREIGN KEY (input_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_processing_allocations
    ADD CONSTRAINT "ledger_custody_processing_allocations_operation_id_fkey" FOREIGN KEY (operation_id) REFERENCES grainledger.ledger_operations(id);

ALTER TABLE ONLY grainledger.ledger_custody_processing_allocations
    ADD CONSTRAINT "ledger_custody_processing_allocations_output_lot_id_fkey" FOREIGN KEY (output_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_custody_processing_allocations
    ADD CONSTRAINT "ledger_custody_processing_allocations_source_lot_id_fkey" FOREIGN KEY (source_lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_equipment
    ADD CONSTRAINT "ledger_equipment_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_equipment
    ADD CONSTRAINT "ledger_equipment_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_events
    ADD CONSTRAINT "ledger_events_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_events
    ADD CONSTRAINT "ledger_events_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_events
    ADD CONSTRAINT "ledger_events_operation_id_fkey" FOREIGN KEY (operation_id) REFERENCES grainledger.ledger_operations(id);

ALTER TABLE ONLY grainledger.ledger_evidence
    ADD CONSTRAINT "ledger_evidence_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_evidence
    ADD CONSTRAINT "ledger_evidence_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_evidence
    ADD CONSTRAINT "ledger_evidence_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_facilities
    ADD CONSTRAINT "ledger_facilities_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_holds
    ADD CONSTRAINT "ledger_holds_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_holds
    ADD CONSTRAINT "ledger_holds_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_locations
    ADD CONSTRAINT "ledger_locations_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_locations
    ADD CONSTRAINT "ledger_locations_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_locations
    ADD CONSTRAINT "ledger_locations_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_lots
    ADD CONSTRAINT "ledger_lot_code_lineage" FOREIGN KEY (code, code_origin_id) REFERENCES grainledger.ledger_lot_codes(code, origin_lot_id);

ALTER TABLE ONLY grainledger.ledger_lot_code_reservations
    ADD CONSTRAINT "ledger_lot_code_reservations_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_lot_codes
    ADD CONSTRAINT "ledger_lot_codes_origin_lot_id_fkey" FOREIGN KEY (origin_lot_id) REFERENCES grainledger.ledger_lots(id) DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE ONLY grainledger.ledger_lots
    ADD CONSTRAINT "ledger_lots_commodity_id_fkey" FOREIGN KEY (commodity_id) REFERENCES grainledger.ledger_commodities(id);

ALTER TABLE ONLY grainledger.ledger_lots
    ADD CONSTRAINT "ledger_lots_operation_id_fkey" FOREIGN KEY (operation_id) REFERENCES grainledger.ledger_operations(id);

ALTER TABLE ONLY grainledger.ledger_lots
    ADD CONSTRAINT "ledger_lots_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_command_results
    ADD CONSTRAINT "ledger_mixed_bulk_command_results_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_policies
    ADD CONSTRAINT "ledger_mixed_bulk_policies_approved_by_fkey" FOREIGN KEY (approved_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_policies
    ADD CONSTRAINT "ledger_mixed_bulk_policies_commodity_id_fkey" FOREIGN KEY (commodity_id) REFERENCES grainledger.ledger_commodities(id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_policies
    ADD CONSTRAINT "ledger_mixed_bulk_policies_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_policies
    ADD CONSTRAINT "ledger_mixed_bulk_policies_location_id_fkey" FOREIGN KEY (location_id) REFERENCES grainledger.ledger_locations(id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_policies
    ADD CONSTRAINT "ledger_mixed_bulk_policies_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_pools
    ADD CONSTRAINT "ledger_mixed_bulk_pools_commodity_id_fkey" FOREIGN KEY (commodity_id) REFERENCES grainledger.ledger_commodities(id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_pools
    ADD CONSTRAINT "ledger_mixed_bulk_pools_location_id_fkey" FOREIGN KEY (location_id) REFERENCES grainledger.ledger_locations(id);

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_pools
    ADD CONSTRAINT "ledger_mixed_bulk_pools_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id) DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE ONLY grainledger.ledger_mixed_bulk_pools
    ADD CONSTRAINT "ledger_mixed_bulk_pools_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_notifications
    ADD CONSTRAINT "ledger_notifications_hold_id_fkey" FOREIGN KEY (hold_id) REFERENCES grainledger.ledger_holds(id);

ALTER TABLE ONLY grainledger.ledger_notifications
    ADD CONSTRAINT "ledger_notifications_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_notifications
    ADD CONSTRAINT "ledger_notifications_user_id_fkey" FOREIGN KEY (user_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_confirmation_evidence_id_fkey" FOREIGN KEY (confirmation_evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_confirmed_by_fkey" FOREIGN KEY (confirmed_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_created_by_fkey" FOREIGN KEY (created_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_equipment_id_fkey" FOREIGN KEY (equipment_id) REFERENCES grainledger.ledger_equipment(id);

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_operations
    ADD CONSTRAINT "ledger_operations_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_attribute_id_fkey" FOREIGN KEY (attribute_id) REFERENCES grainledger.catalog_quality_attributes(id);

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_confirmed_by_fkey" FOREIGN KEY (confirmed_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_planning_quality_observations
    ADD CONSTRAINT "ledger_planning_quality_observations_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_planning_yield_rules
    ADD CONSTRAINT "ledger_planning_yield_rules_compatibility_id_fkey" FOREIGN KEY (compatibility_id) REFERENCES grainledger.ledger_compatibilities(id);

ALTER TABLE ONLY grainledger.ledger_planning_yield_rules
    ADD CONSTRAINT "ledger_planning_yield_rules_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_planning_yield_rules
    ADD CONSTRAINT "ledger_planning_yield_rules_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_planning_yield_rules
    ADD CONSTRAINT "ledger_planning_yield_rules_published_by_fkey" FOREIGN KEY (published_by) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_policies
    ADD CONSTRAINT "ledger_policies_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_policies
    ADD CONSTRAINT "ledger_policies_facility_id_fkey" FOREIGN KEY (facility_id) REFERENCES grainledger.ledger_facilities(id);

ALTER TABLE ONLY grainledger.ledger_positions
    ADD CONSTRAINT "ledger_positions_location_id_fkey" FOREIGN KEY (location_id) REFERENCES grainledger.ledger_locations(id);

ALTER TABLE ONLY grainledger.ledger_positions
    ADD CONSTRAINT "ledger_positions_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_postings
    ADD CONSTRAINT "ledger_postings_event_id_fkey" FOREIGN KEY (event_id) REFERENCES grainledger.ledger_events(id);

ALTER TABLE ONLY grainledger.ledger_postings
    ADD CONSTRAINT "ledger_postings_location_id_fkey" FOREIGN KEY (location_id) REFERENCES grainledger.ledger_locations(id);

ALTER TABLE ONLY grainledger.ledger_postings
    ADD CONSTRAINT "ledger_postings_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_postings
    ADD CONSTRAINT "ledger_postings_reverses_posting_id_fkey" FOREIGN KEY (reverses_posting_id) REFERENCES grainledger.ledger_postings(id);

ALTER TABLE ONLY grainledger.ledger_receipt_correction_blocks
    ADD CONSTRAINT "ledger_receipt_correction_blocks_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_receipt_reversals
    ADD CONSTRAINT "ledger_receipt_reversals_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_receipt_reversals
    ADD CONSTRAINT "ledger_receipt_reversals_owner_id_fkey" FOREIGN KEY (owner_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_receiving_blocks
    ADD CONSTRAINT "ledger_receiving_blocks_evidence_id_fkey" FOREIGN KEY (evidence_id) REFERENCES grainledger.ledger_evidence(id);

ALTER TABLE ONLY grainledger.ledger_receiving_blocks
    ADD CONSTRAINT "ledger_receiving_blocks_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);

ALTER TABLE ONLY grainledger.ledger_receiving
    ADD CONSTRAINT "ledger_receiving_procurement_id_accepted_measureme_36a9c1ef5f44" FOREIGN KEY (procurement_id, accepted_measurement_set_id) REFERENCES grainledger.ledger_arrival_measurement_sets(procurement_id, id) DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE ONLY grainledger.ledger_receiving
    ADD CONSTRAINT "ledger_receiving_procurement_id_accepted_review_id_fkey" FOREIGN KEY (procurement_id, accepted_review_id) REFERENCES grainledger.ledger_arrival_reviews(procurement_id, id) DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE ONLY grainledger.ledger_receiving
    ADD CONSTRAINT "ledger_receiving_procurement_id_fkey" FOREIGN KEY (procurement_id) REFERENCES grainledger.capture_procurements(id);

ALTER TABLE ONLY grainledger.ledger_receiving
    ADD CONSTRAINT "ledger_receiving_procurement_id_latest_measurement_set_id_fkey" FOREIGN KEY (procurement_id, latest_measurement_set_id) REFERENCES grainledger.ledger_arrival_measurement_sets(procurement_id, id) DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_default
    ADD CONSTRAINT "ledger_receiving_threshold_default_revision_fkey" FOREIGN KEY (revision) REFERENCES grainledger.ledger_receiving_threshold_history(revision);

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_history
    ADD CONSTRAINT "ledger_receiving_threshold_history_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_history
    ADD CONSTRAINT "ledger_receiving_threshold_history_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_overrides
    ADD CONSTRAINT "ledger_receiving_threshold_overrides_company_id_fkey" FOREIGN KEY (company_id) REFERENCES grainledger.identity_organizations("Id");

ALTER TABLE ONLY grainledger.ledger_receiving_threshold_overrides
    ADD CONSTRAINT "ledger_receiving_threshold_overrides_revision_fkey" FOREIGN KEY (revision) REFERENCES grainledger.ledger_receiving_threshold_history(revision);

ALTER TABLE ONLY grainledger.ledger_residue_evaluations
    ADD CONSTRAINT "ledger_residue_evaluations_actor_id_fkey" FOREIGN KEY (actor_id) REFERENCES grainledger.identity_users("Id");

ALTER TABLE ONLY grainledger.ledger_residue_evaluations
    ADD CONSTRAINT "ledger_residue_evaluations_lot_id_fkey" FOREIGN KEY (lot_id) REFERENCES grainledger.ledger_lots(id);
