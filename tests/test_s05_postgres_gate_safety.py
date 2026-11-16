"""Synthetic safety regressions for the S05 PostgreSQL gate; never invokes Docker."""

from __future__ import annotations

import importlib.util
import io
import json
import pathlib
import subprocess
import tempfile
import unittest
from contextlib import redirect_stderr
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[1]
RUNNER_PATH = ROOT / "scripts" / "s05_postgres_gate.py"
SPEC = importlib.util.spec_from_file_location("s05_postgres_gate_under_test", RUNNER_PATH)
assert SPEC and SPEC.loader
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)

_docker_guard = None
_invoke_guard = None


def setUpModule() -> None:
    global _docker_guard, _invoke_guard
    _docker_guard = mock.patch.object(gate, "docker", side_effect=AssertionError("synthetic tests must not call Docker"))
    _invoke_guard = mock.patch.object(gate, "invoke", side_effect=AssertionError("synthetic tests must not invoke subprocesses"))
    _docker_guard.start()
    _invoke_guard.start()


def tearDownModule() -> None:
    _invoke_guard.stop()
    _docker_guard.stop()


class DockerJsonTests(unittest.TestCase):
    def test_t1_format_json_requires_one_object(self) -> None:
        value = {"Id": "sha256:abc", "Architecture": "amd64"}
        self.assertEqual(gate.parse_docker_inspect_object(json.dumps(value), "docker image inspect"), value)
        with self.assertRaisesRegex(gate.GateError, "expected one JSON object"):
            gate.parse_docker_inspect_object(json.dumps([value]), "docker image inspect")
        with self.assertRaisesRegex(gate.GateError, "invalid JSON"):
            gate.parse_docker_inspect_object("{broken", "docker image inspect")

    def test_t2_context_accepts_object_or_singleton_list_only(self) -> None:
        context = {"Name": "desktop-linux", "Endpoints": {"docker": {"Host": "unix:///var/run/docker.sock"}}}
        self.assertEqual(gate.parse_docker_context_inspect(json.dumps(context)), context)
        self.assertEqual(gate.parse_docker_context_inspect(json.dumps([context])), context)
        with self.assertRaisesRegex(gate.GateError, "exactly one context object"):
            gate.parse_docker_context_inspect(json.dumps([context, context]))
        with self.assertRaisesRegex(gate.GateError, "empty list"):
            gate.parse_docker_context_inspect("[]")

    def test_versioned_mount_probe_requires_explicit_run_and_dispatches_isolated_mode(self) -> None:
        with mock.patch.object(gate, "run_dotnet_mount_probe", return_value=0) as probe:
            self.assertEqual(gate.main(["--run", "--probe-dotnet-mounts"]), 0)
            probe.assert_called_once_with()
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            gate.main(["--probe-dotnet-mounts"])
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            gate.main(["--run", "--phase-a-only", "--probe-dotnet-mounts"])

    def test_targeted_database_class_requires_run_and_does_not_dispatch_full_suite(self) -> None:
        with mock.patch.object(gate, "run_gate", return_value=0) as run_gate:
            self.assertEqual(gate.main(["--run", "--test-class", "CommercialAppointmentDatabaseTests"]), 0)
            run_gate.assert_called_once_with(phase_a_only=False,
                                             only_test_class="CommercialAppointmentDatabaseTests")
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            gate.main(["--test-class", "CommercialAppointmentDatabaseTests"])
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            gate.main(["--run", "--test-class", "CommercialAppointmentDatabaseTests", "--phase-a-only"])

    def test_migration_chain_and_phase_a_contract_include_narrow_agenda_reference_reads(self) -> None:
        migrations = gate.migration_files()
        self.assertEqual([version for version, _ in migrations], list(range(1, 33)))
        self.assertEqual(migrations[-1][1].name, "032_agenda_reference_read_privileges.sql")
        migration_sql = migrations[-1][1].read_text(encoding="utf-8")
        for fragment in (
            "GRANT SELECT (organization_id, id, name, active)",
            "GRANT SELECT (organization_id, establishment_id, id, status, code, commercial_code, title)",
            "VALUES (32)",
        ):
            self.assertIn(fragment, migration_sql)
        self.assertNotIn("GRANT SELECT ON TABLE real_estate.owners", migration_sql)
        self.assertNotIn("GRANT SELECT ON TABLE real_estate.properties", migration_sql)
        sql = gate.phase_a_sql()
        for fragment in (
            "('customers', 'SELECT', 'organization_id')",
            "('customers', 'SELECT', 'responsible_subject')",
            "('customers', 'SELECT', 'data')",
            "'customer_contacts'",
            "'customer_responsibility_history'",
            "('owners', 'SELECT', 'active')",
            "('properties', 'SELECT', 'commercial_code')",
            "('owners', 'SELECT', 'tax_id')",
            "('properties', 'SELECT', 'capture')",
        ):
            self.assertIn(fragment, sql)

    def test_direct_agenda_no_longer_uses_broad_profile_or_property_projection(self) -> None:
        root = pathlib.Path(__file__).resolve().parents[1]
        service = (root / "backend/src/RealEstate.Application/CommercialAppointmentService.cs").read_text(encoding="utf-8")
        page = (root / "backend/src/RealEstate.Web/Pages/Agenda.cshtml.cs").read_text(encoding="utf-8")
        self.assertNotIn("FindOwnerProfileAsync", service + page)
        self.assertNotIn("FindV2Async", page)
        self.assertIn("FindAgendaOwnerReferenceAsync", service + page)
        self.assertIn("FindAgendaPropertyReferenceAsync", service + page)
        actions = (root / "backend/src/RealEstate.Infrastructure/PostgresNegotiationActions.cs").read_text(encoding="utf-8")
        transfers = (root / "backend/src/RealEstate.Infrastructure/PostgresNegotiationActivityTransfers.cs").read_text(encoding="utf-8")
        self.assertIn("Audit(", actions)
        self.assertIn("Audit(", transfers)


class MountSafetyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="s05-gate-synthetic-")
        self.root = pathlib.Path(self.temp.name)
        self.tests = self.root / "tests"
        self.results = self.root / "results"
        self.tests.mkdir()
        self.results.mkdir()

    def tearDown(self) -> None:
        self.temp.cleanup()

    def inspect(self, *, read_only: object = "ABSENT", tests_read_only: object = True,
                result_rw: object = True, test_rw: object = False,
                source: str | None = None, target: str = "/results", mount_type: str = "bind",
                extra: list[dict[str, object]] | None = None) -> dict[str, object]:
        test_mount: dict[str, object] = {
            "Type": "bind", "Source": str(self.tests.resolve(strict=True)), "Target": "/tests",
            "ReadOnly": tests_read_only,
        }
        result_mount: dict[str, object] = {
            "Type": mount_type, "Source": source or str(self.results.resolve(strict=True)), "Target": target,
        }
        if read_only != "ABSENT":
            result_mount["ReadOnly"] = read_only
        observed: list[dict[str, object]] = [
            {"Type": "bind", "Source": str(self.tests.resolve(strict=True)), "Destination": "/tests", "RW": test_rw},
            {"Type": mount_type, "Source": source or str(self.results.resolve(strict=True)),
             "Destination": target, "RW": result_rw},
        ]
        observed.extend(extra or [])
        return {
            "HostConfig": {
                "Mounts": [test_mount, result_mount], "Binds": None,
                "Tmpfs": {"/tmp": "rw,noexec,nosuid,nodev,size=128m"},
            },
            "Mounts": observed,
        }

    def test_t3_result_readonly_declaration_variants_require_effective_rw(self) -> None:
        for label, value in (("absent", "ABSENT"), ("null", None), ("false", False)):
            with self.subTest(label=label):
                evidence: dict[str, object] = {}
                result = gate.validate_executor_mounts(self.inspect(read_only=value), self.tests,
                                                       self.results, evidence)
                self.assertEqual(result["status"], "PASS")
                declared = next(x for x in result["declaredMounts"] if x["destination"] == "/results")
                self.assertEqual(declared["readOnlyFieldPresent"], value != "ABSENT")
                self.assertEqual(result["checks"]["/resultsDockerAccess"], "PASS")

        for label, value in (("true", True), ("string", "false"), ("integer", 0),
                             ("list", []), ("object", {})):
            with self.subTest(label=label):
                with self.assertRaises(gate.InfrastructureError):
                    gate.validate_executor_mounts(self.inspect(read_only=value), self.tests, self.results, {})

        bad_effective = self.inspect(read_only=None, result_rw=False)
        with self.assertRaisesRegex(gate.InfrastructureError, "invalid access mode for /results"):
            gate.validate_executor_mounts(bad_effective, self.tests, self.results, {})

    def test_t3_mount_identity_and_tests_readonly_remain_strict(self) -> None:
        negatives = (
            self.inspect(source=str(self.root / "wrong-source")),
            self.inspect(target="/other"),
            self.inspect(mount_type="volume"),
            self.inspect(tests_read_only=False, test_rw=True),
            self.inspect(extra=[{"Type": "bind", "Source": "/else", "Destination": "/extra", "RW": True}]),
        )
        for item in negatives:
            with self.subTest(item=item):
                evidence: dict[str, object] = {}
                with self.assertRaises(gate.InfrastructureError):
                    gate.validate_executor_mounts(item, self.tests, self.results, evidence)
                self.assertEqual(evidence["executorMountDiagnostics"]["status"], "FAIL")
                self.assertNotIn(str(self.root), json.dumps(evidence))

    def test_t3_effective_mountinfo_conflicts_are_rejected(self) -> None:
        assembly_hash = "a" * 64
        safe = (
            "10 1 0:10 / /tests ro,relatime - ext4 /dev/test ro\n"
            "11 1 0:11 / /results rw,relatime - ext4 /dev/results rw\n"
            "12 1 0:12 / /tmp rw,nosuid,nodev,noexec - tmpfs tmpfs rw,size=134217728\n"
        )
        unsafe = safe.replace("/results rw,relatime", "/results ro,relatime")
        for raw, expected in ((safe, True), (unsafe, False)):
            evidence = {"executorMountDiagnostics": {"status": "PASS", "checks": {}, "observed": []}}
            commands: list[list[str]] = []

            def fake_docker(args: list[str], *, timeout: int = 30, **_: object) -> str:
                commands.append(args)
                if args[-1] == "/proc/self/mountinfo":
                    return raw
                return f"{assembly_hash}  /tests/RealEstate.Tests.dll\n"

            with mock.patch.object(gate, "docker", side_effect=fake_docker):
                try:
                    gate.validate_executor_effective_mounts("synthetic-id", assembly_hash, evidence)
                    passed = True
                except gate.InfrastructureError:
                    passed = False
            self.assertEqual(passed, expected)
            if expected:
                access_script = commands[1][-1]
                self.assertIn("/tests/.araia-s05-write-probe-", access_script)
                self.assertIn("/results/.araia-s05-write-probe-", access_script)
                self.assertIn("rm --", access_script)
                self.assertIn("sha256sum /tests/RealEstate.Tests.dll", access_script)

    def test_t3_versioned_probe_uses_same_mount_contract_and_network_none(self) -> None:
        args = gate.dotnet_executor_create_args(
            "synthetic-probe", "synthetic-owner", "sha256:synthetic-image", "none",
            self.tests, self.results,
        )
        self.assertEqual(args[0], "create")
        self.assertEqual(args[args.index("--network") + 1], "none")
        self.assertNotIn("--publish", args)
        self.assertNotIn("-p", args)
        mounts = [args[i + 1] for i, value in enumerate(args[:-1]) if value == "--mount"]
        self.assertEqual(len(mounts), 2)
        self.assertTrue(mounts[0].endswith("target=/tests,readonly"))
        self.assertTrue(mounts[1].endswith("target=/results"))

    def test_t3_probe_port_absence_requires_observed_ports_object(self) -> None:
        gate.assert_zero_published_ports({}, {"443/tcp": None}, {}, "executor probe")
        with self.assertRaisesRegex(gate.InfrastructureError, "cannot be discarded"):
            gate.assert_zero_published_ports({}, None, {}, "executor probe")
        with self.assertRaises(gate.InfrastructureError):
            gate.assert_zero_published_ports({}, {"443/tcp": [{"HostPort": "443"}]}, {}, "executor probe")


