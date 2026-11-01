-- Read only the effective compatibility sets needed for owner-scoped export
-- planning. Processing still checks the exact input set at commit time.
CREATE FUNCTION ledger.planning_compatibilities(a uuid, o uuid, f uuid) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF ledger.role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT ledger.own(a,o) THEN
        RAISE EXCEPTION 'Candidate access denied' USING ERRCODE='42501';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=f) THEN
        RAISE EXCEPTION 'Unknown facility' USING ERRCODE='22023';
    END IF;
    RETURN COALESCE((
        SELECT jsonb_agg(jsonb_build_object(
            'id',c.id,
            'inputProducts',(SELECT jsonb_agg(product ORDER BY product)
                FROM unnest(c.input_products) AS product),
            'outputCatalogCommodityId',mapping.catalog_commodity_id,
            'effectiveFrom',c.effective_from
        ) ORDER BY c.id)
        FROM ledger.compatibilities c
        LEFT JOIN ledger.catalog_commodities mapping
            ON mapping.ledger_commodity_id=c.output_commodity_id
        WHERE c.facility_id=f AND c.effective_from<=statement_timestamp()
    ),'[]'::jsonb);
END $$;

REVOKE ALL ON FUNCTION ledger.planning_compatibilities(uuid,uuid,uuid) FROM PUBLIC;
