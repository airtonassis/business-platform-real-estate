# Sprint 05 lessons learned

These are concrete lessons from the ERP Imobiliário Sprint 05 runner. They are examples to inform review, not extra universal requirements or evidence that the runner has now passed operationally.

- A helper call passed `timeout=120` to `psql()` while the helper rejected the keyword. The helper now accepts an optional timeout, defaults to 90 seconds, and forwards it to the Docker command wrapper. Check both signature and propagation when reviewing future calls.
- Docker's `inspect.Mounts` summary did not reliably expose a `--tmpfs` entry for effective `PGDATA` in this environment. Treat `HostConfig.Tmpfs` as the declared configuration and `/proc/self/mountinfo` as the required runtime proof of the exact target, `tmpfs`, `rw`, `noexec`, `nosuid`, and size. An empty or alternate `Mounts` summary is not proof either way; never accept declaration alone or weaken runtime evidence to pass preflight.
- Test mount parsers against the supported Docker representations before creating resources. Match the exact mountpoint for `PGDATA`, reject missing, duplicate, malformed, or insecure evidence, and record sanitized observed facts in the receipt.
- The phase-A-only path uses internal container execution and does not require a published PostgreSQL port. Its preflight should reject published bindings in that mode.
- A migration failure handler had its re-raise nested in the loop that marked remaining migrations `NOT RUN`; it recorded only the first remaining migration. Keep recording all unattempted migrations before propagating the first failure.
- Earlier attempts stopped before migration 001 due to preflight blockers. They do not count as migration or PostgreSQL persistence evidence. No later attempt should be inferred from this note.
- Each authorized run is bounded by the owner's attempt limit. Do not retry, alter the runner, grant missing privileges, or start additional infrastructure automatically after a failure.
