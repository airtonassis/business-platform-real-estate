---
name: araia-postgres-validation
description: Plan or execute an explicitly authorized PostgreSQL validation for an AraiA product using an isolated disposable database, with evidence, least privilege, and safe cleanup. Do not use for operational databases, integrated Core–product homologation, or publication.
metadata:
  short-description: Validate AraiA PostgreSQL safely
---

# AraiA PostgreSQL validation

Use this skill when preparing or carrying out a PostgreSQL persistence gate for an AraiA product. A request to inspect or plan does not authorize execution. Before creating resources, applying migrations, or running destructive/concurrent checks, require explicit authorization for that operation and respect its stated scope.

## Workflow

1. Read the product repository instructions and the current migration/test contracts relevant to the requested gate. Confirm the repository and migration source; never copy migrations between products or infer that local files are published.
2. Classify the task as read-only preparation, local disposable validation, operational homologation, or publication. Keep these states separate in results and receipts.
3. For execution, verify all preconditions before creating resources: locally available supported PostgreSQL image, exclusively local daemon/endpoint, sufficient resources, no operational connection configuration in use, and a runner that can prove resource ownership and cleanup. Stop if any check is ambiguous.
4. Use only an isolated disposable database. Keep migration credentials separate from application/runtime credentials. Do not broaden grants, weaken RLS/audit, or repair product permissions merely to make a gate pass.
5. Apply the authoritative migration chain in order, stopping at the first error. Then inspect effective privileges, RLS/FORCE RLS, tenant and organizational constraints, and real authentication as the runtime role. Distinguish structural diagnostics from functional scenarios.
6. Run only the scenarios authorized and still needed. Reuse prior evidence only when baseline, code fingerprint, environment, and scope still match. Record missing or skipped coverage as `NOT RUN`/`SKIP`; these are not passes.
7. Write a sanitized receipt with per-phase/per-scenario status, migration identity, failure category, relevant non-secret environment facts, and cleanup result. Never record credentials or connection strings containing secrets.
8. Clean only resources created by this run, matching both immutable identity and ownership marker. Verify cleanup. Report cleanup failure separately from the primary failure and stop; do not retry automatically.

## Stop conditions

Stop before or during execution if endpoint locality, image identity, resource limits, database disposability, ownership, credentials, migration ordering, or cleanup safety cannot be established. Stop on the first migration failure. Do not make automatic grant, schema, migration, or infrastructure corrections. A retry requires a new explicit authorization when the previous request limited attempts.

## References

- General acceptance and evidence rules: [general-validation.md](references/general-validation.md)
- ERP Imobiliário scope: [erp-imobiliario.md](references/erp-imobiliario.md)
- Docker/Windows environment notes: [docker-windows.md](references/docker-windows.md)
- Sprint 05 lessons: [sprint-05-lessons.md](references/sprint-05-lessons.md)
