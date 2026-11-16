# General AraiA PostgreSQL validation

These rules apply across products; a product's approved contract and current runner remain authoritative for exact checks.

## Preconditions and isolation

- Identify the owning repository, migration chain, test project, and target product before preparing a gate.
- Use a newly created disposable database on an isolated local instance/container. Never point a destructive gate at a shared, operational, or persistent environment.
- Prove daemon/endpoint locality, image identity, network isolation, resource ceilings, and ownership of every temporary resource before use.
- Use no external service or credential unless the approved test specifically requires it and its authorization is explicit. Prefer an offline database-only proof.
- Separate migration/DDL identity from runtime identity. Runtime must authenticate through the same relevant database path used by the application; a privileged fixture or `SET ROLE` alone does not prove login behavior.

## Migration and database checks

- Derive migration order from the repository's authoritative manifest/naming rules, not a copied list.
- Apply one migration at a time and stop on the first failure. Do not continue into diagnostics or functional tests that depend on a failed schema.
- Inspect effective privileges, including inherited/default privileges and ownership effects. Check RLS and FORCE RLS behavior for relevant roles, tenant/org boundaries, foreign keys, indexes, uniqueness, triggers, and immutable audit/event structures as required by the product contract.
- Never grant extra privileges, disable RLS, alter audit protections, or rewrite migrations to make validation pass. Record the precise missing capability as a blocker.
- For failure/rollback checks, use disposable state and record which transaction boundary and actor were actually exercised.

## Evidence and status

- Keep PASS, FAIL, SKIP, NOT RUN, INTERRUPTED, and cleanup result distinct. A partial phase cannot imply full-gate approval.
- Record command, runner/code fingerprint, migration checksums, image identity, database/role facts, timestamps, per-scenario outcome, and sanitized failure category. Omit secrets, passfiles, connection strings, and personal machine paths.
- Reuse evidence only if baseline, environment, and scope are applicable. Do not rerun approved tests without a material change, demonstrated regression, missing required evidence, or a required gate.
- Separate local code/test validation from PostgreSQL persistence validation, operational Core–product homologation, human acceptance, and publication.

## Recovery and cleanup

- Prefer runner-owned temporary paths and unique run IDs. Cleanup must require exact resource identity plus ownership metadata; never enumerate and delete by a broad name/pattern alone.
- Attempt cleanup on normal failure and interruption. Verify that only run-owned resources were removed; report incomplete cleanup separately and do not silently retry.
- Preserve receipts and existing evidence. A cleanup failure is an incident to report, not permission to broaden deletion.
