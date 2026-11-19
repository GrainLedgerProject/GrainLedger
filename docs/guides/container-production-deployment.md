# Container production — deployment checklist

The configured shared Supabase database received migration `20261117000000_ContainerProduction` and the single new runtime EXECUTE grant on 18/11/2026 after separate explicit user approval. See the [deployment record](../reports/container-production-deployment-2026-11-18.md) for backup, scope and actual checks. The checklist below remains applicable to other environments or future deployments; approval for this run does not authorize those changes.

## Review before shared deployment

- [ ] Confirm the intended database/environment, backup/recovery procedure and installed migration history. Preserve `.env` and both runtime/migration connection settings; do not replace the database.
- [ ] Review migration [20261117000000_ContainerProduction](../../backend/src/GrainLedger.Api/Data/Migrations/20261117000000_ContainerProduction.cs) and [its SQL](../../backend/src/GrainLedger.Api/Data/Sql/ContainerProduction.sql): four private tables, production scope/history triggers, protected production entry, reservation RESERVE event semantics, updated reservation calculation and container read/write functions.
- [ ] Coordinate compatible API instances. An older instance may not understand RESERVE events, production markers or output authority. Do not mix old and new writers or restore grants from an older branch.
- [ ] Review new public runtime EXECUTE **only** for `grainledger.ledger_container_production(uuid,uuid,text,jsonb,jsonb)`. Retain existing installed feature grants; do not grant table access or EXECUTE to private helpers.
- [ ] Review the [implementation verification record](../reports/container-production-implementation-2026-11-18.md), isolated tests, API contracts and declared S1 limits.
- [ ] Obtain explicit database-owner/user approval for the migration and runtime grants. AGENTS.md treats Supabase as shared and requires approval for these changes.

Read-only preflight examples (owner/read-only audit context, no credential output):

```sql
BEGIN READ ONLY;
SELECT "MigrationId" FROM grainledger."__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT to_regclass('grainledger.ledger_container_production') AS production_table,
       to_regprocedure('grainledger.ledger_container_production(uuid,uuid,text,jsonb,jsonb)') AS production_entry;
ROLLBACK;
```

Check all installed features against the build, not just the last migration name. A newer/unexpected schema requires reconciliation before applying this package.

## Approved execution procedure

After the approval above, use the migration-owner workflow in [setup and deployment](../setup-and-deployment.md#local-migration-and-seeds) with this compatible build. Migration is forward-only; it does not rewrite committed production, evidence, stock or container history. Do not run seeds as part of deployment.

Use the existing [runtime-role configuration workflow](../setup-and-deployment.md#runtime-grants-after-branch-or-deployment-changes) only from a build containing **all** installed features, or an explicitly reviewed owner grant for the single new entry. That configuration command rebuilds the finite grant set; running it from the wrong branch can break other API instances. Keep normal application startup on the restricted runtime role.

Verify runtime validation, health, new endpoint availability and existing DIRECT_STOCK reads. A new data-changing shared smoke scenario needs separately authorized fixture scope; the previous HOLD/stale/closed fixtures must not be reset or repurposed blindly. Prefer the isolated browser host/test fixtures for verification.

## If a step fails

Do not bypass runtime-role validation or start the app with owner credentials. Keep affected writes stopped until schema/grants/build agree. Do not apply an automatic down migration or reset: the feature retains immutable production/review/accounting history. Use the reviewed recovery/forward-fix procedure and retain audit evidence. Code rollback alone is not a safe database rollback once new reservation events or production records exist.
