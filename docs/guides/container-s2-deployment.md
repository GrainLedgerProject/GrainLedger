# Container S2 deployment package

Status on 18/11/2026: subsequently applied to the configured shared Supabase after the user's separate explicit S2 migration/grant approval; see [deployment record](../reports/container-s2-deployment-2026-11-18.md). Initial implementation verification used disposable PostgreSQL only. This completed deployment does not authorize changes to other environments or future migrations. No shared seed, fixture copy/reset or business mutation smoke was run.

## Package and compatibility

Apply forward migration [20261118000000_ContainerWorkflow](../../backend/src/GrainLedger.Api/Data/Migrations/20261118000000_ContainerWorkflow.cs) and [embedded SQL](../../backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql) with the matching API/web build. It adds private batch/claim/review/handoff/document/supplement/notice records, imports existing S1 production markers/reviews into retained extensions, and replaces affected protected functions. It does not reset stock, release HOLD, relabel old loads, or edit closing dossiers.

Existing installed EXECUTE grants remain attached to replaced signatures. The only new public runtime entry is `grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb)`. Private extension tables/helpers must remain inaccessible to runtime. Normal application startup never repairs grants or migrates.

Old writers do not understand scoped review versions, partial batches, handoff authority and whole-manifest CLOSE. Coordinate all API writers and matching web assets; do not mix S1 writers with this schema.

## Procedure after explicit approval

1. Confirm the intended shared database, installed migrations and existing runtime role; retain a protected backup and ACL inventory. Preserve `.env` and connection settings. Review [verification and remaining limits](../reports/container-s2-implementation-2026-11-18.md).
2. Obtain explicit approval for migration `20261118000000_ContainerWorkflow` and the single new EXECUTE grant. Pause affected writes during the compatible-instance cutover.
3. Run the approved migration-owner job in [setup](../setup-and-deployment.md#local-migration-and-seeds), targeting this migration with no unrelated pending migration or seed. Do not run the broader `--configure-runtime-role` command merely to add this entry; it rebuilds the finite grant set and needs a separate scope review.
4. As migration owner, grant EXECUTE on **only** the new workflow signature to the already configured runtime role. Resolve/quote that role from existing protected configuration; never invent a role or publish connection strings. Existing helper/table ACLs must remain denied. No SQL with a guessed production role is supplied here.
5. Validate runtime-role startup, installed migration history, function privileges and scoped read endpoints with the matching build. Compare the previous ACL inventory. Use isolated fixtures for write smoke checks; shared smoke writes require their own explicit fixture authorization.

Read-only preflight, using the intended audit context:

```sql
BEGIN READ ONLY;
SELECT "MigrationId" FROM grainledger."__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT to_regprocedure('grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb)') AS workflow_entry,
       to_regclass('grainledger.ledger_container_scope_reviews') AS reviews;
ROLLBACK;
```

Forward recovery only: this migration deliberately has no destructive Down implementation. Restore/recovery requires the environment's reviewed procedure; a code rollback alone cannot reverse new immutable review/reservation/production history. Never start normal application requests with migration-owner credentials to bypass validation.
