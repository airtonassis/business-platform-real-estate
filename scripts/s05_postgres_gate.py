"""Isolated PostgreSQL 17 gate for ERP Real Estate S05-B1/B2.

Default invocation is inspection-only. Pass --run to create and remove an owned,
disposable PostgreSQL container/network and execute the explicitly listed gate.
This runner never applies grants to application tables and never removes evidence.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT / "database" / "migrations"
TEST_PROJECT = ROOT / "tests" / "RealEstate.Tests" / "RealEstate.Tests.csproj"
RESULTS_ROOT = ROOT / "TestResults"
IMAGE_TAG = "postgres:17"
DOTNET_SDK_IMAGE_TAG = "mcr.microsoft.com/dotnet/sdk:10.0"
CONTAINER_PREFIX = "araia-s05-pg-"
EXECUTOR_PREFIX = "araia-s05-dotnet-"
NETWORK_PREFIX = "araia-s05-net-"
OWNER_LABEL = "araia.s05.pg.owner"
KIND_LABEL = "araia.s05.pg.kind"
EXECUTOR_KIND = "s05-dotnet-test-executor"
POSTGRES_NETWORK_ALIAS = "araia-s05-postgres"
MEMORY_BYTES = 1024 * 1024 * 1024
EXECUTOR_MEMORY_BYTES = 1024 * 1024 * 1024
MIN_HOST_FREE_BYTES = 3 * 1024 * 1024 * 1024
PGDATA = "/var/lib/postgresql/data"
PGDATA_TMPFS_SIZE_BYTES = 768 * 1024 * 1024
PINNED_DOCKER_CONTEXT: str | None = None
PINNED_DOCKER_ENDPOINT: str | None = None

LOCAL_DOCKER_ENDPOINTS = {
    "unix:///var/run/docker.sock",
    "npipe:////./pipe/docker_engine",
    "npipe:////./pipe/dockerDesktopLinuxEngine",
}

# Only these database test classes may be selected. Missing classes are NOT RUN.
DB_TEST_CLASSES = (
    "CommercialAppointmentDatabaseTests",
    "NegotiationDatabaseTests",
    "NegotiationActionDatabaseTests",
    "NegotiationActivityTransferDatabaseTests",
)

TARGET_TABLES = (
    "commercial_appointments",
    "commercial_appointment_events",
    "commercial_negotiations",
    "commercial_negotiation_events",
    "commercial_negotiation_actions",
    "commercial_negotiation_action_events",
    "commercial_negotiation_transfer_requests",
    "commercial_negotiation_transfer_followups",
    "commercial_negotiation_transfer_followup_events",
)

# This is a diagnostic contract, not a grant script. Any missing item blocks B.
REQUIRED_TABLE_PRIVILEGES = (
    ("commercial_appointments", "SELECT"),
    ("commercial_appointments", "INSERT"),
    ("commercial_appointment_events", "SELECT"),
    ("commercial_appointment_events", "INSERT"),
    ("commercial_negotiations", "SELECT"),
    ("commercial_negotiations", "INSERT"),
    ("commercial_negotiation_events", "SELECT"),
    ("commercial_negotiation_events", "INSERT"),
    ("commercial_negotiation_actions", "SELECT"),
    ("commercial_negotiation_actions", "INSERT"),
    ("commercial_negotiation_action_events", "SELECT"),
    ("commercial_negotiation_action_events", "INSERT"),
    ("commercial_negotiation_transfer_requests", "SELECT"),
    ("commercial_negotiation_transfer_requests", "INSERT"),
    ("commercial_negotiation_transfer_followups", "SELECT"),
    ("commercial_negotiation_transfer_followups", "INSERT"),
    ("commercial_negotiation_transfer_followup_events", "SELECT"),
    ("commercial_negotiation_transfer_followup_events", "INSERT"),
)

REQUIRED_COLUMN_UPDATES = {
    "commercial_appointments": (
        "status", "starts_at", "ends_at", "location", "notes", "result",
        "responsible_subject", "responsible_name", "version", "updated_by", "updated_at",
    ),
    "commercial_negotiations": (
        "responsible_subject", "responsible_membership_id", "version",
    ),
    "commercial_negotiation_actions": (
        "status", "result", "responsible_subject", "responsible_name",
        "version", "updated_by", "updated_at",
    ),
    "commercial_negotiation_transfer_followups": (
        "status", "result", "version", "updated_by", "updated_at",
    ),
}

# Agenda customer lookup is column-scoped; do not grant table-wide SELECT.
REQUIRED_COLUMN_SELECTS = {
    "customers": ("organization_id", "id", "establishment_id", "name", "responsible_subject"),
    "owners": ("organization_id", "id", "name", "active"),
    "properties": ("organization_id", "establishment_id", "id", "status", "code", "commercial_code", "title"),
}
CUSTOMER_LOOKUP_TABLES = ("customers", "customer_contacts", "customer_responsibility_history")
REFERENCE_TABLES = ("owners", "properties")
RLS_CHECK_TABLES = TARGET_TABLES + CUSTOMER_LOOKUP_TABLES + REFERENCE_TABLES
FORBIDDEN_TABLE_PRIVILEGES = tuple(
    (table, privilege)
    for table in CUSTOMER_LOOKUP_TABLES
    for privilege in ("INSERT", "UPDATE", "DELETE", "TRUNCATE")
) + (("customer_contacts", "SELECT"), ("customer_responsibility_history", "SELECT")) + tuple(
    (table, privilege) for table in REFERENCE_TABLES for privilege in ("INSERT", "UPDATE", "DELETE", "TRUNCATE")
)
FORBIDDEN_COLUMN_PRIVILEGES = (
    ("customers", "data", "SELECT"),
    ("customers", "phone", "SELECT"),
    ("customers", "email", "SELECT"),
    ("customers", "document", "SELECT"),
    ("owners", "tax_id", "SELECT"),
    ("owners", "history", "SELECT"),
    ("owners", "details", "SELECT"),
    ("properties", "capture", "SELECT"),
    ("properties", "internal_reference", "SELECT"),
    ("properties", "summary", "SELECT"),
) + tuple(
    (table, column, "SELECT")
    for table, columns in {
        "customer_contacts": ("organization_id", "establishment_id", "customer_id", "id", "at", "kind", "description", "actor", "responsible_subject"),
        "customer_responsibility_history": ("organization_id", "establishment_id", "customer_id", "id", "at", "actor", "previous_subject", "new_subject"),
    }.items()
    for column in columns
) + tuple(
    (table, column, privilege)
    for table, columns in {
        "customers": ("organization_id", "id", "establishment_id", "name", "data", "phone", "email", "document", "responsible_subject", "version", "created_by", "created_at", "updated_by", "updated_at"),
        "customer_contacts": ("organization_id", "establishment_id", "customer_id", "id", "at", "kind", "description", "actor", "responsible_subject"),
        "customer_responsibility_history": ("organization_id", "establishment_id", "customer_id", "id", "at", "actor", "previous_subject", "new_subject"),
    }.items()
    for column in columns
    for privilege in ("INSERT", "UPDATE")
)

IMMUTABLE_EVENT_TABLES = (
    "commercial_appointment_events",
    "commercial_negotiation_events",
    "commercial_negotiation_action_events",
    "commercial_negotiation_transfer_followup_events",
)
APPEND_ONLY_TABLES = IMMUTABLE_EVENT_TABLES + ("commercial_negotiation_transfer_requests",)


class GateError(RuntimeError):
    pass


class PgdataTmpfsError(GateError):
    def __init__(self, message: str, diagnostic: dict[str, Any]):
        super().__init__(message)
        self.diagnostic = diagnostic


class InfrastructureError(GateError):
    pass


class MigrationError(GateError):
    pass


class DiagnosticError(GateError):
    pass


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def scrub(text: str, secrets_to_hide: tuple[str, ...] = ()) -> str:
    result = text
    for secret in secrets_to_hide:
        if secret:
            result = result.replace(secret, "[REDACTED]")
    return result


def invoke(args: list[str], *, input_text: str | None = None, timeout: int = 30,
           env: dict[str, str] | None = None) -> subprocess.CompletedProcess[str]:
    if env is None and PINNED_DOCKER_ENDPOINT:
        env = os.environ.copy()
        env.pop("DOCKER_CONTEXT", None)
        env["DOCKER_HOST"] = PINNED_DOCKER_ENDPOINT
    return subprocess.run(
        args,
        cwd=ROOT,
        input=input_text,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        env=env,
        check=False,
    )


def docker(args: list[str], *, input_text: str | None = None, timeout: int = 30,
           secrets_to_hide: tuple[str, ...] = ()) -> str:
    exe = shutil.which("docker")
    if not exe:
        raise GateError("Docker CLI indisponível")
    result = invoke([exe, *args], input_text=input_text, timeout=timeout)
    output = scrub((result.stdout or "") + (result.stderr or ""), secrets_to_hide)
    if result.returncode != 0:
        raise InfrastructureError(f"docker {args[0]} falhou (exit {result.returncode}): {output[-1200:]}")
    return result.stdout.strip()


def normalized_arch(value: Any) -> str | None:
    aliases = {"x86_64": "amd64", "amd64": "amd64", "aarch64": "arm64", "arm64": "arm64"}
    return aliases.get(str(value).lower())


def architectures_compatible(image_arch: Any, daemon_arch: Any) -> bool:
    normalized_image = normalized_arch(image_arch)
    normalized_daemon = normalized_arch(daemon_arch)
    return normalized_image is not None and normalized_daemon is not None and normalized_image == normalized_daemon


def parse_docker_inspect_object(raw: str, command: str) -> dict[str, Any]:
    try:
        payload = json.loads(raw)
    except (json.JSONDecodeError, TypeError) as exc:
        raise GateError(f"{command} --format returned invalid JSON") from exc
    if not isinstance(payload, dict):
        raise GateError(f"{command} --format expected one JSON object")
    return payload


def _docker_inspect_proves_absent(result: subprocess.CompletedProcess[str], resource_name: str,
                                  resource_type: str) -> bool:
    if result.returncode == 0:
        return False
    output = ((result.stdout or "") + "\n" + (result.stderr or "")).strip()
    kinds = ("object", "container") if resource_type == "container" else ("object", "network")
    return any(
        re.search(rf"(?:^|\n)(?:Error:\s*)?No such {kind}: {re.escape(resource_name)}\s*(?:\n|$)", output)
        is not None
        for kind in kinds
    )


def _inspect_owned_resource(resource_type: str, lookup: str, expected_name: str,
                            owner_label: str, owner_value: str,
                            kind_label: str, kind_value: str,
                            image_id: str | None, *, expected_id: str | None = None,
                            invoke_fn: Any | None = None) -> dict[str, Any]:
    call = invoke_fn or invoke
    exe = shutil.which("docker") or "docker"
    args = [exe, "network", "inspect", lookup] if resource_type == "network" else [exe, "inspect", lookup]
    try:
        result = call(args, timeout=10)
    except subprocess.TimeoutExpired:
        return {"status": "INCONCLUSIVE", "reason": "resource inspection timed out"}
    except Exception:
        return {"status": "INCONCLUSIVE", "reason": "resource inspection failed"}
    if result.returncode != 0:
        if _docker_inspect_proves_absent(result, lookup, resource_type):
            return {"status": "ABSENT"}
        return {"status": "INCONCLUSIVE", "reason": "resource inspection did not prove absence"}
    try:
        payload = json.loads(result.stdout or "")
    except (json.JSONDecodeError, TypeError):
        return {"status": "INCONCLUSIVE", "reason": "resource inspection returned invalid JSON"}
    if not isinstance(payload, list) or len(payload) != 1 or not isinstance(payload[0], dict):
        return {"status": "INCONCLUSIVE", "reason": "resource inspection returned an unexpected shape"}
    info = payload[0]
    labels = (((info.get("Config") or {}).get("Labels") or {}) if resource_type == "container"
              else info.get("Labels") or {})
    actual_name = "/" + expected_name if resource_type == "container" else expected_name
    actual_id = info.get("Id")
    matches = (
        actual_id == expected_id if expected_id is not None else isinstance(actual_id, str)
    ) and (
        info.get("Name") == actual_name
        and labels.get(owner_label) == owner_value
        and labels.get(kind_label) == kind_value
        and (image_id is None or info.get("Image") == image_id)
        and isinstance(actual_id, str)
        and re.fullmatch(r"[0-9a-f]{64}", actual_id) is not None
    )
    if not matches:
        return {"status": "OWNERSHIP_MISMATCH", "reason": "resource ownership could not be proven"}
    return {"status": "FOUND", "id": actual_id, "info": info}


def recover_owned_resource_by_name(resource_type: str, resource_name: str,
                                   owner_label: str, owner_value: str,
                                   kind_label: str, kind_value: str,
                                   image_id: str | None, *,
                                   invoke_fn: Any | None = None) -> dict[str, Any]:
    """Recover a possibly-created resource only after proving its exact ownership."""
    return _inspect_owned_resource(
        resource_type, resource_name, resource_name, owner_label, owner_value,
        kind_label, kind_value, image_id, invoke_fn=invoke_fn,
    )


def cleanup_owned_resource_by_id(resource_type: str, resource_id: str, expected_name: str,
                                 owner_label: str, owner_value: str,
                                 kind_label: str, kind_value: str,
                                 image_id: str | None, *,
                                 invoke_fn: Any | None = None) -> dict[str, Any]:
    before = _inspect_owned_resource(
        resource_type, resource_id, expected_name, owner_label, owner_value,
        kind_label, kind_value, image_id, expected_id=resource_id, invoke_fn=invoke_fn,
    )
    if before["status"] == "ABSENT":
        return {"status": "ABSENT"}
    if before["status"] != "FOUND":
        return {"status": "FAIL", "reason": before.get("reason", "ownership was not proven")}
    if resource_type == "network" and (before["info"].get("Containers") or {}):
        return {"status": "FAIL", "reason": "owned network still has members"}
    call = invoke_fn or invoke
    exe = shutil.which("docker") or "docker"
    remove_args = [exe, "network", "rm", resource_id] if resource_type == "network" else [exe, "rm", "--force", resource_id]
    remove_succeeded = False
    remove_error: str | None = None
    try:
        removed = call(remove_args, timeout=30)
        remove_succeeded = removed.returncode == 0
        if not remove_succeeded:
            remove_error = f"remove command exited {removed.returncode}"
    except Exception as exc:
        # A timeout may happen after Docker removed the object; verify by immutable ID below.
        remove_error = f"remove command raised {type(exc).__name__}"
    after = _inspect_owned_resource(
        resource_type, resource_id, expected_name, owner_label, owner_value,
        kind_label, kind_value, image_id, expected_id=resource_id, invoke_fn=call,
    )
    if after["status"] == "ABSENT":
        return {"status": "REMOVED", "removeCommandSucceeded": remove_succeeded,
                "removeCommandDiagnostic": remove_error}
    if after["status"] == "FOUND":
        return {"status": "FAIL", "reason": "resource remains after cleanup",
                "removeCommandDiagnostic": remove_error}
    return {"status": "FAIL", "reason": "post-cleanup absence could not be proven"}


def cleanup_run_resources(resources: list[dict[str, Any]], *, local_context_verified: bool,
                          owner_label: str = OWNER_LABEL, kind_label: str = KIND_LABEL,
                          invoke_fn: Any | None = None) -> tuple[dict[str, str], list[str]]:
    """Recover and remove only resources from this run; uncertainty is a cleanup failure."""
    statuses: dict[str, str] = {}
    errors: list[str] = []
    for resource in resources:
        key = resource["key"]
        resource_id = resource.get("id")
        if resource_id and not resource.get("createAttempted"):
            statuses[key] = "FAIL"
            errors.append(f"{key}: resource ID exists without a create attempt recorded by this run")
            continue
        if (resource_id or resource.get("createAttempted")) and not local_context_verified:
            statuses[key] = "FAIL"
            errors.append(f"{key}: Docker endpoint ownership context was not verified")
            continue
        if not resource_id:
            if not resource.get("createAttempted"):
                statuses[key] = "NOT CREATED"
                continue
            try:
                recovered = recover_owned_resource_by_name(
                    resource["type"], resource["name"], owner_label, resource["owner"],
                    kind_label, resource["kind"], resource.get("imageId"), invoke_fn=invoke_fn,
                )
            except KeyboardInterrupt:
                statuses[key] = "INTERRUPTED"
                errors.append(f"{key}: interrupted while recovering owned resource")
                continue
            if recovered["status"] == "ABSENT":
                statuses[key] = "ABSENT"
                continue
            if recovered["status"] != "FOUND":
                statuses[key] = "FAIL"
                errors.append(f"{key}: {recovered.get('reason', recovered['status'])}")
                continue
            resource_id = recovered["id"]
        try:
            result = cleanup_owned_resource_by_id(
                resource["type"], resource_id, resource["name"], owner_label,
                resource["owner"], kind_label, resource["kind"], resource.get("imageId"),
                invoke_fn=invoke_fn,
            )
        except KeyboardInterrupt:
            statuses[key] = "INTERRUPTED"
            errors.append(f"{key}: interrupted while cleaning owned resource")
            continue
        except Exception:
            statuses[key] = "FAIL"
            errors.append(f"{key}: cleanup raised an unexpected error")
            continue
        statuses[key] = result["status"]
        if result["status"] == "FAIL":
            errors.append(f"{key}: {result.get('reason', 'cleanup was not proven')}")
    return statuses, errors


def cleanup_result(errors: list[str], resources: dict[str, str] | None = None) -> str:
    resolved = {"NOT CREATED", "ABSENT", "REMOVED"}
    if errors or any(state not in resolved for state in (resources or {}).values()):
        return "FAIL"
    return "PASS"


def overall_gate_status(phase_a_status: str, phase_b_status: str, cleanup_status: str) -> str:
    if cleanup_status != "PASS":
        return "FAIL"
    if phase_a_status in {"FAIL", "INTERRUPTED"} or phase_b_status in {"FAIL", "INTERRUPTED"}:
        return "FAIL"
    if phase_a_status == "PASS" and phase_b_status == "PASS":
        return "PASS"
    return "BLOCKED"


def free_memory_bytes() -> int | None:
    if os.name == "nt":
        class MEMORYSTATUSEX(ctypes.Structure):
            _fields_ = [
                ("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
            ]
        status = MEMORYSTATUSEX()
        status.dwLength = ctypes.sizeof(status)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
            return int(status.ullAvailPhys)
        return None
    try:
        return int(os.sysconf("SC_AVPHYS_PAGES") * os.sysconf("SC_PAGE_SIZE"))
    except (ValueError, OSError, AttributeError):
        return None


def migration_files() -> list[tuple[int, Path]]:
    if not MIGRATIONS.is_dir():
        raise GateError(f"Diretório de migrations ausente: {MIGRATIONS}")
    found: list[tuple[int, Path]] = []
    for path in MIGRATIONS.glob("*.sql"):
        match = re.match(r"^(\d{3})_.+\.sql$", path.name)
        if match:
            found.append((int(match.group(1)), path))
    found.sort(key=lambda item: item[0])
    versions = [number for number, _ in found]
    if versions != list(range(1, 33)):
        raise GateError(f"Esperada cadeia contínua 001–032; encontrada: {versions}")
    return found


def migration_manifest() -> list[dict[str, Any]]:
    return [
        {"version": number, "file": path.name,
         "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        for number, path in migration_files()
    ]


def read_only_inspection() -> dict[str, Any]:
    report: dict[str, Any] = {
        "mode": "INSPECT_ONLY",
        "atUtc": utc_now(),
        "repository": str(ROOT),
        "migrations": {"status": "NOT RUN", "files": []},
        "docker": {"status": "NOT RUN"},
        "postgresImage": {"status": "NOT RUN"},
        "existingOwnedContainers": {"status": "NOT RUN"},
        "hostFreeMemoryBytes": free_memory_bytes(),
        "resourcesCreated": False,
    }
    try:
        report["migrations"] = {"status": "PASS", "files": migration_manifest()}
    except Exception as exc:
        report["migrations"] = {"status": "FAIL", "detail": str(exc)}
    exe = shutil.which("docker")
    if not exe:
        report["docker"] = {"status": "BLOCKED", "detail": "Docker CLI ausente"}
        return report
    try:
        context = parse_docker_context_inspect(
            docker(["context", "inspect", "--format", "{{json .}}"], timeout=15)
        )
        endpoint = context.get("Endpoints", {}).get("docker", {}).get("Host", "")
        if endpoint not in LOCAL_DOCKER_ENDPOINTS:
            raise GateError(f"Contexto Docker não local, recusado: {endpoint or 'sem endpoint'}")
        info = parse_docker_inspect_object(
            docker(["info", "--format", "{{json .}}"], timeout=20), "docker info"
        )
        report["docker"] = {
            "status": "PASS", "context": context.get("Name"), "endpoint": endpoint,
            "serverVersion": info.get("ServerVersion"), "operatingSystem": info.get("OperatingSystem"),
        }
    except Exception as exc:
        report["docker"] = {"status": "BLOCKED", "detail": str(exc)}
        return report
    try:
        image = parse_docker_inspect_object(
            docker(["image", "inspect", IMAGE_TAG, "--format", "{{json .}}"], timeout=20),
            "docker image inspect",
        )
        image_id = image.get("Id", "")
        tags = image.get("RepoTags") or []
        if IMAGE_TAG not in tags or not image_id.startswith("sha256:"):
            raise GateError("Imagem local sem tag/imagem ID verificável")
        report["postgresImage"] = {
            "status": "PASS", "tag": IMAGE_TAG, "imageId": image_id,
            "repoDigests": image.get("RepoDigests") or [],
            "note": "Major efetiva será confirmada no container antes das migrations.",
        }
    except Exception as exc:
        report["postgresImage"] = {"status": "BLOCKED", "detail": str(exc)}
    try:
        existing_text = docker([
            "ps", "-a", "--filter", f"label={KIND_LABEL}", "--format", "{{json .}}"
        ], timeout=15)
        existing = [json.loads(line) for line in existing_text.splitlines() if line.strip()]
        report["existingOwnedContainers"] = {
            "status": "PASS" if not existing else "BLOCKED",
            "count": len(existing),
            "containers": [{"id": x.get("ID"), "name": x.get("Names"), "state": x.get("State")} for x in existing],
            "note": "Inspeção não altera containers encontrados.",
        }
    except Exception as exc:
        report["existingOwnedContainers"] = {"status": "BLOCKED", "detail": str(exc)}
    try:
        existing_text = docker([
            "network", "ls", "--filter", f"label={KIND_LABEL}", "--format", "{{json .}}"
        ], timeout=15)
        existing = [json.loads(line) for line in existing_text.splitlines() if line.strip()]
        report["existingOwnedNetworks"] = {
            "status": "PASS" if not existing else "BLOCKED",
            "count": len(existing),
            "networks": [{"id": x.get("ID"), "name": x.get("Name")} for x in existing],
        }
    except Exception as exc:
        report["existingOwnedNetworks"] = {"status": "BLOCKED", "detail": str(exc)}
    return report


def sql_literal(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def psql(container_id: str, database: str, sql: str, *, secrets_to_hide: tuple[str, ...] = (),
         json_result: bool = False, password_file: str | None = None,
         username: str = "postgres", timeout: int = 90) -> Any:
    args = ["exec", "-i"]
    if password_file:
        args.extend(["-u", "0", "-e", f"PGPASSFILE={password_file}"])
    args.extend([container_id, "psql", "-X", "-q", "-v", "ON_ERROR_STOP=1"])
    if password_file:
        args.extend(["-h", "127.0.0.1", "-p", "5432", "-U", username])
    else:
        args.extend(["-U", "postgres"])
    args.extend(["-d", database])
    if json_result:
        psql_index = args.index("psql")
        args[psql_index + 1:psql_index + 1] = ["-A", "-t"]
    try:
        raw = docker(args, input_text=sql, timeout=timeout, secrets_to_hide=secrets_to_hide)
    except InfrastructureError as exc:
        message = str(exc)
        if "ERROR:" in message or "SQLSTATE" in message:
            raise MigrationError(message) from exc
        raise
    if json_result:
        try:
            return json.loads(raw.strip().splitlines()[-1])
        except (ValueError, IndexError) as exc:
            raise GateError("Resposta PostgreSQL não era JSON válido") from exc
    return raw


def phase_a_sql() -> str:
    privilege_rows = ",\n".join(
        f"({sql_literal(table)}, {sql_literal(privilege)}, NULL::text)"
        for table, privilege in REQUIRED_TABLE_PRIVILEGES
    )
    column_rows = ",\n".join(
        [f"({sql_literal(table)}, 'UPDATE', {sql_literal(column)})"
         for table, columns in REQUIRED_COLUMN_UPDATES.items() for column in columns]
        + [f"({sql_literal(table)}, 'SELECT', {sql_literal(column)})"
           for table, columns in REQUIRED_COLUMN_SELECTS.items() for column in columns]
    )
    no_write_rows = ",\n".join(
        [f"({sql_literal(table)}, 'DELETE', NULL::text)" for table in TARGET_TABLES]
        + [f"({sql_literal(table)}, 'UPDATE', NULL::text)" for table in APPEND_ONLY_TABLES]
        + [f"({sql_literal(table)}, {sql_literal(privilege)}, NULL::text)"
           for table, privilege in FORBIDDEN_TABLE_PRIVILEGES]
    )
    forbidden_column_rows = ",\n".join(
        f"({sql_literal(table)}, {sql_literal(privilege)}, {sql_literal(column)})"
        for table, column, privilege in FORBIDDEN_COLUMN_PRIVILEGES
    )
    required_tables = ",".join(sql_literal(t) for t in TARGET_TABLES)
    rls_tables = ",".join(sql_literal(t) for t in RLS_CHECK_TABLES)
    immutable_tables = ",".join(sql_literal(t) for t in IMMUTABLE_EVENT_TABLES)
    return f"""
