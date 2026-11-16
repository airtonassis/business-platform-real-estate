# Docker and Windows notes

Environment-specific guidance only. Prefer the product runner's validated checks and current Windows/Docker Desktop setup; do not assume these values apply to Linux hosts or other products.

- Accept only explicitly supported local Docker endpoints. Do not treat an arbitrary named pipe or remote context as local merely because it uses a familiar scheme.
- Verify the selected context and daemon identity before resource creation. A local Docker CLI is not proof that its configured daemon is local.
- Where PostgreSQL data must be ephemeral, verify the effective mount covering the actual `PGDATA`, not only a declared `--tmpfs` option or Docker `Mounts` summary. On Linux containers, `/proc/self/mountinfo` can provide runtime evidence; validate target, `tmpfs`, `rw`, `noexec`, `nosuid`, and configured size against the runner's policy.
- In a phase that uses only `docker exec`, avoid publishing a host port. Require zero published bindings in that mode. If a test genuinely needs host connectivity, document and constrain the binding to the approved loopback address.
- Enforce CPU, memory, tmpfs, startup, and command timeouts before launch. Ensure timeout parameters accepted by helper functions are propagated to the underlying process call.
- Keep generated passwords out of command-line arguments, logs, receipts, and versioned files. Use ephemeral protected files/streams and remove only run-owned copies during cleanup.
- If preflight evidence is gathered before a later check fails, persist the sanitized evidence immediately so the receipt retains it.
