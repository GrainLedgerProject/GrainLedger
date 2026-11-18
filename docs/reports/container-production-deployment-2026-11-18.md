# Container S1 — migration and runtime grant, 18/11/2026

Applied migration `20261117000000_ContainerProduction` to the configured shared Supabase database after the user's explicit instruction: “Rồi chay migration và thêm quyền đi”. Granted EXECUTE on the single new entry `grainledger.ledger_container_production(uuid,uuid,text,jsonb,jsonb)` to the configured restricted runtime role. This approval covered this deployment; it is not blanket approval for future database changes.

## Applied scope

- Preflight confirmed that owner and runtime connections address the same database/schema, the runtime role has no elevated role attributes, the latest installed migration was `20261114000000_ResidueProfileRetirement`, and the production entry/tables were absent.
- Backed up the `grainledger` schema and its data as a PostgreSQL custom-format archive before changing it. Archive listing validation passed: 1,259,182 bytes, 1,223 entries. This is an application-schema backup, not a full Supabase project backup; restore was not rehearsed.
- Generated the EF forward script for only the new migration. Applied schema/functions/triggers, migration history and the single new grant in one transaction, holding the existing stock advisory lock `714928352` before migration writes. Transaction-local lock timeout was 10 seconds; statement timeout was 120 seconds.
- Did not invoke application `--migrate`, because that path also rebuilds the complete runtime grant set. Applied the reviewed script directly through the existing migration-owner connection; ordinary application startup retained runtime credentials.
- Preserved `.env`, connection settings and all pre-existing function/table owners and ACLs. No seeds, fixture resets or business-data smoke writes were run.

The migration changes reservation event semantics and protected container routines as described in the [deployment checklist](../guides/container-production-deployment.md). It does not convert existing containers into production tasks. Compatible builds are required for writers; this run verified the local matching API, not an inventory or rollout of remote API instances.

## Checks actually run

| Check | Result |
| --- | --- |
| Migration history | Previous history unchanged, with only `20261117000000_ContainerProduction` appended. |
| Existing function/table ownership and grants | Before/after metadata comparison passed for every pre-existing object. |
| New production entry | Runtime has EXECUTE. |
| Private production helper and four private tables | Runtime has no helper EXECUTE or direct SELECT/INSERT/UPDATE/DELETE on these tables. |
| Normal backend startup | PASS with the unchanged restricted runtime connection on `http://localhost:5080`. |
| `GET /health` | HTTP 200, `{"status":"ok"}`. |
| Anonymous `GET /api/container-production` | HTTP 401: route is mapped and requires authentication. This does not establish an authenticated production flow on shared data. |
| Scoped function reads using runtime credentials, in READ ONLY transaction | New production detail returned `NOT_REQUESTED` for the existing own-company DIRECT_STOCK fixture `CTN-20261117-000004`; existing execution detail remained `CLOSED`, loaded `10.000000` kg. |
| Four new production tables | All empty after deployment. No business records were created. |

No backend test suite was rerun for deployment. The [implementation record](container-production-implementation-2026-11-18.md) contains the prior focused container tests and actual browser flow on isolated PostgreSQL. Shared-database verification here was health/auth-boundary and read-only database verification; it was not another data-changing browser test.

## Local deployment artifacts and recovery boundary

Protected artifacts are outside Git at `/home/phucpinku/.local/share/grainledger-backups/container-production-20261118/`: application-schema backup, checksum, before/after owner/ACL snapshots, reviewed script, application output and read-only verification results. Directory permissions are 0700; backup and metadata snapshots are 0600. The archive contains application data and must not be published.

Applied script SHA-256: `65993925afd342290d6c82f822059c7d9a919c8659c25e4540b3e6151f132749`.

No restore, down migration, commit, push or remote deployment was performed in this deployment run. Keep the backup and coordinate a reviewed forward fix or recovery if needed; reverting code alone is unsafe after new reservation events or production records exist. The subsequently updated [core plan](../plans/container-core-completion-plan-2026-11-17.md) defines the remaining requirement-backed S2 scope and excludes extensions; it does not authorize new database changes.