WITH required_tables(table_name) AS (VALUES {','.join(f'({sql_literal(t)})' for t in TARGET_TABLES)}),
rls_tables(table_name) AS (VALUES {','.join(f'({sql_literal(t)})' for t in RLS_CHECK_TABLES)}),
table_privileges(table_name, privilege, column_name) AS (VALUES {privilege_rows}),
column_privileges(table_name, privilege, column_name) AS (VALUES {column_rows}),
forbidden_privileges(table_name, privilege, column_name) AS (VALUES {no_write_rows}),
forbidden_column_privileges(table_name, privilege, column_name) AS (VALUES {forbidden_column_rows}),
tables AS (
 SELECT c.relname AS table_name, c.relrowsecurity AS rls_enabled, c.relforcerowsecurity AS rls_forced,
   (SELECT count(*) FROM pg_policies p WHERE p.schemaname='real_estate' AND p.tablename=c.relname) AS policy_count
 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='real_estate' AND c.relkind='r' AND c.relname IN ({rls_tables})
),
constraints AS (
 SELECT conname, convalidated FROM pg_constraint
 WHERE conrelid IN (SELECT format('real_estate.%I',table_name)::regclass FROM required_tables
                    WHERE to_regclass(format('real_estate.%I',table_name)) IS NOT NULL)
),
triggers AS (
 SELECT c.relname AS table_name, t.tgname, t.tgenabled, fn_ns.nspname || '.' || fn.proname AS function_name
 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
 JOIN pg_proc fn ON fn.oid=t.tgfoid JOIN pg_namespace fn_ns ON fn_ns.oid=fn.pronamespace
 WHERE n.nspname='real_estate' AND NOT t.tgisinternal AND c.relname IN ({immutable_tables})
),
indexes AS (
 SELECT indexrelid::regclass::text AS index_name, indisunique AS is_unique, indpred IS NOT NULL AS partial
 FROM pg_index WHERE indexrelid=to_regclass('real_estate.commercial_negotiation_followup_pending_action')
),
foreign_keys AS (
 SELECT c.relname AS table_name, count(k.oid) AS fk_count,
   coalesce(bool_and(cardinality(k.conkey)>=2 AND k.convalidated),false) AS all_composite_validated
 FROM required_tables r
 LEFT JOIN pg_namespace n ON n.nspname='real_estate'
 LEFT JOIN pg_class c ON c.relname=r.table_name AND c.relnamespace=n.oid
 LEFT JOIN pg_constraint k ON k.conrelid=c.oid AND k.contype='f'
 GROUP BY r.table_name,c.relname
)
SELECT jsonb_build_object(
 'database', current_database(),
 'runtimeRole', (SELECT jsonb_build_object('exists',count(*)=1,'canLogin',bool_or(rolcanlogin),
   'superuser',bool_or(rolsuper),'bypassRls',bool_or(rolbypassrls)) FROM pg_roles WHERE rolname='erp_runtime'),
 'ddlRole', (SELECT jsonb_build_object('exists',count(*)=1,'canLogin',bool_or(rolcanlogin),
   'superuser',bool_or(rolsuper),'bypassRls',bool_or(rolbypassrls)) FROM pg_roles WHERE rolname='erp_ddl'),
 'runtimeConnect', has_database_privilege('erp_runtime',current_database(),'CONNECT'),
 'ddlConnect', has_database_privilege('erp_ddl',current_database(),'CONNECT'),
 'ddlCreate', has_database_privilege('erp_ddl',current_database(),'CREATE'),
 'schemaUsage', has_schema_privilege('erp_runtime','real_estate','USAGE'),
 'versions', (SELECT coalesce(array_agg(version ORDER BY version),'{{}}'::integer[]) FROM real_estate.schema_versions),
 'tables', (SELECT coalesce(jsonb_agg(jsonb_build_object('name',r.table_name,'rls',t.rls_enabled,'forceRls',t.rls_forced,
    'policyCount',t.policy_count)
     ORDER BY r.table_name),'[]'::jsonb) FROM rls_tables r LEFT JOIN tables t USING(table_name)),
 'tablePrivileges', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',v.table_name,'privilege',v.privilege,
    'granted',to_regclass(format('real_estate.%I',v.table_name)) IS NOT NULL AND
      has_table_privilege('erp_runtime',format('real_estate.%I',v.table_name),v.privilege))),'[]'::jsonb)
    FROM table_privileges v),
 'columnPrivileges', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',v.table_name,'column',v.column_name,
    'privilege',v.privilege,'granted',to_regclass(format('real_estate.%I',v.table_name)) IS NOT NULL AND
      has_column_privilege('erp_runtime',format('real_estate.%I',v.table_name),v.column_name,v.privilege))),'[]'::jsonb)
    FROM column_privileges v),
 'forbiddenPrivileges', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',v.table_name,'privilege',v.privilege,
    'absent',to_regclass(format('real_estate.%I',v.table_name)) IS NOT NULL AND
      NOT has_table_privilege('erp_runtime',format('real_estate.%I',v.table_name),v.privilege))),'[]'::jsonb)
    FROM forbidden_privileges v),
  'forbiddenColumnPrivileges', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',v.table_name,'column',v.column_name,
     'privilege',v.privilege,'absent',to_regclass(format('real_estate.%I',v.table_name)) IS NOT NULL AND
       NOT has_column_privilege('erp_runtime',format('real_estate.%I',v.table_name),v.column_name,v.privilege))),'[]'::jsonb)
     FROM forbidden_column_privileges v),
 'appendOnlyColumnUpdatesAbsent', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',r.table_name,
    'absent',to_regclass(format('real_estate.%I',r.table_name)) IS NOT NULL AND
      NOT has_any_column_privilege('erp_runtime',format('real_estate.%I',r.table_name),'UPDATE'))),'[]'::jsonb)
    FROM required_tables r WHERE r.table_name IN ({','.join(sql_literal(t) for t in APPEND_ONLY_TABLES)})),
 'constraintsValidated', (SELECT count(*)=0 FROM constraints WHERE NOT convalidated),
 'foreignKeys', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',table_name,'count',fk_count,
    'allCompositeValidated',all_composite_validated) ORDER BY table_name),'[]'::jsonb) FROM foreign_keys),
 'actionAppointmentUnique', EXISTS(SELECT 1 FROM pg_constraint c
   WHERE c.conrelid='real_estate.commercial_negotiation_actions'::regclass
   AND c.contype='u' AND c.convalidated AND cardinality(c.conkey)=2
   AND c.conkey[1]=(SELECT attnum FROM pg_attribute WHERE attrelid=c.conrelid AND attname='organization_id')
   AND c.conkey[2]=(SELECT attnum FROM pg_attribute WHERE attrelid=c.conrelid AND attname='appointment_id')),
 'eventTriggers', (SELECT coalesce(jsonb_agg(jsonb_build_object('table',t.table_name,'trigger',t.tgname,
    'enabled',t.tgenabled='O','function',t.function_name)),'[]'::jsonb) FROM triggers t),
 'pendingFollowupIndex', (SELECT coalesce(bool_or(is_unique AND partial),false) FROM indexes)
)::text;
"""


def phase_a_blockers(report: dict[str, Any]) -> list[str]:
 blockers: list[str] = []
 for missing in report.get("missingObjects", []):
  blockers.append(f"Objeto estrutural ausente: {missing}")
 runtime = report.get("runtimeRole") or {}
 ddl = report.get("ddlRole") or {}
 if not runtime.get("exists") or not runtime.get("canLogin") or runtime.get("superuser") or runtime.get("bypassRls"):
  blockers.append("erp_runtime ausente, sem LOGIN ou com privilégio elevado")
 if not ddl.get("exists") or not ddl.get("canLogin") or ddl.get("superuser") or ddl.get("bypassRls"):
  blockers.append("erp_ddl deve aceitar login próprio, sem SUPERUSER ou BYPASSRLS")
 if report.get("runtimeConnect") is not True:
  blockers.append("erp_runtime sem CONNECT no banco descartável")
 if report.get("ddlConnect") is not True or report.get("ddlCreate") is not True:
  blockers.append("erp_ddl sem CONNECT/CREATE no banco descartável")
 if report.get("runtimeAuthentication", {}).get("authenticated") is not True:
  blockers.append("erp_runtime não autenticou no banco descartável")
 if report.get("schemaUsage") is not True:
  blockers.append("erp_runtime sem USAGE no schema real_estate")
 if report.get("versions") != list(range(1, 33)):
  blockers.append("schema_versions não contém exatamente 1–32")
 tables = report.get("tables") or []
 for table in tables:
  if table.get("rls") is not True or table.get("forceRls") is not True:
   blockers.append(f"RLS/FORCE RLS ausente em {table.get('name')}")
  if not isinstance(table.get("policyCount"), int) or table.get("policyCount", 0) < 1:
   blockers.append(f"Política RLS ausente em {table.get('name')}")
  if table.get("name") and table.get("rls") is None:
   blockers.append(f"Tabela obrigatória ausente: {table['name']}")
 for item in report.get("tablePrivileges", []):
  if item.get("granted") is not True:
   blockers.append(f"Privilégio ausente: {item.get('privilege')} em {item.get('table')}")
 for item in report.get("columnPrivileges", []):
  if item.get("granted") is not True:
   blockers.append(f"Privilégio ausente: {item.get('privilege')}({item.get('column')}) em {item.get('table')}")
 for item in report.get("forbiddenPrivileges", []):
  if item.get("absent") is not True:
   blockers.append(f"Privilégio amplo presente: {item.get('privilege')} em {item.get('table')}")
 for item in report.get("forbiddenColumnPrivileges", []):
  if item.get("absent") is not True:
   blockers.append(f"Privilégio de coluna amplo presente: {item.get('privilege')}({item.get('column')}) em {item.get('table')}")
 for item in report.get("appendOnlyColumnUpdatesAbsent", []):
  if item.get("absent") is not True:
   blockers.append(f"UPDATE por coluna indevido em registro append-only: {item.get('table')}")
 if report.get("constraintsValidated") is not True:
  blockers.append("Há constraint não validada nos objetos da jornada")
 for fk in report.get("foreignKeys", []):
  if fk.get("count", 0) < 1 or fk.get("allCompositeValidated") is not True:
   blockers.append(f"FK ausente/não composta/não validada em {fk.get('table')}")
 if report.get("actionAppointmentUnique") is not True:
  blockers.append("Vínculo ação-compromisso sem unicidade validada")
 expected_triggers = set(IMMUTABLE_EVENT_TABLES)
 enabled_triggers = {x.get("table") for x in report.get("eventTriggers", [])
                     if x.get("enabled") and x.get("function") == "real_estate.reject_customer_history_change"}
 if not expected_triggers.issubset(enabled_triggers):
  blockers.append("Trigger imutável ausente/desabilitado em tabela de eventos")
 if report.get("pendingFollowupIndex") is not True:
  blockers.append("Índice único parcial de follow-up pendente ausente")
 return blockers


def discover_test_classes() -> tuple[list[str], list[str]]:
 found: list[str] = []
 missing: list[str] = []
 files = list((ROOT / "tests" / "RealEstate.Tests").rglob("*.cs"))
 source = "\n".join(p.read_text(encoding="utf-8-sig", errors="replace") for p in files)
 for name in DB_TEST_CLASSES:
  (found if re.search(rf"\bclass\s+{re.escape(name)}\b", source) else missing).append(name)
 return found, missing


def assert_no_prior_gate_resources() -> None:
 containers = docker(["ps", "-a", "--filter", f"label={KIND_LABEL}", "--format", "{{json .}}"], timeout=15)
 networks = docker(["network", "ls", "--filter", f"label={KIND_LABEL}", "--format", "{{json .}}"], timeout=15)
 if any(line.strip() for line in containers.splitlines()) or any(line.strip() for line in networks.splitlines()):
  raise GateError("Recursos de uma execuÃ§Ã£o anterior do gate ainda existem; limpeza/verificaÃ§Ã£o necessÃ¡ria")


def database_environment_names() -> list[str]:
 exact = {"erptestadminconnection", "appointmenttestruntimeconnection", "appointmenttestadminconnection"}
 return sorted((key for key in os.environ
                if key.lower().startswith(("connectionstrings__", "database__", "pg"))
                or key.lower() in exact), key=str.lower)


def prepare_test_artifacts() -> dict[str, Any]:
 found, missing = discover_test_classes()
 if missing or found != list(DB_TEST_CLASSES):
  raise GateError("As quatro classes PostgreSQL autorizadas nÃ£o estÃ£o disponÃ­veis nos fontes")
 test_directory = ROOT / "tests" / "RealEstate.Tests"
 test_bin = test_directory / "bin" / "Debug" / "net10.0"
 test_dll = test_bin / "RealEstate.Tests.dll"
 deps_path = test_bin / "RealEstate.Tests.deps.json"
 runtime_path = test_bin / "RealEstate.Tests.runtimeconfig.json"
 adapter_path = test_bin / "xunit.runner.visualstudio.testadapter.dll"
 if is_link_or_reparse(test_bin) or not test_bin.is_dir():
  raise GateError("DiretÃ³rio de assemblies de teste ausente ou nÃ£o confiÃ¡vel")
 for path in (test_dll, deps_path, runtime_path, adapter_path):
  if is_link_or_reparse(path) or not path.is_file():
   raise GateError(f"Artefato obrigatÃ³rio ausente ou nÃ£o confiÃ¡vel: {path.name}")
 files = [path for path in test_bin.rglob("*") if path.is_file()]
 if any(is_link_or_reparse(path) for path in files):
  raise GateError("Árvore de assemblies contém link/reparse point")
 source_files = [path for path in test_directory.rglob("*.cs")
                 if not {"bin", "obj"}.intersection(path.parts)]
 source_files.append(TEST_PROJECT)
 source_files.extend(path for path in (ROOT / "backend" / "src").rglob("*.cs")
                     if not {"bin", "obj"}.intersection(path.parts))
 source_files.extend(path for path in (ROOT / "backend" / "src").rglob("*.csproj")
                     if not {"bin", "obj"}.intersection(path.parts))
 source_files.extend(path for directory in (test_directory, ROOT / "backend" / "src")
                     for pattern in ("*.props", "*.targets") for path in directory.rglob(pattern)
                     if not {"bin", "obj"}.intersection(path.parts))
 source_files.extend(ROOT.glob("Directory.Build.*"))
 latest_source = max((path.stat().st_mtime for path in source_files), default=0)
 if test_dll.stat().st_mtime < latest_source:
  dotnet = shutil.which("dotnet")
  if not dotnet:
   raise GateError("Assembly PostgreSQL estÃ¡ desatualizado e dotnet local nÃ£o estÃ¡ disponÃ­vel")
  child_env = os.environ.copy()
  for key in database_environment_names():
   child_env.pop(key, None)
  result = invoke([dotnet, "build", str(TEST_PROJECT), "--no-restore", "--configuration", "Debug", "--nologo"],
                  timeout=1800, env=child_env)
  if result.returncode != 0:
   output = scrub((result.stdout or "") + "\n" + (result.stderr or ""))
   raise GateError("Build indispensÃ¡vel dos assemblies desatualizados falhou: " + output[-1600:])
  latest_source = max((path.stat().st_mtime for path in source_files), default=0)
  if not test_dll.is_file() or test_dll.stat().st_mtime < latest_source:
   raise GateError("Build local nÃ£o atualizou o assembly PostgreSQL")
 files = [path for path in test_bin.rglob("*") if path.is_file()]
 if any(is_link_or_reparse(path) for path in files):
  raise GateError("Build output contains a link/reparse point")
 deps = json.loads(deps_path.read_text(encoding="utf-8"))
 runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
 runtime_target = (deps.get("runtimeTarget") or {}).get("name", "")
 if runtime.get("runtimeOptions", {}).get("tfm") != "net10.0" or runtime_target != ".NETCoreApp,Version=v10.0":
  raise GateError("Target framework dos assemblies nÃ£o Ã© net10.0 portÃ¡til")
 assembly_bytes = test_dll.read_bytes()
 embedded_classes = [name for name in DB_TEST_CLASSES if name.encode("ascii") in assembly_bytes]
 if embedded_classes != list(DB_TEST_CLASSES):
  raise GateError("Assembly atual nÃ£o contÃ©m as quatro classes PostgreSQL esperadas")
 tree = hashlib.sha256()
 total_bytes = 0
 for path in sorted(files, key=lambda item: item.relative_to(test_bin).as_posix()):
  data = path.read_bytes()
  tree.update(path.relative_to(test_bin).as_posix().encode("utf-8") + b"\0" + hashlib.sha256(data).digest())
  total_bytes += len(data)
 return {
  "status": "PASS", "targetFramework": "net10.0", "rid": None,
  "classes": embedded_classes, "assemblySha256": hashlib.sha256(assembly_bytes).hexdigest(),
  "treeSha256": tree.hexdigest(), "fileCount": len(files), "totalBytes": total_bytes,
 }


def parse_trx(path: Path) -> dict[str, int]:
 import xml.etree.ElementTree as ET
 root = ET.parse(path).getroot()
 counts = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
 for node in root.iter():
  if node.tag.endswith("UnitTestResult"):
   counts["total"] += 1
   outcome = node.attrib.get("outcome", "").lower()
   if outcome == "passed": counts["passed"] += 1
   elif outcome in ("notexecuted", "skipped", "inconclusive"): counts["skipped"] += 1
   else: counts["failed"] += 1
 return counts


def connection_string(host: str, port: int, database: str, username: str, password: str) -> str:
 return (f"Host={host};Port={port};Database={database};Username={username};"
         f"Password={password};Timeout=5;Command Timeout=30;Include Error Detail=false")


def run_vstest_in_executor(executor_id: str, class_name: str, trx_name: str,
                           connection_strings: tuple[str, str], secrets_to_hide: tuple[str, ...]) -> subprocess.CompletedProcess[str]:
 if class_name not in DB_TEST_CLASSES or not re.fullmatch(r"db-[A-Za-z0-9]+\.trx", trx_name):
  raise GateError("Identificador de cen??rio inv??lido para dotnet vstest")
 script = (
  "set -eu\n"
  "IFS= read -r runtime_cs\n"
  "IFS= read -r admin_cs\n"
  "export ConnectionStrings__Erp=\"$runtime_cs\"\n"
  "export ErpTestAdminConnection=\"$admin_cs\"\n"
  "export AppointmentTestRuntimeConnection=\"$runtime_cs\"\n"
  "export AppointmentTestAdminConnection=\"$admin_cs\"\n"
  "export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_PROCESSOR_COUNT=1 "
  "DOTNET_GCHeapHardLimit=30000000 DOTNET_CLI_HOME=/tmp/dotnet-home\n"
  "exec dotnet vstest /tests/RealEstate.Tests.dll \"--TestCaseFilter:FullyQualifiedName~$1\" "
  "\"--Logger:trx;LogFileName=$2\" --TestAdapterPath:/tests --ResultsDirectory:/results\n"
 )
 docker_exe = shutil.which("docker") or "docker"
 payload = "\n".join(connection_strings) + "\n"
 result = invoke([docker_exe, "exec", "-i", executor_id, "sh", "-c", script,
                  "araia-vstest", class_name, trx_name], input_text=payload, timeout=1800)
 result.stdout = scrub(result.stdout or "", secrets_to_hide)
 result.stderr = scrub(result.stderr or "", secrets_to_hide)
 return result


def output_json(path: Path, value: Any) -> None:
    root = RESULTS_ROOT.resolve(strict=True)
    parent = path.parent.resolve(strict=True)
    if is_link_or_reparse(path.parent) or (parent != root and root not in parent.parents):
        raise GateError("Recibo fora da árvore TestResults própria")
    if path.exists() and is_link_or_reparse(path):
        raise GateError("Recusado sobrescrever artefato link simbólico")
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def sanitize_owned_artifacts(directory: Path, secrets_to_hide: tuple[str, ...]) -> None:
    encoded = [secret.encode("ascii") for secret in secrets_to_hide if secret]
    for path in directory.rglob("*"):
        if not path.is_file():
            continue
        if is_link_or_reparse(path):
            raise GateError(f"Artefato inesperado como link/reparse point: {path.name}")
        content = path.read_bytes()
        clean = content
        for secret in encoded:
            clean = clean.replace(secret, b"[REDACTED]")
        if clean != content:
            path.write_bytes(clean)
        if any(secret in clean for secret in encoded):
            raise GateError(f"Segredo não removido do artefato: {path.name}")


def ensure_results_root() -> Path:
 if RESULTS_ROOT.exists() and is_link_or_reparse(RESULTS_ROOT):
  raise GateError("TestResults não pode ser link simbólico")
 RESULTS_ROOT.mkdir(exist_ok=True)
 return RESULTS_ROOT.resolve(strict=True)


def is_link_or_reparse(path: Path) -> bool:
 if path.is_symlink():
  return True
 try:
  attributes = getattr(path.lstat(), "st_file_attributes", 0)
  return bool(attributes & 0x400)  # FILE_ATTRIBUTE_REPARSE_POINT on Windows
 except OSError:
  return False


def parse_docker_context_inspect(raw: str) -> dict[str, Any]:
 try:
  payload = json.loads(raw)
 except (json.JSONDecodeError, TypeError) as exc:
  raise GateError("docker context inspect returned invalid JSON") from exc
 if isinstance(payload, dict):
  context = payload
 elif isinstance(payload, list):
  if not payload:
   raise GateError("docker context inspect returned an empty list")
  if len(payload) != 1 or not isinstance(payload[0], dict):
   raise GateError("docker context inspect must contain exactly one context object")
  context = payload[0]
 else:
  raise GateError("docker context inspect must return an object or a list of objects")
 endpoints = context.get("Endpoints", {})
 if not isinstance(endpoints, dict):
  raise GateError("docker context inspect returned invalid Endpoints")
 docker_endpoint = endpoints.get("docker", {})
 if not isinstance(docker_endpoint, dict):
  raise GateError("docker context inspect returned an invalid docker endpoint")
 return context


def inspect_network_endpoint() -> str:
 result = invoke([shutil.which("docker") or "docker", "context", "inspect", "--format", "{{json .}}"], timeout=15)
 if result.returncode:
  raise InfrastructureError("Não foi possível inspecionar o endpoint Docker local")
 context = parse_docker_context_inspect(result.stdout)
 endpoint = context.get("Endpoints", {}).get("docker", {}).get("Host", "")
 if endpoint not in LOCAL_DOCKER_ENDPOINTS:
  raise InfrastructureError("Endpoint Docker remoto/ambíguo recusado")
 global PINNED_DOCKER_ENDPOINT
 PINNED_DOCKER_ENDPOINT = endpoint
 return context.get("Name") or "default"


def phase_a_preflight_sql() -> str:
 required = ",".join(sql_literal(t) for t in TARGET_TABLES)
 required_objects = ",".join(sql_literal(t) for t in TARGET_TABLES + CUSTOMER_LOOKUP_TABLES)
 required_columns = {
  (table, column) for table, columns in REQUIRED_COLUMN_UPDATES.items() for column in columns
 }
 required_columns.update({
  (table, column) for table, columns in REQUIRED_COLUMN_SELECTS.items() for column in columns
 })
 required_columns.update({
  (table, column) for table, columns in {
   "customer_contacts": ("organization_id", "establishment_id", "customer_id", "id", "at", "kind", "description", "actor", "responsible_subject"),
   "customer_responsibility_history": ("organization_id", "establishment_id", "customer_id", "id", "at", "actor", "previous_subject", "new_subject"),
  }.items() for column in columns
 })
 required_columns.update({
  ("commercial_negotiation_actions", "organization_id"),
  ("commercial_negotiation_actions", "appointment_id"),
 })
 column_values = ",".join(f"({sql_literal(table)}, {sql_literal(column)})"
                          for table, column in sorted(required_columns))
 return f"""
