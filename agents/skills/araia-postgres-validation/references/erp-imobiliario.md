# ERP Imobiliário profile

This file captures product-specific context, not a universal AraiA schema. Confirm current contracts and repository contents before every gate; migration numbers and test classes evolve.

## Sprint 05 persistence scope

The local Sprint 05 gate was designed around migrations 001–029, including migration 023 and negotiation action/transfer migrations 028–029. Treat that range as the Sprint 05 baseline only; derive the current chain from `database/migrations/` and the runner before execution.

Relevant concerns include:

- Commercial Agenda ownership and permissions.
- Negotiation actions with explicit negotiation linkage and a unique Agenda commitment association.
- Assisted transfer, deterministic time-overlap handling, explicit follow-ups, optimistic versions, idempotency, and audit events.
- Organization/branch isolation, composite foreign keys, RLS/FORCE RLS, and effective privileges for `erp_runtime`.

Do not use this profile to infer that PostgreSQL persistence is validated, operational Core authorization is available, or Sprint 05 is homologated. Keep functional in-memory evidence separate from PostgreSQL and Core–ERP evidence.

## Product evidence

The project instructions identify `AGENTS.md`, `DEVELOPMENT_STANDARDS.md` in the Core repository, `POL-DOC-001.md`, relevant `docs/` contracts, migrations, tests, and `scripts/s05_postgres_gate.py` as sources to consult. Preserve each source's status and version. Do not copy or modify Core contracts as part of an ERP database gate without separate authorization.
