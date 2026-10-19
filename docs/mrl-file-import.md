# MRL file import

The Admin **MRL tables → Import CSV / Excel** action reads `.csv` and `.xlsx`
files in the browser. The file is not uploaded as an attachment. The web client
shows a preview and sends one selected, normalized group to
`POST /api/mrl-versions/import`. The endpoint creates one draft MRL version and
all of its limits in one database transaction. It never publishes the version.

The importer recognizes the supplied `mrl_table-v1.1` columns:
`substance_id`, `substance_name`, `commodity_code`, `commodity_name_en`,
`market`, `regulation_reference`, `mrl_value_mg_kg`, `mrl_unit`,
`effective_date`, `version`, and `status`. An optional `effective_to` column
may be added. Dates, when present, use `YYYY-MM-DD`. The selected group is
defined by file market, commodity, version and effective dates. One file can
contain several groups; importing one does not import the others.

Only `ACTIVE` rows with a numeric nonnegative `mg/kg` limit are eligible.
`NO_MRL`, `DEFAULT_APPLIES` and `ACTIVE_TIME_LIMITED` rows are listed as
excluded in the preview. They are not converted to zero limits. Duplicate
substance codes or invalid eligible rows block the selected group. Every
eligible `substance_id` must already exist as an active-substance catalog code;
the separate `pesticide_substances-v1.1` file is a reference for setting up
those catalog entries and is not imported automatically. The `MG/KG`
residue-concentration unit must also exist.

The administrator maps the file group to an existing market and commodity,
verifies the regulatory source, and selects the effective interval. File dates
have no timezone, so **Effective from** must always be entered and verified
manually. The preview says when the file has no `effective_date`.
**Effective to** may be blank, which is stored as `null` (open-ended). A file
date in the past can be saved in a draft, but the existing publication rule
will reject publication when its effective start precedes publication time.
Do not change a historical legal date just to pass that rule without a policy
decision.

The current MRL schema does not represent a per-limit LOD flag, `NO_MRL`,
default applicability, time-limited status, note, or regulation URL. Those
fields are not silently translated into limits. Also, a file group may be
only one part of a complete legal MRL table. Review the source and all
required substances before publishing. Importing a draft is not a compliance
decision.