SELECT jsonb_build_object(
 'schemaExists', to_regnamespace('real_estate') IS NOT NULL,
 'missingObjects', (
   SELECT coalesce(jsonb_agg(x.object_name ORDER BY x.object_name), '[]'::jsonb)
   FROM (
     SELECT 'schema real_estate' AS object_name WHERE to_regnamespace('real_estate') IS NULL
     UNION ALL
     SELECT 'real_estate.schema_versions' WHERE to_regclass('real_estate.schema_versions') IS NULL
     UNION ALL
     SELECT 'column real_estate.schema_versions.version'
      WHERE to_regclass('real_estate.schema_versions') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM pg_attribute
                        WHERE attrelid=to_regclass('real_estate.schema_versions')
                          AND attname='version' AND NOT attisdropped)
     UNION ALL
     SELECT 'real_estate.' || required_name
       FROM unnest(ARRAY[{required_objects}]) AS required(required_name)
      WHERE to_regclass(format('real_estate.%I', required_name)) IS NULL
      UNION ALL
      SELECT format('column real_estate.%I.%I', required_columns.table_name, required_columns.column_name)
        FROM (VALUES {column_values}) AS required_columns(table_name, column_name)
       WHERE to_regclass(format('real_estate.%I', required_columns.table_name)) IS NOT NULL
         AND NOT EXISTS (
           SELECT 1 FROM pg_attribute a
            WHERE a.attrelid=to_regclass(format('real_estate.%I', required_columns.table_name))
              AND a.attname=required_columns.column_name AND NOT a.attisdropped
         )
   ) x
 )
)::text;
"""


def runtime_authentication_sql() -> str:
    return "SELECT jsonb_build_object('authenticated', current_user = 'erp_runtime', 'database', current_database())::text;"


def validate_gate_resources(container_id: str, owner: str, image_id: str,
                            network_name: str, network_id: str,
                            evidence: dict[str, Any] | None = None) -> None:
    container = validate_owned_container(container_id, owner, image_id, network_name, network_id,
                                         evidence=evidence)
    network = json.loads(docker(["network", "inspect", network_id], timeout=15))[0]
    labels = network.get("Labels") or {}
    members = set((network.get("Containers") or {}).keys())
    if (network.get("Id") != network_id or network.get("Name") != network_name
        or labels.get(OWNER_LABEL) != owner or labels.get(KIND_LABEL) != "s05-postgres-gate"
        or not network.get("Internal") or members != {container_id}):
        raise InfrastructureError("Rede do gate diverge de identidade, isolamento ou membership esperado")
    if container.get("HostConfig", {}).get("NetworkMode") not in (network_name, network_id):
        raise InfrastructureError("Container deixou de usar a rede isolada do gate")


def assert_zero_published_ports(host_config: dict[str, Any], network_ports: Any,
                                evidence: dict[str, Any] | None, role: str) -> None:
    def has_bindings(bindings: Any, field: str) -> bool:
        if bindings is None:
            return False
        if not isinstance(bindings, dict):
            raise InfrastructureError(f"{field} shape inválido; exposição não pode ser descartada")
        for values in bindings.values():
            if values is None:
                continue
            if not isinstance(values, list):
                raise InfrastructureError(f"{field} contém binding inválido")
            if values:
                return True
        return False
    if not isinstance(network_ports, dict):
        raise InfrastructureError("NetworkSettings.Ports shape invalid; exposure cannot be discarded")
    if (host_config.get("PublishAllPorts") is True
        or has_bindings(host_config.get("PortBindings"), "HostConfig.PortBindings")
        or has_bindings(network_ports, "NetworkSettings.Ports")):
        raise InfrastructureError(f"Porta publicada inesperada no container {role}")
    if evidence is not None:
        key = "postgresPortBindingCheck" if role == "PostgreSQL" else "executorPortBindingCheck"
        evidence[key] = {"status": "PASS", "mode": "none-published", "publishedBindings": 0}


def normalize_docker_mounts(value: Any) -> list[dict[str, Any]]:
    """Normalize supported Docker inspect mount shapes without inventing entries."""
    if isinstance(value, list):
        if any(not isinstance(item, dict) for item in value):
            raise InfrastructureError("Docker inspect Mounts contains a non-object entry")
        return value
    if isinstance(value, dict):
        if isinstance(value.get("Destination"), str):
            return [value]
        normalized: list[dict[str, Any]] = []
        for destination, item in value.items():
            if not isinstance(destination, str) or not isinstance(item, dict):
                raise InfrastructureError("Docker inspect Mounts map has an invalid entry")
            entry = dict(item)
            if entry.get("Destination", destination) != destination:
                raise InfrastructureError("Docker inspect Mounts map destination is inconsistent")
            entry["Destination"] = destination
            normalized.append(entry)
        return normalized
    raise InfrastructureError("Docker inspect Mounts has an unsupported shape")


def docker_source_variants(path: Path) -> set[str]:
    """Return the supplied host path in a comparison-safe spelling only.

    Docker Desktop path translations are intentionally not treated as aliases
    unless a run-specific observation establishes that mapping.
    """
    source = str(path.resolve(strict=True)).replace("\\?\\", "")
    normalized = source.replace("\\", "/")
    return {normalized.rstrip("/").casefold()}


def source_matches_expected(observed: Any, expected: Path) -> bool:
    if not isinstance(observed, str) or not observed:
        return False
    normalized = observed.replace("\\", "/").rstrip("/").casefold()
    return normalized in docker_source_variants(expected)


def mount_source_fingerprint(source: Any) -> str | None:
    if not isinstance(source, str) or not source:
        return None
    normalized = source.replace("\\", "/").casefold()
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()[:16]


def parse_executor_mountinfo(raw: str) -> dict[str, Any]:
    targets = {"/tests": [], "/results": [], "/tmp": []}
    for line in raw.splitlines():
        if " - " not in line:
            continue
        before, after = line.split(" - ", 1)
        before_fields, after_fields = before.split(), after.split()
        if len(before_fields) < 6 or len(after_fields) < 3:
            continue
        target = re.sub(r"\\([0-7]{3})", lambda match: chr(int(match.group(1), 8)), before_fields[4])
        if target not in targets:
            continue
        mount_options = set(before_fields[5].split(","))
        super_options = set(after_fields[2].split(","))
        options = mount_options | super_options
        size_values = [item[5:] for item in options if item.startswith("size=")]
        try:
            size_bytes = parse_tmpfs_size(size_values[0]) if len(size_values) == 1 else None
        except ValueError:
            size_bytes = None
        targets[target].append({
            "target": target,
            "filesystem": after_fields[0],
            "readOnly": "ro" in mount_options and "rw" not in mount_options,
            "readWrite": "rw" in mount_options and "ro" not in mount_options,
            "noexec": "noexec" in options,
            "nosuid": "nosuid" in options,
            "nodev": "nodev" in options,
            "sizeBytes": size_bytes,
        })
    return {
        target: entries[0] if len(entries) == 1 else None
        for target, entries in targets.items()
    } | {"counts": {target: len(entries) for target, entries in targets.items()}}


def executor_mountinfo_rejection(effective: dict[str, Any]) -> str | None:
    counts = effective.get("counts") or {}
    if any(counts.get(target) != 1 for target in ("/tests", "/results", "/tmp")):
        return "Effective mountinfo does not contain exactly one entry per required target"
    tests_mount, results_mount, tmp_mount = (effective.get(target) for target in ("/tests", "/results", "/tmp"))
    if not tests_mount or not tests_mount.get("readOnly"):
        return "Effective /tests mount is not proven read-only"
    if not results_mount or not results_mount.get("readWrite"):
        return "Effective /results mount is not proven writable"
    if (not tmp_mount or tmp_mount.get("filesystem") != "tmpfs"
            or not tmp_mount.get("readWrite") or tmp_mount.get("readOnly")
            or not tmp_mount.get("noexec") or not tmp_mount.get("nosuid")
            or not tmp_mount.get("nodev") or tmp_mount.get("sizeBytes") != 128 * 1024 * 1024):
        return "Effective /tmp tmpfs does not meet the required flags and size"
    return None


def executor_probe_matches(raw: str, assembly_sha256: str) -> bool:
    match = re.search(r"\b([0-9a-f]{64})\s+/tests/RealEstate\.Tests\.dll\s*$", raw.strip())
    return bool(match and match.group(1) == assembly_sha256)


def validate_executor_mounts(info: dict[str, Any], test_bin: Path, results_dir: Path,
                             evidence: dict[str, Any] | None = None) -> dict[str, Any]:
    diagnostic: dict[str, Any] = {
        "status": "IN PROGRESS",
        "expected": [
            {"destination": "/tests", "type": "bind", "readOnly": True,
             "sourceFingerprint": mount_source_fingerprint(str(test_bin.resolve(strict=True)))},
            {"destination": "/results", "type": "bind", "readOnly": False,
             "sourceFingerprint": mount_source_fingerprint(str(results_dir.resolve(strict=True)))},
            {"destination": "/tmp", "type": "tmpfs", "readOnly": False,
             "sizeBytes": 128 * 1024 * 1024, "noexec": True, "nosuid": True, "nodev": True},
        ],
        "declaredMounts": [],
        "observed": [],
        "effective": None,
        "checks": {},
        "dockerMountSummaryShape": type(info.get("Mounts")).__name__,
    }

    def record() -> None:
        if evidence is not None:
            evidence["executorMountDiagnostics"] = diagnostic

    def reject(reason: str) -> None:
        diagnostic["status"] = "FAIL"
        diagnostic["rejectionReason"] = reason
        record()
        raise InfrastructureError(reason)

    try:
        host = info.get("HostConfig") or {}
        raw_mounts = info.get("Mounts")
        if isinstance(raw_mounts, list):
            summary_items = [(None, item) for item in raw_mounts]
        elif isinstance(raw_mounts, dict) and isinstance(raw_mounts.get("Destination"), str):
            summary_items = [(None, raw_mounts)]
        elif isinstance(raw_mounts, dict):
            summary_items = [(key, item) for key, item in raw_mounts.items()]
        else:
            summary_items = []
        for mapped_destination, item in summary_items:
            if not isinstance(item, dict):
                diagnostic["observed"].append({"destination": mapped_destination,
                                               "entryType": type(item).__name__})
                continue
            destination = item.get("Destination", mapped_destination)
            source = item.get("Source")
            diagnostic["observed"].append({
                "destination": destination,
                "type": item.get("Type"),
                "readWrite": item.get("RW"),
                "sourceFingerprint": mount_source_fingerprint(source),
            })
        declared_for_evidence = host.get("Mounts")
        if isinstance(declared_for_evidence, list):
            for item in declared_for_evidence:
                if not isinstance(item, dict):
                    diagnostic["declaredMounts"].append({"entryType": type(item).__name__})
                    continue
                destination = item.get("Target")
                source = item.get("Source")
                expected_source = test_bin if destination == "/tests" else results_dir
                diagnostic["declaredMounts"].append({
                    "destination": destination,
                    "type": item.get("Type"),
                    "readOnlyFieldPresent": "ReadOnly" in item,
                    "readOnly": item.get("ReadOnly"),
                    "sourceFingerprint": mount_source_fingerprint(source),
                    "sourceMatchesExpected": source_matches_expected(source, expected_source)
                    if destination in ("/tests", "/results") else False,
                })
        diagnostic["checks"]["legacyBindsAbsent"] = "PASS" if host.get("Binds") in (None, []) else "FAIL"
        record()
        if host.get("Binds") not in (None, []):
            reject("Executor .NET has undeclared legacy bind mounts")
        declared_mounts = host.get("Mounts")
        if not isinstance(declared_mounts, list) or len(declared_mounts) != 2 \
                or any(not isinstance(item, dict) for item in declared_mounts):
            reject("Executor .NET declared bind mounts are absent or unexpected")
        declared_by_target = {item.get("Target"): item for item in declared_mounts}
        if (len(declared_by_target) != 2 or set(declared_by_target) != {"/tests", "/results"}
                or any(item.get("Type") != "bind" for item in declared_mounts)):
            reject("Executor .NET declared mount targets/types are unexpected")
        declared_tests = declared_by_target["/tests"]
        declared_results = declared_by_target["/results"]
        if declared_tests.get("ReadOnly") is not True:
            diagnostic["checks"]["testsDeclaredReadOnly"] = "FAIL"
            reject("Executor .NET /tests bind is not declared read-only")
        diagnostic["checks"]["testsDeclaredReadOnly"] = "PASS"
        if ("ReadOnly" in declared_results
                and declared_results.get("ReadOnly") is not False
                and declared_results.get("ReadOnly") is not None):
            diagnostic["checks"]["resultsDeclaredWritable"] = "FAIL"
            reject("Executor .NET /results bind is not declared writable")
        diagnostic["checks"]["resultsDeclaredWritable"] = "PASS"
        if not source_matches_expected(declared_tests.get("Source"), test_bin):
            diagnostic["checks"]["testsDeclaredSource"] = "FAIL"
            reject("Executor .NET /tests declared source does not match this run's assembly directory")
        diagnostic["checks"]["testsDeclaredSource"] = "PASS"
        if not source_matches_expected(declared_results.get("Source"), results_dir):
            diagnostic["checks"]["resultsDeclaredSource"] = "FAIL"
            reject("Executor .NET /results declared source does not match this run's evidence directory")
        diagnostic["checks"]["resultsDeclaredSource"] = "PASS"
        declared_tmpfs = host.get("Tmpfs") or {}
        if not isinstance(declared_tmpfs, dict) or set(declared_tmpfs) != {"/tmp"}:
            reject("Executor .NET declared tmpfs targets are unexpected")
        tmpfs_options = {item.strip().lower() for item in str(declared_tmpfs["/tmp"]).split(",")}
        tmpfs_sizes = [item[5:] for item in tmpfs_options if item.startswith("size=")]
        try:
            tmpfs_size = parse_tmpfs_size(tmpfs_sizes[0]) if len(tmpfs_sizes) == 1 else None
        except ValueError:
            tmpfs_size = None
        if (tmpfs_size != 128 * 1024 * 1024
                or not {"rw", "noexec", "nosuid", "nodev"}.issubset(tmpfs_options)):
            reject("Executor .NET declared tmpfs options diverge from the contract")

        observed = normalize_docker_mounts(raw_mounts)
        destinations: dict[str, dict[str, Any]] = {}
        for item in observed:
            destination = item.get("Destination")
            if not isinstance(destination, str) or destination in destinations:
                reject("Docker inspect Mounts has a missing or duplicate destination")
            destinations[destination] = item
        if not {"/tests", "/results"}.issubset(destinations) \
                or set(destinations) - {"/tests", "/results", "/tmp"}:
            reject("Docker inspect Mounts omits a required bind or reports an unauthorized target")
        tests_mount, results_mount = destinations["/tests"], destinations["/results"]
        for destination, mount, expected_type, expected_rw in (
            ("/tests", tests_mount, "bind", False),
            ("/results", results_mount, "bind", True),
        ):
            if mount.get("Type") != expected_type:
                diagnostic["checks"][f"{destination}DockerType"] = "FAIL"
                reject(f"Docker inspect Mounts reports an invalid type for {destination}")
            diagnostic["checks"][f"{destination}DockerType"] = "PASS"
            if mount.get("RW") is not expected_rw:
                diagnostic["checks"][f"{destination}DockerAccess"] = "FAIL"
                reject(f"Docker inspect Mounts reports an invalid access mode for {destination}")
            diagnostic["checks"][f"{destination}DockerAccess"] = "PASS"
            expected_source = test_bin if destination == "/tests" else results_dir
            if not source_matches_expected(mount.get("Source"), expected_source):
                diagnostic["checks"][f"{destination}DockerSource"] = "FAIL"
                reject(f"Docker inspect Mounts source for {destination} does not match this run")
            diagnostic["checks"][f"{destination}DockerSource"] = "PASS"
        if "/tmp" in destinations:
            if destinations["/tmp"].get("Type") != "tmpfs":
                diagnostic["checks"]["tmpfsDockerType"] = "FAIL"
                reject("Docker inspect Mounts reports a non-tmpfs /tmp mount")
            if destinations["/tmp"].get("RW") is not True:
                diagnostic["checks"]["tmpfsDockerAccess"] = "FAIL"
                reject("Docker inspect Mounts reports /tmp as non-writable")
            diagnostic["checks"]["tmpfsDockerType"] = "PASS"
            diagnostic["checks"]["tmpfsDockerAccess"] = "PASS"
        diagnostic["checks"]["declaredMounts"] = "PASS"
        diagnostic["checks"]["dockerMountSummary"] = "PASS"
        diagnostic["status"] = "PASS"
        record()
        return diagnostic
    except InfrastructureError as exc:
        if diagnostic["status"] != "FAIL":
            diagnostic["status"] = "FAIL"
            diagnostic["rejectionReason"] = str(exc)
            record()
        raise
    except Exception:
        reject("Executor .NET mount declaration could not be validated safely")


def validate_executor_effective_mounts(container_id: str, assembly_sha256: str,
                                       evidence: dict[str, Any] | None = None,
                                       write_probe_nonce: str | None = None) -> None:
    diagnostic = (evidence or {}).get("executorMountDiagnostics")
    if not isinstance(diagnostic, dict):
        diagnostic = {"status": "IN PROGRESS", "checks": {}, "observed": []}
    try:
        try:
            raw = docker(["exec", container_id, "cat", "/proc/self/mountinfo"], timeout=15)
        except Exception:
            diagnostic["status"] = "FAIL"
            diagnostic["rejectionReason"] = "Executor mountinfo could not be read"
            if evidence is not None:
                evidence["executorMountDiagnostics"] = diagnostic
            raise InfrastructureError(diagnostic["rejectionReason"])
        effective = parse_executor_mountinfo(raw)
        diagnostic["effective"] = effective
        mountinfo_rejection = executor_mountinfo_rejection(effective)
        if mountinfo_rejection:
            diagnostic["status"] = "FAIL"
            diagnostic["rejectionReason"] = mountinfo_rejection
            if evidence is not None:
                evidence["executorMountDiagnostics"] = diagnostic
            raise InfrastructureError(diagnostic["rejectionReason"])
        nonce = write_probe_nonce or uuid.uuid4().hex
        if not re.fullmatch(r"[0-9a-f]{32}", nonce):
            raise InfrastructureError("Executor write probe nonce is invalid")
        tests_probe = f"/tests/.araia-s05-write-probe-{nonce}"
        results_probe = f"/results/.araia-s05-write-probe-{nonce}"
        access_script = (
            "set -eu; "
            f"test ! -e '{tests_probe}'; "
            f"if (umask 077; set -C; : > '{tests_probe}') 2>/dev/null; then "
            f"rm -f -- '{tests_probe}'; exit 81; fi; "
            f"test ! -e '{results_probe}'; "
            f"(umask 077; set -C; printf '%s' '{nonce}' > '{results_probe}'); "
            f"test \"$(cat '{results_probe}')\" = '{nonce}'; "
            f"rm -- '{results_probe}'; test ! -e '{results_probe}'; "
            "sha256sum /tests/RealEstate.Tests.dll"
        )
        try:
            probe = docker([
                "exec", container_id, "sh", "-c",
                "set -eu; for f in RealEstate.Tests.dll RealEstate.Tests.deps.json "
                "RealEstate.Tests.runtimeconfig.json xunit.runner.visualstudio.testadapter.dll; do "
                "test -f /tests/$f && test -r /tests/$f; done; " + access_script,
            ], timeout=20).strip()
        except Exception:
            diagnostic["status"] = "FAIL"
            diagnostic["rejectionReason"] = "Executor file-access probe failed"
            if evidence is not None:
                evidence["executorMountDiagnostics"] = diagnostic
            raise InfrastructureError(diagnostic["rejectionReason"])
        if not executor_probe_matches(probe, assembly_sha256):
            diagnostic["status"] = "FAIL"
            diagnostic["rejectionReason"] = "Executor assembly is unreadable or differs from the preflight fingerprint"
            if evidence is not None:
                evidence["executorMountDiagnostics"] = diagnostic
            raise InfrastructureError(diagnostic["rejectionReason"])
        diagnostic["checks"].update({
            "assembliesPresentReadableAndFingerprintMatched": "PASS",
            "assembliesReadOnly": "PASS",
            "testsWriteDenied": "PASS",
            "resultsWriteReadAndRemove": "PASS",
            "effectiveMountinfo": "PASS",
        })
        diagnostic["status"] = "PASS"
        if evidence is not None:
            evidence["executorMountDiagnostics"] = diagnostic
    except InfrastructureError:
        if diagnostic.get("status") != "FAIL":
            diagnostic["status"] = "FAIL"
            diagnostic["rejectionReason"] = "Executor mount validation failed safely"
            if evidence is not None:
                evidence["executorMountDiagnostics"] = diagnostic
        raise
    except Exception:
        diagnostic["status"] = "FAIL"
        diagnostic["rejectionReason"] = "Executor effective mounts or access probe failed"
        if evidence is not None:
            evidence["executorMountDiagnostics"] = diagnostic
        raise InfrastructureError(diagnostic["rejectionReason"])


def dotnet_executor_create_args(name: str, owner: str, image_id: str,
                               network_name_or_id: str, test_bin: Path,
                               results_dir: Path) -> list[str]:
    return [
        "create", "--name", name,
        "--label", f"{OWNER_LABEL}={owner}", "--label", f"{KIND_LABEL}={EXECUTOR_KIND}",
        "--network", network_name_or_id,
        "--memory", str(EXECUTOR_MEMORY_BYTES), "--memory-swap", str(EXECUTOR_MEMORY_BYTES),
        "--cpus", "1", "--pids-limit", "128",
        "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=128m",
        "--mount", f"type=bind,source={test_bin},target=/tests,readonly",
        "--mount", f"type=bind,source={results_dir},target=/results",
        "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "--env", "DOTNET_NOLOGO=1",
        "--env", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1", "--env", "DOTNET_CLI_HOME=/tmp/dotnet-home",
        "--env", "HOME=/tmp/dotnet-home", "--env", "TMPDIR=/tmp",
        "--entrypoint", "/bin/sh", image_id, "-c", "while :; do sleep 3600; done",
    ]


def validate_dotnet_executor(container_id: str, expected_name: str, owner: str, image_id: str,
                             network_name: str, network_id: str,
                             test_bin: Path, results_dir: Path, assembly_sha256: str,
                             evidence: dict[str, Any] | None = None) -> dict[str, Any]:
    if evidence is not None:
        evidence.setdefault("executorMountDiagnostics", {
            "status": "NOT RUN",
            "expected": [
                {"destination": "/tests", "type": "bind", "readOnly": True,
                 "sourceFingerprint": mount_source_fingerprint(str(test_bin.resolve(strict=True)))},
                {"destination": "/results", "type": "bind", "readOnly": False,
                 "sourceFingerprint": mount_source_fingerprint(str(results_dir.resolve(strict=True)))},
                {"destination": "/tmp", "type": "tmpfs", "readOnly": False,
                 "sizeBytes": 128 * 1024 * 1024, "noexec": True, "nosuid": True, "nodev": True},
            ],
            "observed": [],
            "effective": None,
            "checks": {},
            "rejectionReason": "Executor identity/resource preflight did not reach mount validation",
        })
    info = inspect_container(container_id)
    labels = info.get("Config", {}).get("Labels") or {}
    if (info.get("Id") != container_id or info.get("Name") != "/" + expected_name
        or labels.get(OWNER_LABEL) != owner or labels.get(KIND_LABEL) != EXECUTOR_KIND
        or info.get("Image") != image_id or not info.get("State", {}).get("Running")):
        raise InfrastructureError("Executor .NET não corresponde à identidade, imagem ou ownership esperados")
    host = info.get("HostConfig") or {}
    if (host.get("NetworkMode") not in (network_name, network_id)
        or host.get("Memory") != EXECUTOR_MEMORY_BYTES
        or host.get("MemorySwap") != EXECUTOR_MEMORY_BYTES
        or host.get("NanoCpus") != 1_000_000_000
        or host.get("PidsLimit") != 128):
        raise InfrastructureError("Limites de recursos/rede do executor .NET divergem do contrato")
    if (host.get("Privileged") or host.get("CapAdd") or host.get("Devices")
        or host.get("ReadonlyRootfs") is not True
        or "ALL" not in (host.get("CapDrop") or [])
        or "no-new-privileges:true" not in (host.get("SecurityOpt") or [])):
        raise InfrastructureError("Executor .NET recebeu privilégio ou filesystem diferente do contrato")
    validate_executor_mounts(info, test_bin, results_dir, evidence)
    validate_executor_effective_mounts(container_id, assembly_sha256, evidence)
    net_settings = info.get("NetworkSettings") or {}
    if "Ports" not in net_settings:
        raise InfrastructureError("NetworkSettings.Ports ausente; exposição não pode ser descartada")
    assert_zero_published_ports(host, net_settings["Ports"], evidence, "executor .NET")
    networks = net_settings.get("Networks") or {}
    if list(networks.keys()) != [network_name]:
        raise InfrastructureError("Executor .NET conectado a uma rede inesperada")
    return info


def validate_two_container_network(postgres_id: str, executor_id: str, owner: str,
                                   network_name: str, network_id: str) -> None:
    network = json.loads(docker(["network", "inspect", network_id], timeout=15))[0]
    labels = network.get("Labels") or {}
    members = set((network.get("Containers") or {}).keys())
    if (network.get("Id") != network_id or network.get("Name") != network_name
        or labels.get(OWNER_LABEL) != owner or labels.get(KIND_LABEL) != "s05-postgres-gate"
        or network.get("Internal") is not True or members != {postgres_id, executor_id}):
        raise InfrastructureError("Rede interna não contém exclusivamente os dois containers autorizados")
    pg = inspect_container(postgres_id)
    aliases = (((pg.get("NetworkSettings") or {}).get("Networks") or {}).get(network_name) or {}).get("Aliases") or []
    if POSTGRES_NETWORK_ALIAS not in aliases:
        raise InfrastructureError("Alias interno PostgreSQL não foi confirmado na rede")
    for cid in (postgres_id, executor_id):
        info = inspect_container(cid)
        if (info.get("Id") != cid
            or (info.get("Config", {}).get("Labels") or {}).get(OWNER_LABEL) != owner
            or info.get("HostConfig", {}).get("NetworkMode") not in (network_name, network_id)):
            raise InfrastructureError("Identidade/ownership do membro da rede mudou")


def parse_tmpfs_size(value: str) -> int:
 raw = value.strip().lower()
 if raw.startswith("size="):
  raw = raw[5:]
 match = re.fullmatch(r"(\d+)([kmgt]?)", raw)
 if not match:
  raise ValueError("invalid tmpfs size option")
 scale = {"": 1, "k": 1024, "m": 1024**2, "g": 1024**3, "t": 1024**4}[match.group(2)]
 return int(match.group(1)) * scale


def validate_pgdata_tmpfs(info: dict[str, Any], container_id: str) -> dict[str, Any]:
 env = info.get("Config", {}).get("Env") or []
 pgdata_values = [entry.split("=", 1)[1] for entry in env
                  if isinstance(entry, str) and entry.startswith("PGDATA=")]
 effective_pgdata = pgdata_values[0] if len(pgdata_values) == 1 else None
 raw_host_tmpfs = info.get("HostConfig", {}).get("Tmpfs")
 host_tmpfs = raw_host_tmpfs if isinstance(raw_host_tmpfs, dict) else {}
 raw_options = host_tmpfs.get(effective_pgdata) if effective_pgdata else None
 host_option_values = [item.strip().lower() for item in raw_options.split(",")] if isinstance(raw_options, str) else []
 host_size_values = [item for item in host_option_values if item.startswith("size=")]
 diagnostic: dict[str, Any] = {
  "expectedPgdata": PGDATA,
  "effectivePgdata": effective_pgdata,
  "hostTmpfs": {key: value for key, value in host_tmpfs.items() if key == PGDATA and isinstance(value, str)},
  "inspectMountsShape": "list" if isinstance(info.get("Mounts"), list) else type(info.get("Mounts")).__name__,
 }
 if (effective_pgdata != PGDATA or not isinstance(raw_host_tmpfs, dict)
     or set(host_tmpfs) != {PGDATA} or len(host_size_values) != 1):
  raise PgdataTmpfsError("PGDATA and HostConfig.Tmpfs do not match the required temporary path", diagnostic)
 try:
  host_size = parse_tmpfs_size(host_size_values[0])
 except ValueError as exc:
  raise PgdataTmpfsError("HostConfig.Tmpfs size is invalid", diagnostic) from exc
 if (host_size != PGDATA_TMPFS_SIZE_BYTES
     or not {"rw", "noexec", "nosuid"}.issubset(set(host_option_values))):
  raise PgdataTmpfsError("HostConfig.Tmpfs options diverge from the PGDATA contract", diagnostic)
 # Docker's Mounts summary does not consistently represent HostConfig.Tmpfs.
 # It is diagnostic only; the live mount table below is the proof of effect.
 mountinfo = docker(["exec", container_id, "cat", "/proc/self/mountinfo"], timeout=10)
 effective_mounts: list[dict[str, Any]] = []
 for line in mountinfo.splitlines():
  if " - " not in line:
   continue
  before, after = line.split(" - ", 1)
  before_fields = before.split()
  after_fields = after.split()
  if len(before_fields) < 6 or len(after_fields) < 3:
   continue
  mountpoint = re.sub(
   r"\\([0-7]{3})", lambda match: chr(int(match.group(1), 8)), before_fields[4]
  )
  if mountpoint != PGDATA:
   continue
  mount_options = set(before_fields[5].split(","))
  super_options = set(after_fields[2].split(","))
  size_values = [item[5:] for item in super_options if item.startswith("size=")]
  try:
   size_bytes = parse_tmpfs_size(size_values[0]) if len(size_values) == 1 else None
  except ValueError:
   size_bytes = None
  effective_mounts.append({
   "mountpoint": mountpoint,
   "filesystem": after_fields[0],
   "readWrite": "rw" in mount_options and "ro" not in mount_options,
   "noexec": "noexec" in mount_options,
   "nosuid": "nosuid" in mount_options,
   "sizeBytes": size_bytes,
  })
 effective = effective_mounts[0] if len(effective_mounts) == 1 else None
 diagnostic["effectiveMountCount"] = len(effective_mounts)
 diagnostic["effectiveMount"] = effective
 if (not effective or effective["filesystem"] != "tmpfs" or not effective["readWrite"]
     or not effective["noexec"] or not effective["nosuid"]
     or effective["sizeBytes"] != PGDATA_TMPFS_SIZE_BYTES):
  raise PgdataTmpfsError("mountinfo does not prove the effective PGDATA tmpfs contract", diagnostic)
 return diagnostic


def inspect_container(container_id: str) -> dict[str, Any]:
 return json.loads(docker(["inspect", container_id], timeout=20))[0]


def run_dotnet_mount_probe() -> int:
    report_root = ensure_results_root()
    run_id = uuid.uuid4().hex
    owner = str(uuid.uuid4())
    name = EXECUTOR_PREFIX + "probe-" + run_id[:12]
    out = report_root / f"s05-dotnet-mount-probe-{run_id}"
    image_id: str | None = None
    container_id: str | None = None
    create_attempted = False
    local_context_verified = False
    errors: list[str] = []
    interrupted = False
    receipt: dict[str, Any] = {
        "runId": run_id, "owner": owner, "startedAtUtc": utc_now(),
        "mode": "DOTNET_MOUNT_PROBE", "result": "NOT RUN",
        "postgres": "NOT RUN", "migrations": "NOT RUN", "testExecution": "NOT RUN",
        "mountValidation": {"status": "NOT RUN"},
        "cleanup": {"status": "NOT RUN"},
    }
    out.mkdir(exist_ok=False)
    marker = {"owner": owner, "runId": run_id, "createdAtUtc": utc_now()}
    output_json(out / ".ownership.json", marker)
    output_json(out / "receipt.json", receipt)
    try:
        if not shutil.which("docker"):
            raise InfrastructureError("Docker CLI indisponÃ­vel")
        global PINNED_DOCKER_CONTEXT
        PINNED_DOCKER_CONTEXT = inspect_network_endpoint()
        local_context_verified = True
        if database_environment_names():
            raise InfrastructureError("External PostgreSQL connection environment detected; probe refused")
        assert_no_prior_gate_resources()
        docker_info = parse_docker_inspect_object(
            docker(["info", "--format", "{{json .}}"], timeout=20), "docker info",
        )
        free = free_memory_bytes()
        receipt["hostFreeMemoryBytes"] = free
        receipt["dockerMemoryBytes"] = docker_info.get("MemTotal")
        if free is None or free < MIN_HOST_FREE_BYTES:
            raise InfrastructureError("Free host memory is unknown or below the 3 GiB probe threshold")
        if not isinstance(docker_info.get("MemTotal"), int) or docker_info["MemTotal"] < 3 * 1024**3:
            raise InfrastructureError("Docker Desktop reports less than 3 GiB total memory")
        sdk_image = parse_docker_inspect_object(
            docker(["image", "inspect", DOTNET_SDK_IMAGE_TAG, "--format", "{{json .}}"], timeout=20),
            "docker image inspect",
        )
        image_id = sdk_image.get("Id")
        repo_digests = sdk_image.get("RepoDigests") or []
        if (DOTNET_SDK_IMAGE_TAG not in (sdk_image.get("RepoTags") or [])
                or not isinstance(image_id, str) or not image_id.startswith("sha256:")
                or sdk_image.get("Os") != "linux"
                or not architectures_compatible(sdk_image.get("Architecture"), docker_info.get("Architecture"))
                or not any(str(d).startswith("mcr.microsoft.com/dotnet/sdk@sha256:") for d in repo_digests)):
            raise InfrastructureError("Official .NET SDK 10 image identity or architecture validation failed")
        receipt["sdkImage"] = {
            "tag": DOTNET_SDK_IMAGE_TAG, "imageId": image_id, "repoDigests": repo_digests,
            "os": sdk_image.get("Os"), "architecture": sdk_image.get("Architecture"),
        }
        test_bin = ROOT / "tests" / "RealEstate.Tests" / "bin" / "Debug" / "net10.0"
        test_dll = test_bin / "RealEstate.Tests.dll"
        required = (
            test_dll, test_bin / "RealEstate.Tests.deps.json",
            test_bin / "RealEstate.Tests.runtimeconfig.json",
            test_bin / "xunit.runner.visualstudio.testadapter.dll",
        )
        if is_link_or_reparse(test_bin) or not test_bin.is_dir() or any(
                is_link_or_reparse(path) or not path.is_file() for path in required):
            raise InfrastructureError("Existing net10.0 test assemblies are absent or unsafe")
        assembly_sha256 = hashlib.sha256(test_dll.read_bytes()).hexdigest()
        results_dir = out / "test-results"
        results_dir.mkdir(exist_ok=False)
        if is_link_or_reparse(results_dir):
            raise InfrastructureError("Probe results directory is a link/reparse point")
        create_args = dotnet_executor_create_args(name, owner, image_id, "none", test_bin, results_dir)
        create_attempted = True
        created_id = docker(create_args, timeout=45).strip()
        if not re.fullmatch(r"[0-9a-f]{64}", created_id):
            raise InfrastructureError("Docker returned an invalid .NET mount probe ID")
        container_id = created_id
        receipt["resource"] = {"type": "container", "id": container_id, "name": name}
        output_json(out / "receipt.json", receipt)
        docker(["start", container_id], timeout=30)
        info = inspect_container(container_id)
        labels = (info.get("Config", {}).get("Labels") or {})
        host = info.get("HostConfig") or {}
        net_settings = info.get("NetworkSettings") or {}
        if "Ports" not in net_settings:
            raise InfrastructureError("NetworkSettings.Ports absent; probe exposure cannot be discarded")
        if (info.get("Id") != container_id or info.get("Name") != "/" + name
                or labels.get(OWNER_LABEL) != owner or labels.get(KIND_LABEL) != EXECUTOR_KIND
                or info.get("Image") != image_id or not info.get("State", {}).get("Running")
                or host.get("NetworkMode") != "none"
                or host.get("Memory") != EXECUTOR_MEMORY_BYTES
                or host.get("MemorySwap") != EXECUTOR_MEMORY_BYTES
                or host.get("NanoCpus") != 1_000_000_000 or host.get("PidsLimit") != 128
                or host.get("Privileged") or host.get("CapAdd") or host.get("Devices")
                or host.get("ReadonlyRootfs") is not True
                or "ALL" not in (host.get("CapDrop") or [])
                or "no-new-privileges:true" not in (host.get("SecurityOpt") or [])):
            raise InfrastructureError("Probe container identity, network, or resource security diverges")
        validate_executor_mounts(info, test_bin, results_dir, receipt)
        assert_zero_published_ports(host, net_settings.get("Ports"), receipt, "executor probe")
        validate_executor_effective_mounts(container_id, assembly_sha256, receipt)
        receipt["mountValidation"]["status"] = "PASS"
        receipt["result"] = "PASS"
    except KeyboardInterrupt:
        interrupted = True
        receipt["result"] = "INTERRUPTED"
        receipt["primaryFailure"] = {"type": "INTERRUPTED", "detail": "Probe interrupted by user"}
    except Exception as exc:
        receipt["result"] = "FAIL"
        receipt["primaryFailure"] = {
            "type": "INFRASTRUCTURE" if isinstance(exc, InfrastructureError) else "SETUP",
            "detail": scrub(str(exc), (str(ROOT), str(ROOT).replace("\\", "/"))),
        }
        if receipt["mountValidation"].get("status") == "NOT RUN":
            diagnostics = receipt.get("executorMountDiagnostics")
            if isinstance(diagnostics, dict) and diagnostics.get("status") == "FAIL":
                receipt["mountValidation"] = {"status": "FAIL", "diagnostics": diagnostics}
            else:
                receipt["mountValidation"] = {"status": "NOT RUN", "reason": "Earlier probe prerequisite failed"}
    finally:
        states, errors = cleanup_run_resources([{
            "key": "dotnetProbe", "type": "container", "id": container_id,
            "createAttempted": create_attempted, "name": name, "owner": owner,
            "kind": EXECUTOR_KIND, "imageId": image_id,
        }], local_context_verified=local_context_verified)
        if "INTERRUPTED" in states.values():
            interrupted = True
            receipt["result"] = "INTERRUPTED"
        receipt["cleanup"] = {"status": cleanup_result(errors, states), "resources": states,
                              "errors": errors, "resourcesRemoved": cleanup_result(errors, states) == "PASS"}
        if receipt["cleanup"]["status"] != "PASS" and not interrupted:
            receipt["result"] = "FAIL"
        receipt["overallStatus"] = "PASS" if (
            receipt["result"] == "PASS" and receipt["cleanup"]["status"] == "PASS"
        ) else "FAIL"
        receipt["finishedAtUtc"] = utc_now()
        try:
            output_json(out / "receipt.json", receipt)
        except KeyboardInterrupt:
            interrupted = True
            receipt["result"] = "INTERRUPTED"
            receipt["cleanup"]["status"] = "FAIL"
            receipt["overallStatus"] = "FAIL"
            receipt["cleanup"]["receiptWriteFailure"] = "interrupted while writing receipt"
        except Exception as exc:
            receipt["cleanup"]["status"] = "FAIL"
            receipt["overallStatus"] = "FAIL"
            receipt["cleanup"]["receiptWriteFailure"] = scrub(
                str(exc), (str(ROOT), str(ROOT).replace("\\", "/")),
            )
    receipt_path = out / "receipt.json"
    if interrupted or receipt["cleanup"]["status"] != "PASS" or receipt["result"] != "PASS":
        print(f"FAIL: dotnet mount probe {receipt['result']}; cleanup {receipt['cleanup']['status']}; recibo: {receipt_path}",
              file=sys.stderr)
        return 130 if interrupted else 2
    print(f"PASS: dotnet mount probe; recibo: {receipt_path}")
    return 0


def validate_owned_container(container_id: str, owner: str, image_id: str, network_name: str,
                             network_id: str, *, evidence: dict[str, Any] | None = None) -> dict[str, Any]:
 info = inspect_container(container_id)
 labels = info.get("Config", {}).get("Labels") or {}
 if info.get("Id") != container_id or labels.get(OWNER_LABEL) != owner or labels.get(KIND_LABEL) != "s05-postgres-gate":
  raise GateError("Container não corresponde à identidade/ownership deste run")
 if info.get("Image") != image_id or not info.get("State", {}).get("Running"):
  raise GateError("Container não usa a imagem validada ou não está ativo")
 host = info.get("HostConfig", {})
 if host.get("NetworkMode") not in (network_name, network_id) or host.get("Memory") != MEMORY_BYTES or host.get("NanoCpus") != 2_000_000_000:
  raise GateError("Limites ou rede do container divergem do contrato")
 if host.get("MemorySwap") != MEMORY_BYTES or host.get("PidsLimit") != 256:
  raise GateError("Limite de swap/PIDs diverge do contrato")
 if host.get("Privileged") or host.get("CapAdd") or host.get("Devices"):
  raise GateError("Container recebeu privilégio/capacidade/dispositivo inesperado")
 mounts = info.get("Mounts")
 if not isinstance(mounts, list) or any(not isinstance(mount, dict) for mount in mounts):
  raise GateError("Docker inspect mounts do not prove the secret bind and allowed mount set")
 info["_gatePgdataTmpfsDiagnostic"] = validate_pgdata_tmpfs(info, container_id)
 if evidence is not None:
  evidence["pgdataTmpfsDiagnostic"] = info["_gatePgdataTmpfsDiagnostic"]
 if any(m.get("Type") == "volume" for m in mounts):
  raise GateError("Volume persistente inesperado no container")
 secret_mounts = [m for m in mounts if m.get("Destination") == "/run/secrets/postgres_password"]
 if len(secret_mounts) != 1 or secret_mounts[0].get("Type") != "bind" or secret_mounts[0].get("RW") is not False:
  raise GateError("Senha inicial temporária não está em bind mount somente leitura")
 if any(m.get("Destination") not in (PGDATA, "/run/secrets/postgres_password") for m in mounts):
  raise GateError("Mount não previsto no container")
 network_settings = info.get("NetworkSettings")
 if not isinstance(network_settings, dict) or "Ports" not in network_settings:
  raise GateError("NetworkSettings.Ports is absent; published exposure cannot be ruled out")
 network_ports = network_settings["Ports"]
 assert_zero_published_ports(host, network_ports, evidence, "PostgreSQL")
 networks = info.get("NetworkSettings", {}).get("Networks", {})
 if list(networks.keys()) != [network_name]:
  raise GateError("Container conectado a rede inesperada")
 env = info.get("Config", {}).get("Env") or []
 if "POSTGRES_PASSWORD_FILE=/run/secrets/postgres_password" not in env or any(x.startswith("POSTGRES_PASSWORD=") for x in env):
  raise GateError("Inicialização não usa arquivo temporário de senha")
 return info


def run_gate(*, phase_a_only: bool = False, only_test_class: str | None = None) -> int:
 report_root = ensure_results_root()
 run_id = uuid.uuid4().hex
 owner = str(uuid.uuid4())
 name = CONTAINER_PREFIX + run_id[:12]
 executor_name = EXECUTOR_PREFIX + run_id[:12]
 network_name = NETWORK_PREFIX + run_id[:12]
 out = report_root / f"s05-pg-gate-{run_id}"
 out.mkdir(exist_ok=False)
 marker = {"owner": owner, "runId": run_id, "createdAtUtc": utc_now()}
 output_json(out / ".ownership.json", marker)

 phase_a: dict[str, Any] = {"status": "NOT RUN", "scenarios": []}
 phase_b: dict[str, Any] = {"status": "NOT RUN", "scenarios": []}
 cleanup: dict[str, Any] = {"status": "NOT RUN"}
 receipt: dict[str, Any] = {
   "runId": run_id, "owner": owner, "startedAtUtc": utc_now(),
   "mode": "TARGETED_TEST" if only_test_class else "RUN",
   "postgresTag": IMAGE_TAG, "phaseAOnly": phase_a_only,
   "selectedTestClasses": [only_test_class] if only_test_class else list(DB_TEST_CLASSES), "migrations": [],
  "resourcesCreated": [], "phaseA": phase_a, "phaseB": phase_b, "cleanup": cleanup,
 }
 output_json(out / "receipt.json", receipt)

 container_id: str | None = None
 executor_id: str | None = None
 network_id: str | None = None
 container_create_attempted = False
 executor_create_attempted = False
 network_create_attempted = False
 cleanup_resources: dict[str, str] = {}
 image_id: str | None = None
 executor_image_id: str | None = None
 local_context_verified = False
 test_results_dir: Path | None = None
 admin_password = secrets.token_urlsafe(36)
 runtime_password = secrets.token_urlsafe(36)
 secret_values = (admin_password, runtime_password)
 primary_error: str | None = None
 primary_error_type: str | None = None
 interrupted = False
 current_stage = "preflight"
 pending_test_classes: list[str] = []
 try:
  if not shutil.which("docker"):
   raise GateError("Docker CLI indisponível")
  global PINNED_DOCKER_CONTEXT
  PINNED_DOCKER_CONTEXT = inspect_network_endpoint()
  local_context_verified = True
  migrations = migration_manifest()
  receipt["migrations"] = migrations
  free = free_memory_bytes()
  receipt["hostFreeMemoryBytesAtStart"] = free
  if free is None or free < MIN_HOST_FREE_BYTES:
   raise GateError("Free host memory is unknown or below the 3 GiB minimum")
  if database_environment_names():
   raise GateError("External PostgreSQL connection environment detected; run refused")
  assert_no_prior_gate_resources()
  docker_info = parse_docker_inspect_object(
       docker(["info", "--format", "{{json .}}"], timeout=20), "docker info"
   )
  docker_memory = docker_info.get("MemTotal")
  receipt["dockerHostMemoryBytes"] = docker_memory
  if not isinstance(docker_memory, int) or docker_memory < 3 * 1024 * 1024 * 1024:
    raise GateError("Docker Desktop reports less than 3 GiB total memory")
  image = parse_docker_inspect_object(
       docker(["image", "inspect", IMAGE_TAG, "--format", "{{json .}}"], timeout=20),
       "docker image inspect",
   )
  if IMAGE_TAG not in (image.get("RepoTags") or []):
   raise GateError("Imagem PostgreSQL 17 n?o est? dispon?vel localmente com a tag esperada")
  image_id = image.get("Id")
  if (not image_id or not image_id.startswith("sha256:")
      or image.get("Os") != "linux"
        or not architectures_compatible(image.get("Architecture"), docker_info.get("Architecture"))):
   raise GateError("Identidade ou arquitetura da imagem PostgreSQL inv?lida")
  receipt["imageRepoDigests"] = image.get("RepoDigests") or []
  sdk_image = parse_docker_inspect_object(
       docker(["image", "inspect", DOTNET_SDK_IMAGE_TAG, "--format", "{{json .}}"], timeout=20),
       "docker image inspect",
   )
  sdk_repo_digests = sdk_image.get("RepoDigests") or []
  executor_image_id = sdk_image.get("Id")
  if (DOTNET_SDK_IMAGE_TAG not in (sdk_image.get("RepoTags") or [])
      or not isinstance(executor_image_id, str) or not executor_image_id.startswith("sha256:")
      or sdk_image.get("Os") != "linux"
        or not architectures_compatible(sdk_image.get("Architecture"), docker_info.get("Architecture"))
      or not any(str(digest).startswith("mcr.microsoft.com/dotnet/sdk@sha256:") for digest in sdk_repo_digests)):
    raise GateError("Official .NET SDK 10 image, digest, or architecture validation failed")
  test_artifacts = prepare_test_artifacts()
  receipt["testExecutorPreflight"] = {
   "status": "PASS", "imageTag": DOTNET_SDK_IMAGE_TAG, "imageId": executor_image_id,
   "repoDigests": sdk_repo_digests, "os": sdk_image.get("Os"),
   "architecture": sdk_image.get("Architecture"), "artifacts": test_artifacts,
   "hostDatabaseEnvironmentAbsent": True,
  }
  test_bin = ROOT / "tests" / "RealEstate.Tests" / "bin" / "Debug" / "net10.0"
  test_results_dir = out / "test-results"
  test_results_dir.mkdir(exist_ok=False)
  if is_link_or_reparse(test_results_dir):
   raise GateError("Diret?rio de resultados n?o pode ser link/reparse point")
  # Use the immutable local image ID, not the mutable tag, for container creation.
  network_create_attempted = True
  created_network_id = docker([
   "network", "create", "--internal", "--label", f"{OWNER_LABEL}={owner}",
   "--label", f"{KIND_LABEL}=s05-postgres-gate", network_name,
  ], timeout=30).strip()
  if not re.fullmatch(r"[0-9a-f]{64}", created_network_id):
   raise GateError("Docker returned an invalid network ID")
  network_id = created_network_id
  receipt["resourcesCreated"].append({"type": "network", "id": network_id, "name": network_name})
  with tempfile.TemporaryDirectory(prefix="araia-s05-pg-secret-") as secret_dir:
   secret_root = Path(secret_dir)
   secret_file = secret_root / "postgres_password"
   ddl_password = secrets.token_urlsafe(36)
   secret_values = secret_values + (ddl_password,)
   database = f"araia_s05_{run_id[:12]}"
   secret_file.write_text(admin_password, encoding="ascii")
   ddl_passfile = secret_root / "erp_ddl.pgpass"
   ddl_passfile.write_text(f"127.0.0.1:5432:{database}:erp_ddl:{ddl_password}\n", encoding="ascii")
   runtime_passfile = secret_root / "erp_runtime.pgpass"
   runtime_passfile.write_text(f"127.0.0.1:5432:{database}:erp_runtime:{runtime_password}\n", encoding="ascii")
   try:
    os.chmod(secret_file, 0o600)
   except OSError:
    pass
   create_args = [
    "create", "--name", name,
    "--label", f"{OWNER_LABEL}={owner}", "--label", f"{KIND_LABEL}=s05-postgres-gate",
    "--network", network_id, "--network-alias", POSTGRES_NETWORK_ALIAS,
    "--memory", str(MEMORY_BYTES), "--memory-swap", str(MEMORY_BYTES), "--cpus", "2",
    "--pids-limit", "256",
    "--tmpfs", f"{PGDATA}:rw,noexec,nosuid,size=768m",
    "--mount", f"type=bind,source={secret_file},target=/run/secrets/postgres_password,readonly",
   ]
   create_args.extend([
    "--env", "POSTGRES_PASSWORD_FILE=/run/secrets/postgres_password",
    "--env", "POSTGRES_HOST_AUTH_METHOD=scram-sha-256",
    "--env", f"POSTGRES_DB=araia_s05_{run_id[:12]}",
    image_id,
   ])
   container_create_attempted = True
   created = docker(create_args, timeout=30).strip()
   if not re.fullmatch(r"[0-9a-f]{64}", created):
    raise GateError("Docker returned an invalid PostgreSQL container ID")
   container_id = created
   receipt["resourcesCreated"].append({"type": "container", "id": container_id, "name": name})
   docker(["start", container_id], timeout=30)
   container_info = validate_owned_container(container_id, owner, image_id, network_name, network_id,
                                            evidence=receipt)
   container_info.pop("_gatePgdataTmpfsDiagnostic", None)
   network_info = json.loads(docker(["network", "inspect", network_id], timeout=15))[0]
   if not network_info.get("Internal") or (network_info.get("Labels") or {}).get(OWNER_LABEL) != owner:
    raise GateError("Rede não está isolada/interna ou ownership diverge")
   if set((network_info.get("Containers") or {}).keys()) != {container_id}:
    raise GateError("A rede contém container alheio ao run")
   version_output = docker(["exec", container_id, "postgres", "--version"], timeout=10)
   if not re.search(r"\b17(?:\.|\s)", version_output):
    raise GateError(f"Major PostgreSQL inesperada: {version_output}")
   for _ in range(80):
    ready = invoke([shutil.which("docker") or "docker", "exec", container_id,
                    "pg_isready", "-U", "postgres", "-d", f"araia_s05_{run_id[:12]}"], timeout=5)
    if ready.returncode == 0:
     break
    time.sleep(0.25)
   else:
    raise GateError("PostgreSQL não ficou pronto no tempo limite")

   docker(["cp", str(ddl_passfile), f"{container_id}:/run/erp_ddl.pgpass"], timeout=20,
          secrets_to_hide=secret_values)
   docker(["cp", str(runtime_passfile), f"{container_id}:/run/erp_runtime.pgpass"], timeout=20,
          secrets_to_hide=secret_values)
   docker(["exec", "-u", "0", container_id, "chmod", "0600", "/run/erp_ddl.pgpass"], timeout=10)
   docker(["exec", "-u", "0", container_id, "chmod", "0600", "/run/erp_runtime.pgpass"], timeout=10)
   role_sql = (
    f"CREATE ROLE erp_ddl LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD {sql_literal(ddl_password)};\n"
    f"CREATE ROLE erp_runtime LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD {sql_literal(runtime_password)};\n"
    f"GRANT CONNECT, CREATE ON DATABASE {database} TO erp_ddl;\n"
    f"GRANT CONNECT ON DATABASE {database} TO erp_runtime;\n"
   )
   psql(container_id, database, role_sql, secrets_to_hide=secret_values + (ddl_password,))
   for number, path in migration_files():
    current_stage = f"migration-{number:03d}"
    started = utc_now()
    sql = path.read_text(encoding="utf-8-sig")
    try:
     psql(container_id, database, sql, timeout=120, password_file="/run/erp_ddl.pgpass", username="erp_ddl")
     phase_a["scenarios"].append({"id": f"migration-{number:03d}", "status": "PASS",
                                  "startedAtUtc": started, "finishedAtUtc": utc_now(),
                                  "file": path.name,
                                  "sha256": next(x["sha256"] for x in migrations if x["version"] == number)})
    except Exception as exc:
     error_type = "INFRASTRUCTURE" if isinstance(exc, InfrastructureError) else "MIGRATION"
     phase_a["scenarios"].append({"id": f"migration-{number:03d}", "status": "FAIL", "failureType": error_type,
                                  "startedAtUtc": started, "finishedAtUtc": utc_now(),
                                  "file": path.name, "detail": scrub(str(exc), secret_values)})
     for remaining in range(number + 1, 33):
      phase_a["scenarios"].append({"id": f"migration-{remaining:03d}", "status": "NOT RUN",
                                   "reason": "Migration anterior falhou"})
     if isinstance(exc, InfrastructureError):
      raise InfrastructureError(f"Infraestrutura falhou durante migration {number:03d}") from exc
     raise MigrationError(f"Fase A parou na migration {number:03d}") from exc
    output_json(out / "receipt.json", receipt)
   current_stage = "runtime-authentication"
   try:
    runtime_auth = psql(container_id, database, runtime_authentication_sql(),
                        password_file="/run/erp_runtime.pgpass", username="erp_runtime", json_result=True)
   except Exception as exc:
    runtime_auth = {"authenticated": False, "detail": scrub(str(exc), secret_values)}
   current_stage = "structural-diagnostic"
   try:
    preflight = psql(container_id, database, phase_a_preflight_sql(), json_result=True)
   except Exception as exc:
    raise DiagnosticError(f"Pré-diagnóstico estrutural falhou: {scrub(str(exc), secret_values)}") from exc
   missing_objects = preflight.get("missingObjects") or []
   if missing_objects:
    structural = {
     "database": database, "runtimeRole": {"exists": True, "canLogin": True, "superuser": False, "bypassRls": False},
     "ddlRole": {"exists": True, "canLogin": True, "superuser": False, "bypassRls": False},
     "runtimeConnect": True, "ddlConnect": True, "ddlCreate": True, "schemaUsage": False,
     "runtimeAuthentication": runtime_auth, "versions": [], "tables": [],
     "tablePrivileges": [], "columnPrivileges": [], "forbiddenPrivileges": [],
     "appendOnlyColumnUpdatesAbsent": [], "constraintsValidated": False, "foreignKeys": [],
     "actionAppointmentUnique": False, "eventTriggers": [], "pendingFollowupIndex": False,
     "missingObjects": missing_objects,
    }
   else:
    try:
     structural = psql(container_id, database, phase_a_sql(), json_result=True)
    except Exception as exc:
     raise DiagnosticError(f"Diagnóstico estrutural falhou: {scrub(str(exc), secret_values)}") from exc
    structural["runtimeAuthentication"] = runtime_auth
   phase_a["structural"] = structural
   blockers = phase_a_blockers(structural)
   phase_a["blockers"] = blockers
   phase_a["status"] = "PASS" if not blockers else "FAIL"
   phase_a["scenarios"].append({"id": "roles-privileges-rls-integrity", "status": phase_a["status"],
                                "blockers": blockers})
   if phase_a_only:
    phase_b["status"] = "NOT RUN"
    phase_b["scenarios"].append({"id": "functional-suite", "status": "NOT RUN",
                                 "reason": "Modo somente Fase A solicitado"})
   elif blockers:
    phase_b["status"] = "SKIP"
    phase_b["scenarios"].append({"id": "functional-suite", "status": "SKIP",
                                 "reason": "Fase A contem bloqueios relevantes"})
    for class_name in DB_TEST_CLASSES:
     phase_b["scenarios"].append({"id": class_name, "status": "NOT RUN",
                                  "reason": "Fase A bloqueou a validacao comportamental"})
   else:
    current_stage = "pre-phase-b-revalidation"
    validate_gate_resources(container_id, owner, image_id, network_name, network_id,
                            evidence=receipt)
    found, missing = discover_test_classes()
    if found != list(DB_TEST_CLASSES) or missing:
     raise GateError("The four PostgreSQL test classes changed after executor preflight")
    selected_classes = [only_test_class] if only_test_class else list(found)
    for class_name in found:
      if class_name not in selected_classes:
       phase_b["scenarios"].append({"id": class_name, "status": "NOT RUN",
                                    "reason": "Not selected in targeted validation"})
    pending_test_classes = list(selected_classes)
    free_before_executor = free_memory_bytes()
    receipt["hostFreeMemoryBytesBeforeExecutor"] = free_before_executor
    if free_before_executor is None or free_before_executor < MIN_HOST_FREE_BYTES:
     raise InfrastructureError("Host free memory fell below the 3 GiB executor threshold")
    test_results_dir = test_results_dir or (out / "test-results")
    test_bin = ROOT / "tests" / "RealEstate.Tests" / "bin" / "Debug" / "net10.0"
    executor_create_args = dotnet_executor_create_args(
     executor_name, owner, executor_image_id, network_id, test_bin, test_results_dir,
    )
    executor_create_attempted = True
    created_executor_id = docker(executor_create_args, timeout=45).strip()
    if not re.fullmatch(r"[0-9a-f]{64}", created_executor_id):
     raise GateError("Docker returned an invalid .NET executor ID")
    executor_id = created_executor_id
    receipt["resourcesCreated"].append({"type": "container", "role": "dotnet-executor",
                                        "id": executor_id, "name": executor_name})
    docker(["start", executor_id], timeout=30)
    validate_dotnet_executor(executor_id, executor_name, owner, executor_image_id,
                             network_name, network_id, test_bin, test_results_dir,
                             test_artifacts["assemblySha256"], receipt)
    validate_two_container_network(container_id, executor_id, owner, network_name, network_id)
    sdk_version = docker(["exec", executor_id, "dotnet", "--version"], timeout=30)
    if not re.match(r"^10\.", sdk_version.strip()):
     raise InfrastructureError("The .NET sidecar did not start SDK major version 10")
    receipt["testExecutorPreflight"]["dotnetSdkVersion"] = sdk_version.strip()
    runtime_cs = connection_string(POSTGRES_NETWORK_ALIAS, 5432, database, "erp_runtime", runtime_password)
    admin_cs = connection_string(POSTGRES_NETWORK_ALIAS, 5432, database, "postgres", admin_password)
    for class_name in selected_classes:
     current_stage = f"test-{class_name}"
     test_started_at = utc_now()
     test_start_monotonic = time.monotonic()
     pending_test_classes.remove(class_name)
     trx_name = f"db-{class_name}.trx"
     trx_path = test_results_dir / trx_name
     try:
      validate_owned_container(container_id, owner, image_id, network_name, network_id,
                               evidence=receipt)
      validate_dotnet_executor(executor_id, executor_name, owner, executor_image_id,
                               network_name, network_id, test_bin, test_results_dir,
                               test_artifacts["assemblySha256"], receipt)
      validate_two_container_network(container_id, executor_id, owner, network_name, network_id)
      test_result = run_vstest_in_executor(executor_id, class_name, trx_name,
                                           (runtime_cs, admin_cs), secret_values)
      test_text = scrub((test_result.stdout or "") + "\n" + (test_result.stderr or ""), secret_values)
      (out / f"{class_name}.log").write_text(test_text, encoding="utf-8")
      if trx_path.is_file() and not is_link_or_reparse(trx_path):
       sanitize_owned_artifacts(out, secret_values)
       counts = parse_trx(trx_path)
       trx_hash = hashlib.sha256(trx_path.read_bytes()).hexdigest()
       status = "PASS" if (test_result.returncode == 0 and counts["total"] > 0
                           and counts["passed"] == counts["total"] and counts["failed"] == 0
                           and counts["skipped"] == 0) else "FAIL"
       phase_b["scenarios"].append({
        "id": class_name, "status": status, "results": counts,
        "exitCode": test_result.returncode, "assemblySha256": test_artifacts["assemblySha256"],
        "startedAtUtc": test_started_at, "finishedAtUtc": utc_now(),
        "durationSeconds": round(time.monotonic() - test_start_monotonic, 3),
        "trx": f"test-results/{trx_name}", "trxSha256": trx_hash,
       })
      else:
       phase_b["scenarios"].append({
        "id": class_name, "status": "FAIL", "reason": "TRX absent or unsafe; execution unproven",
        "exitCode": test_result.returncode, "assemblySha256": test_artifacts["assemblySha256"],
        "startedAtUtc": test_started_at, "finishedAtUtc": utc_now(),
        "durationSeconds": round(time.monotonic() - test_start_monotonic, 3),
       })
       status = "FAIL"
     except Exception as exc:
      phase_b["scenarios"].append({"id": class_name, "status": "FAIL",
                                   "detail": scrub(str(exc), secret_values),
                                   "assemblySha256": test_artifacts["assemblySha256"],
                                   "startedAtUtc": test_started_at, "finishedAtUtc": utc_now(),
                                   "durationSeconds": round(time.monotonic() - test_start_monotonic, 3)})
      status = "FAIL"
     output_json(out / "receipt.json", receipt)
     if status == "FAIL":
      for remaining_class in selected_classes[selected_classes.index(class_name) + 1:]:
       phase_b["scenarios"].append({"id": remaining_class, "status": "NOT RUN",
                                    "reason": f"Stopped after failure in {class_name}"})
      break
    runnable_results = [item for item in phase_b["scenarios"] if item["id"] in selected_classes]
    if any(item.get("status") == "FAIL" for item in runnable_results):
     phase_b["status"] = "FAIL"
    elif only_test_class and len(runnable_results) == 1 and runnable_results[0].get("status") == "PASS":
     phase_b["status"] = "INCOMPLETE"
    elif (len(runnable_results) == len(DB_TEST_CLASSES)
          and all(item.get("status") == "PASS" for item in runnable_results)):
     phase_b["status"] = "PASS"
    elif any(item.get("status") == "NOT RUN" for item in phase_b["scenarios"]):
     phase_b["status"] = "INCOMPLETE"
    else:
     phase_b["status"] = "NOT RUN"
   receipt["database"] = database
   receipt["postgresVersion"] = version_output
   receipt["imageId"] = image_id
   receipt["networkId"] = network_id
   receipt["phaseA"] = phase_a
   receipt["phaseB"] = phase_b
   output_json(out / "receipt.json", receipt)
 except KeyboardInterrupt:
  interrupted = True
  primary_error = "Execution interrupted by user (KeyboardInterrupt)"
  primary_error_type = "INTERRUPTED"
  if phase_a["status"] != "PASS":
   phase_a["status"] = "INTERRUPTED"
   phase_a["scenarios"].append({"id": current_stage, "status": "INTERRUPTED", "reason": primary_error})
   if not any(x.get("id", "").startswith("migration-") for x in phase_a["scenarios"]):
    for migration in receipt.get("migrations", []):
     number = migration["version"]
     phase_a["scenarios"].append({"id": f"migration-{number:03d}", "status": "NOT RUN",
                                  "reason": "Interrupted before application"})
   migration_match = re.fullmatch(r"migration-(\d{3})", current_stage)
   if migration_match:
    for number in range(int(migration_match.group(1)) + 1, 33):
     phase_a["scenarios"].append({"id": f"migration-{number:03d}", "status": "NOT RUN",
                                   "reason": "Interrupted after a previous migration"})
  if phase_a_only:
   phase_b["status"] = "NOT RUN"
   phase_b["scenarios"].append({"id": "functional-suite", "status": "NOT RUN",
                                "reason": "Fase A only mode; Phase B was not run"})
  elif current_stage.startswith("test-") or phase_a["status"] == "PASS":
   phase_b["status"] = "INTERRUPTED"
   phase_b["scenarios"].append({"id": current_stage, "status": "INTERRUPTED", "reason": primary_error})
   for class_name in pending_test_classes:
    phase_b["scenarios"].append({"id": class_name, "status": "NOT RUN",
                                 "reason": "Interrupted before this scenario"})
  else:
   phase_b["status"] = "SKIP"
   phase_b["scenarios"].append({"id": "phase-b", "status": "SKIP", "reason": "Interrupted before Phase B"})
 except Exception as exc:
  primary_error = scrub(str(exc), secret_values)
  primary_error_type = ("INFRASTRUCTURE" if isinstance(exc, InfrastructureError) else
                        "MIGRATION" if isinstance(exc, MigrationError) else
                        "DIAGNOSTIC" if isinstance(exc, DiagnosticError) else "SETUP")
  if isinstance(exc, PgdataTmpfsError):
   receipt["pgdataTmpfsDiagnostic"] = exc.diagnostic
  if phase_a["status"] == "NOT RUN":
   phase_a["status"] = "FAIL"
   phase_a["scenarios"].append({"id": current_stage, "status": "FAIL", "failureType": primary_error_type,
                                "detail": primary_error})
   if not any(x.get("id", "").startswith("migration-") for x in phase_a["scenarios"]):
    for migration in receipt.get("migrations", []):
     number = migration["version"]
     phase_a["scenarios"].append({"id": f"migration-{number:03d}", "status": "NOT RUN",
                                  "reason": "Falha de preparação antes da aplicação"})
  if phase_a_only:
   phase_b["status"] = "NOT RUN"
   phase_b["scenarios"].append({"id": "functional-suite", "status": "NOT RUN",
                                "reason": "Modo somente Fase A; Fase B nao executada"})
  elif phase_b["status"] == "NOT RUN":
   phase_b["status"] = "SKIP"
   phase_b["scenarios"].append({"id": "phase-b", "status": "SKIP", "reason": "Fase A/setup nao liberou execucao"})
   recorded = {item.get("id") for item in phase_b["scenarios"]}
   for class_name in DB_TEST_CLASSES:
    if class_name not in recorded:
     phase_b["scenarios"].append({"id": class_name, "status": "NOT RUN",
                                  "reason": "Fase B nao iniciou devido ao bloqueio registrado"})
 finally:
  cleanup_specs = [
   {"key": "dotnetExecutor", "type": "container", "id": executor_id,
    "createAttempted": executor_create_attempted, "name": executor_name, "owner": owner,
    "kind": EXECUTOR_KIND, "imageId": executor_image_id},
   {"key": "postgresContainer", "type": "container", "id": container_id,
    "createAttempted": container_create_attempted, "name": name, "owner": owner,
    "kind": "s05-postgres-gate", "imageId": image_id},
   {"key": "network", "type": "network", "id": network_id,
    "createAttempted": network_create_attempted, "name": network_name, "owner": owner,
    "kind": "s05-postgres-gate", "imageId": None},
  ]
  cleanup_resources, errors = cleanup_run_resources(
   cleanup_specs, local_context_verified=local_context_verified,
  )
  if "INTERRUPTED" in cleanup_resources.values():
   interrupted = True
   primary_error = primary_error or "Execu????o interrompida durante cleanup"
   primary_error_type = "INTERRUPTED"
  cleanup["resources"] = cleanup_resources
  cleanup["status"] = cleanup_result(errors, cleanup_resources)
  cleanup["resourcesRemoved"] = cleanup["status"] == "PASS"
  cleanup["errors"] = errors
  try:
   sanitize_owned_artifacts(out, secret_values)
   cleanup["artifactSanitization"] = "PASS"
  except KeyboardInterrupt:
   interrupted = True
   primary_error = primary_error or "Execution interrupted while sanitizing evidence"
   primary_error_type = "INTERRUPTED"
   errors.append("Evidence sanitization was interrupted")
   cleanup["status"] = "FAIL"
   cleanup["artifactSanitization"] = "INTERRUPTED"
   cleanup["errors"] = errors
  except Exception as exc:
   errors.append(scrub(str(exc), secret_values))
   cleanup["status"] = "FAIL"
   cleanup["artifactSanitization"] = "FAIL"
   cleanup["errors"] = errors
  receipt["phaseA"] = phase_a
  receipt["phaseB"] = phase_b
  receipt["cleanup"] = cleanup
  receipt["overallStatus"] = overall_gate_status(
   phase_a.get("status", "NOT RUN"), phase_b.get("status", "NOT RUN"), cleanup["status"],
  )
  if only_test_class:
   selected_result = next((item for item in phase_b.get("scenarios", [])
                           if item.get("id") == only_test_class), None)
   targeted_status = ("PASS" if phase_a.get("status") == "PASS"
                      and selected_result is not None and selected_result.get("status") == "PASS"
                      and cleanup.get("status") == "PASS" else
                      "FAIL" if selected_result is not None and selected_result.get("status") == "FAIL"
                      else "NOT RUN")
   receipt["targetedValidation"] = {"class": only_test_class, "status": targeted_status,
                                    "fullPhaseB": "NOT RUN"}
  receipt["finishedAtUtc"] = utc_now()
  if primary_error:
   receipt["primaryFailure"] = {"type": primary_error_type, "detail": primary_error}
  if errors:
   receipt["cleanupFailure"] = list(errors)
  try:
   if is_link_or_reparse(out) or out.resolve(strict=True).parent != report_root:
    raise GateError("Diret??rio de evid??ncias saiu do destino pr??prio")
   marker_path = out / ".ownership.json"
   receipt_path = out / "receipt.json"
   if is_link_or_reparse(marker_path) or is_link_or_reparse(receipt_path):
    raise GateError("Recusado gravar sobre artefato link simb??lico")
   saved_marker = json.loads(marker_path.read_text(encoding="utf-8"))
   if saved_marker.get("owner") != owner or saved_marker.get("runId") != run_id:
    raise GateError("Recibo n??o gravado: ownership dos artefatos diverge")
   output_json(receipt_path, receipt)
  except KeyboardInterrupt:
   interrupted = True
   primary_error = primary_error or "Execu????o interrompida ao gravar recibo"
   primary_error_type = "INTERRUPTED"
   cleanup["status"] = "FAIL"
   receipt["overallStatus"] = "FAIL"
   cleanup["receiptWriteFailure"] = "interrupted while writing receipt"
  except Exception as exc:
   cleanup["status"] = "FAIL"
   receipt["overallStatus"] = "FAIL"
   cleanup["receiptWriteFailure"] = scrub(str(exc), secret_values)

 if primary_error:
  cleanup_note = f"; falha de limpeza: {errors}" if errors else ""
  print(f"{primary_error_type or 'FAIL'}: {primary_error}{cleanup_note}; recibo: {out / 'receipt.json'}", file=sys.stderr)
  return 130 if interrupted else 2
 if cleanup.get("status") != "PASS":
  print(f"FAIL: cleanup pendente; recibo: {out / 'receipt.json'}", file=sys.stderr)
  return 3
 if only_test_class:
  targeted_status = receipt.get("targetedValidation", {}).get("status", "NOT RUN")
  print(f"TARGETED_{targeted_status}: {out / 'receipt.json'}")
  return 0 if targeted_status == "PASS" else 2
 overall = ("PHASE_A_ONLY" if phase_a_only and phase_a.get("status") == "PASS" else
            "PASS" if phase_a.get("status") == "PASS" and phase_b.get("status") == "PASS" else "BLOCKED")
 print(f"{overall}: {out / 'receipt.json'}")
 return 0 if overall == "PASS" else 2


def main(argv: list[str] | None = None) -> int:
 parser = argparse.ArgumentParser(description="Gate PostgreSQL isolado S05-B1/B2")
 parser.add_argument("--run", action="store_true", help="Cria recursos descartáveis e executa o gate")
 parser.add_argument("--inspect", action="store_true", help="Inspeção de leitura; comportamento padrão")
 parser.add_argument("--phase-a-only", action="store_true", help="Executa somente a Fase A; nunca inicia a Fase B")
 parser.add_argument("--probe-dotnet-mounts", action="store_true",
                     help="Executa apenas sondagem isolada dos mounts do executor .NET")
 parser.add_argument("--test-class", choices=DB_TEST_CLASSES,
                     help="Executa somente esta classe PostgreSQL; as demais ficam NOT RUN e o gate global permanece incompleto")
 args = parser.parse_args(argv)
 if args.phase_a_only and not args.run:
  parser.error("--phase-a-only requer --run; a inspecao padrao permanece somente leitura")
 if args.probe_dotnet_mounts and not args.run:
  parser.error("--probe-dotnet-mounts requer --run; a inspeção padrão permanece somente leitura")
 if args.probe_dotnet_mounts and args.phase_a_only:
  parser.error("--probe-dotnet-mounts não pode ser combinado com --phase-a-only")
 if args.test_class and not args.run:
  parser.error("--test-class requer --run; a inspeção padrão permanece somente leitura")
 if args.test_class and (args.phase_a_only or args.probe_dotnet_mounts):
  parser.error("--test-class não pode ser combinado com --phase-a-only ou --probe-dotnet-mounts")
 if args.run and args.inspect:
  parser.error("Use --run ou --inspect, não ambos")
 if args.run:
  try:
   if args.probe_dotnet_mounts:
    return run_dotnet_mount_probe()
   return run_gate(phase_a_only=args.phase_a_only, only_test_class=args.test_class)
  except Exception as exc:
   print(f"BLOCKED: {exc}", file=sys.stderr)
   return 2
 report = read_only_inspection()
 print(json.dumps(report, ensure_ascii=False, indent=2))
 return 0 if all(report.get(k, {}).get("status") == "PASS" for k in ("migrations", "docker", "postgresImage")) else 1


if __name__ == "__main__":
 sys.exit(main())
