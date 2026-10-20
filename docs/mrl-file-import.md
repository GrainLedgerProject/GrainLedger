# MRL file import

The Admin **MRL tables → Import CSV / Excel** action reads `.csv` and `.xlsx`
files in the browser. The file is not uploaded as an attachment. The web client
shows a preview and sends one selected, normalized group to
`POST /api/mrl-versions/import`. The endpoint creates one DRAFT MRL version and
all of its numeric limits in one database transaction. It never publishes.

## Bundled Vietnam reference import

`tools/import_catalog_reference.py` imports only the bundled snapshots in
`tools/catalog-reference-data/`. They are attachments to YouTrack GL-A-4
(substances) and GL-A-3 (MRL table), both version 1.1, revised 2026-09-13:

- [GL-A-4](https://huubng.youtrack.cloud/articles/GL-A-4):
  `pesticide_substances-v1.1.csv`, 14 substances.
- [GL-A-3](https://huubng.youtrack.cloud/articles/GL-A-3):
  `mrl_table-v1.1.csv`, containing 96 rows; Vietnam subset has 5 ACTIVE
  numeric limits and 9 NO_MRL rows.

The script reads snapshot paths relative to itself. Its generated `SourceRef`
contains both article links/version, `Thông tư 50/2016/TT-BYT`,
`VN_TT50_2016`, effective date `2017-07-01`, and SHA-256 hashes of raw CSV
bytes. Snapshot hashes: substances
`8c1a18aa7bec6e751a04b0b38264d61947c45ff45d51bf1ed8cedce79785ce1a`;
MRL table `d43364841232fae50b67697709d0b794f33a87fdb95240f839fbfa6f22735e08`.
Snapshots stay unchanged and retain all markets; apply selects only Vietnam.
This is historical source provenance, not current legal verification or a
compliance conclusion.

Preview needs only Python standard library and is offline:

```sh
python3 tools/import_catalog_reference.py
```

Run local importer regression checks with:

```sh
python3 -B -m unittest discover -s tools -p test_import_catalog_reference.py
```

Apply requires active Administrator credentials authorized for
`platform.configure`, HTTPS API base URL (HTTP only for loopback), and
`GRAINLEDGER_IMPORT_TOKEN` in environment, never in arguments. Set both
commodity mappings explicitly; they must be different existing, active codes
and neither may be PADDY:

```sh
export GRAINLEDGER_IMPORT_TOKEN='…'
python3 tools/import_catalog_reference.py --apply \
  --base-url https://api.example.invalid \
  --market-code VN \
  --rice-commodity-code RICE \
  --husked-rice-commodity-code HUSKED-RICE
```

Use actual configured commodity codes. Importer resolves exact market,
commodity, unit and substance records; it does not create or remap commodities.
Commodity API response must include `isArchived: false`; archived commodities
or older API responses missing this field fail preflight before writes. Restart
API after deploying `CommodityResponse.isArchived`. Vietnam market `VN` and
`MG/KG` residue-concentration unit must already exist. Missing market/unit
prerequisites stop preflight before any writes.
Substances are created by code and English source name only when both absent.
Existing code/name conflict fails closed; existing entries are never renamed.
CAS and processing factors remain only in source snapshot because API has no
fields for them.

Vietnam rows group into exactly two drafts: generic rice (3 limits: CPF006
0.5, LCH010 1, TBZ014 1.5 mg/kg) and husked rice (2 limits: CBD005 2,
ABA008 0.1 mg/kg). There is no paddy import. Nine NO_MRL rows are printed
with source notes and retained in CSV; they mean this snapshot establishes no
numeric limit. This is insufficient evidence to determine legal applicability,
not zero or unlimited/permissive status. Other-market rows are not imported.

The importer preflights local snapshots and remote conflicts before first
write. Exact matching source DRAFT is reused on rerun; mismatched or ambiguous
source drafts stop rather than overwrite or duplicate. POST requests are not
retried automatically. If a later request fails, output reports already
created/reused resource IDs; inspect API state with GET before rerunning.
This is not an atomic operation across substance creates and two draft
requests. The endpoint transaction applies to each individual MRL draft only.

The API accepts only substance code, not CAS number. Numeric limits use
`mg/kg`; API stores decimal numeric values. Publication is a separate action.
The API rejects historical effective start earlier than publication time, so
these drafts dated 2017-07-01 remain unpublished unless publication policy
changes through an authorized decision. Do not alter legal dates to bypass
that gate.

## Browser CSV / Excel import

The browser importer recognizes `substance_id`, `substance_name`,
`commodity_code`, `commodity_name_en`, `market`, `regulation_reference`,
`mrl_value_mg_kg`, `mrl_unit`, `effective_date`, `version`, and `status`.
An optional `effective_to` column may be added. Dates use `YYYY-MM-DD`. The
selected group is defined by market, commodity, version, and effective dates.
One file can contain several groups; importing one does not import others.

Only `ACTIVE` rows with a numeric nonnegative `mg/kg` limit are eligible.
`NO_MRL`, `DEFAULT_APPLIES`, and `ACTIVE_TIME_LIMITED` rows are listed as
excluded in preview. They are not converted to zero limits. Duplicate
substance codes or invalid eligible rows block selected group. Each eligible
substance code must already exist as active-substance catalog code; the
separate substance snapshot is reference for setting up those entries.
`MG/KG` residue-concentration unit must also exist.

Administrator maps file group to existing market and commodity, verifies
regulatory source, and selects effective interval. File dates have no
timezone, so **Effective from** must always be entered and verified manually.
**Effective to** may be blank, stored as `null` (open-ended). Historical
effective start may be saved as DRAFT, but publication rule rejects it when
start precedes publication time. Do not change historical legal date without
policy decision.

Current MRL schema does not represent per-limit LOD flag, NO_MRL, default
applicability, time-limited status, notes, regulation URL, CAS, or processing
factors. Those remain source evidence and are not translated into limits.
Review source and all required substances before publishing. Importing DRAFT
is not a compliance decision.