class ArchitectureAndCleanupTests(unittest.TestCase):
    def test_t4_unknown_architectures_never_match(self) -> None:
        self.assertTrue(gate.architectures_compatible("x86_64", "amd64"))
        self.assertTrue(gate.architectures_compatible("aarch64", "arm64"))
        for left, right in ((None, None), ("unknown", "unknown"), ("mips", "mips"),
                            ("amd64", None), (None, "amd64"), ("amd64", "arm64")):
            with self.subTest(left=left, right=right):
                self.assertFalse(gate.architectures_compatible(left, right))

    def test_t5_possible_create_timeout_and_name_inspect_timeout_fail_cleanup(self) -> None:
        timeout = subprocess.TimeoutExpired(["docker", "inspect", "owned-name"], 10)
        with mock.patch.object(gate, "invoke", side_effect=timeout) as invoke:
            result = gate.recover_owned_resource_by_name(
                "container", "owned-name", "owner-key", "owner-value", "kind-key", "kind-value",
                "sha256:image", invoke_fn=invoke,
            )
        self.assertEqual(result["status"], "INCONCLUSIVE")
        self.assertEqual(gate.cleanup_result(["Owned container recovery was inconclusive"]), "FAIL")

        absent = subprocess.CompletedProcess(["docker"], 1, "", "Error: No such object: owned-name")
        with mock.patch.object(gate, "invoke", return_value=absent) as invoke:
            result = gate.recover_owned_resource_by_name(
                "container", "owned-name", "owner-key", "owner-value", "kind-key", "kind-value",
                "sha256:image", invoke_fn=invoke,
            )
        self.assertEqual(result["status"], "ABSENT")
        self.assertEqual(gate.cleanup_result([]), "PASS")

    def test_t5_cleanup_requires_ownership_and_proven_post_removal_absence(self) -> None:
        resource_id = "a" * 64
        owner = "synthetic-owner"
        info = {
            "Id": resource_id,
            "Name": "/owned-container",
            "Config": {"Labels": {"owner": owner, "kind": "test-kind"}},
            "Image": "sha256:synthetic-image",
        }
        responses = [
            subprocess.CompletedProcess(["docker"], 0, json.dumps([info]), ""),
            subprocess.CompletedProcess(["docker"], 0, "", ""),
            subprocess.CompletedProcess(["docker"], 1, "", f"Error: No such object: {resource_id}"),
        ]
        call = mock.Mock(side_effect=responses)
        result = gate.cleanup_owned_resource_by_id(
            "container", resource_id, "owned-container", "owner", owner,
            "kind", "test-kind", "sha256:synthetic-image", invoke_fn=call,
        )
        self.assertEqual(result["status"], "REMOVED")
        self.assertEqual(call.call_count, 3)
        self.assertIn("rm", call.call_args_list[1].args[0])

        inconclusive_after = mock.Mock(side_effect=[
            subprocess.CompletedProcess(["docker"], 0, json.dumps([info]), ""),
            subprocess.CompletedProcess(["docker"], 0, "", ""),
            subprocess.TimeoutExpired(["docker", "inspect", resource_id], 10),
        ])
        result = gate.cleanup_owned_resource_by_id(
            "container", resource_id, "owned-container", "owner", owner,
            "kind", "test-kind", "sha256:synthetic-image", invoke_fn=inconclusive_after,
        )
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(inconclusive_after.call_count, 3)

        removal_timeout_but_absent = mock.Mock(side_effect=[
            subprocess.CompletedProcess(["docker"], 0, json.dumps([info]), ""),
            subprocess.TimeoutExpired(["docker", "rm", resource_id], 30),
            subprocess.CompletedProcess(["docker"], 1, "", f"Error: No such object: {resource_id}"),
        ])
        result = gate.cleanup_owned_resource_by_id(
            "container", resource_id, "owned-container", "owner", owner,
            "kind", "test-kind", "sha256:synthetic-image", invoke_fn=removal_timeout_but_absent,
        )
        self.assertEqual(result["status"], "REMOVED")
        self.assertEqual(result["removeCommandSucceeded"], False)
        self.assertEqual(result["removeCommandDiagnostic"], "remove command raised TimeoutExpired")

    def test_t5_cleanup_refuses_unowned_resource_and_keeps_inconclusive_as_fail(self) -> None:
        resource_id = "b" * 64
        unowned = {
            "Id": resource_id, "Name": "/owned-container",
            "Config": {"Labels": {"owner": "someone-else", "kind": "test-kind"}},
            "Image": "sha256:synthetic-image",
        }
        call = mock.Mock(return_value=subprocess.CompletedProcess(["docker"], 0, json.dumps([unowned]), ""))
        result = gate.cleanup_owned_resource_by_id(
            "container", resource_id, "owned-container", "owner", "expected-owner",
            "kind", "test-kind", "sha256:synthetic-image", invoke_fn=call,
        )
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(call.call_count, 1)

        timeout = subprocess.TimeoutExpired(["docker", "inspect", resource_id], 10)
        result = gate.cleanup_owned_resource_by_id(
            "container", resource_id, "owned-container", "owner", "expected-owner",
            "kind", "test-kind", "sha256:synthetic-image",
            invoke_fn=mock.Mock(side_effect=timeout),
        )
        self.assertEqual(result["status"], "FAIL")

    def test_t5_network_with_members_is_not_removed(self) -> None:
        resource_id = "c" * 64
        network = {
            "Id": resource_id, "Name": "owned-network",
            "Labels": {"owner": "expected-owner", "kind": "test-kind"},
            "Containers": {"d" * 64: {"Name": "member"}},
        }
        call = mock.Mock(return_value=subprocess.CompletedProcess(["docker"], 0, json.dumps([network]), ""))
        result = gate.cleanup_owned_resource_by_id(
            "network", resource_id, "owned-network", "owner", "expected-owner",
            "kind", "test-kind", None, invoke_fn=call,
        )
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(call.call_count, 1)

    def test_t5_run_cleanup_never_swallows_uncertain_recovery(self) -> None:
        resource = {
            "key": "postgres", "type": "container", "id": None,
            "createAttempted": True, "name": "owned-container", "owner": "expected-owner",
            "kind": "test-kind", "imageId": "sha256:synthetic-image",
        }
        timeout = subprocess.TimeoutExpired(["docker", "inspect", "owned-container"], 10)
        statuses, errors = gate.cleanup_run_resources(
            [resource], local_context_verified=True, invoke_fn=mock.Mock(side_effect=timeout),
        )
        self.assertEqual(statuses, {"postgres": "FAIL"})
        self.assertTrue(errors)
        self.assertEqual(gate.cleanup_result(errors, statuses), "FAIL")

        no_create = dict(resource, createAttempted=False)
        call = mock.Mock(side_effect=AssertionError("unexpected Docker access"))
        statuses, errors = gate.cleanup_run_resources(
            [no_create], local_context_verified=True, invoke_fn=call,
        )
        self.assertEqual(statuses, {"postgres": "NOT CREATED"})
        self.assertEqual(errors, [])
        call.assert_not_called()

        statuses, errors = gate.cleanup_run_resources(
            [dict(resource, id="f" * 64, createAttempted=False)],
            local_context_verified=True, invoke_fn=call,
        )
        self.assertEqual(statuses, {"postgres": "FAIL"})
        self.assertTrue(errors)
        call.assert_not_called()

        statuses, errors = gate.cleanup_run_resources(
            [dict(resource, id="e" * 64, createAttempted=True)],
            local_context_verified=False, invoke_fn=call,
        )
        self.assertEqual(statuses, {"postgres": "FAIL"})
        self.assertTrue(errors)
        call.assert_not_called()

        statuses, errors = gate.cleanup_run_resources(
            [dict(resource, id=None, createAttempted=True)], local_context_verified=True,
            invoke_fn=mock.Mock(side_effect=KeyboardInterrupt()),
        )
        self.assertEqual(statuses, {"postgres": "INTERRUPTED"})
        self.assertTrue(errors)

    def test_t5_absence_requires_exact_docker_not_found_evidence(self) -> None:
        absent = subprocess.CompletedProcess(["docker"], 1, "", "Error: No such object: resource-a")
        substring = subprocess.CompletedProcess(["docker"], 1, "", "Error: No such object: resource-a-extra")
        unknown = subprocess.CompletedProcess(["docker"], 1, "", "daemon unavailable")
        self.assertTrue(gate._docker_inspect_proves_absent(absent, "resource-a", "container"))
        self.assertFalse(gate._docker_inspect_proves_absent(substring, "resource-a", "container"))
        self.assertFalse(gate._docker_inspect_proves_absent(unknown, "resource-a", "container"))

    def test_t6_preflight_phase_a_or_cleanup_failure_never_approves_gate(self) -> None:
        self.assertEqual(gate.overall_gate_status("PASS", "PASS", "PASS"), "PASS")
        self.assertEqual(gate.overall_gate_status("FAIL", "PASS", "PASS"), "FAIL")
        self.assertEqual(gate.overall_gate_status("PASS", "SKIP", "PASS"), "BLOCKED")
        self.assertEqual(gate.overall_gate_status("PASS", "PASS", "FAIL"), "FAIL")
        self.assertEqual(gate.overall_gate_status("PASS", "PASS", "INCONCLUSIVE"), "FAIL")


if __name__ == "__main__":
    unittest.main()
