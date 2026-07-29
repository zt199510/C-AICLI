import hashlib
import json
import re
import unittest
from collections import Counter
from pathlib import Path, PurePosixPath


REPO_ROOT = Path(__file__).resolve().parents[1]
WEEKLY_ROOT = REPO_ROOT / "docs_md" / "weekly"
MANIFEST_PATH = WEEKLY_ROOT / "84_92_week_gate_requirements.json"
RAW_BYTE_ATTRIBUTES_PATH = WEEKLY_ROOT / ".gitattributes"
PLANS_ATTRIBUTES_PATH = REPO_ROOT / "docs_md" / "plans" / ".gitattributes"
TOOLS_ATTRIBUTES_PATH = REPO_ROOT / "tools" / ".gitattributes"

REPO_LOCAL_SOURCE_COLLECTION_KEYS = frozenset(
    {"entrySources", "readSources", "scenarioSources"}
)
REPO_LOCAL_SOURCE_IDENTITY_PATTERNS = {
    "sha256": re.compile(r"^[0-9a-f]{64}$"),
    "sourceSha256": re.compile(r"^[0-9a-f]{64}$"),
    "gitBlobSha": re.compile(r"^[0-9a-f]{40}$"),
    "controlRevision": re.compile(r"^[0-9a-f]{40}$"),
}
REPO_LOCAL_SOURCE_IDENTITY_MARKERS = {
    key: f"<repository-local-source-{key}>"
    for key in REPO_LOCAL_SOURCE_IDENTITY_PATTERNS
}


GROUPS = [
    (84, "baseline", "G", 10, "84_week_renderer_listener_retention_refactor_baseline.plan.md", "week84-renderer-listener-retention"),
    (85, "renderer", "R", 8, "85_week_renderer_feature_boundaries_state_split.plan.md", "week85-renderer-feature-boundaries"),
    (85, "cli", "C", 6, "85_week_cli_composition_diagnostics_modules.plan.md", "week85-cli-composition"),
    (86, "renderer", "R", 8, "86_week_chat_first_shell_design_system.plan.md", "week86-renderer-chat-first-shell"),
    (86, "cli", "C", 6, "86_week_cli_jobs_review_session_modules.plan.md", "week86-cli-jobs-review-session"),
    (87, "renderer", "R", 6, "87_week_conversation_projection_timeline.plan.md", "week87-renderer-conversation-projection"),
    (87, "cli", "C", 6, "87_week_cli_exec_skills_queue_modules.plan.md", "week87-cli-exec-skills-queue"),
    (88, "renderer", "R", 6, "88_week_composer_inline_approval_task_controls.plan.md", "week88-renderer-composer-approval"),
    (88, "cli", "C", 6, "88_week_cli_packs_artifacts_modules.plan.md", "week88-cli-packs-artifacts"),
    (89, "renderer", "R", 8, "89_week_context_panel_terminal_review_workspace.plan.md", "week89-context-review-workspace"),
    (89, "cli", "C", 8, "89_week_cli_automation_pipeline_compatibility.plan.md", "week89-cli-automation-pipeline"),
    (90, "integration", "G", 8, "90_week_cli_desktop_cross_lane_integration.plan.md", "week90-cli-desktop-integration"),
    (91, "hardening", "G", 8, "91_week_refactor_security_resource_hardening.plan.md", "week91-refactor-hardening"),
    (92, "acceptance", "G", 10, "92_week_refactor_final_acceptance.plan.md", "week92-refactor-acceptance"),
]


def expected_gate_ids():
    return [
        f"W{week}-{prefix}{index}"
        for week, _lane, prefix, count, _plan, _artifact_dir in GROUPS
        for index in range(count)
    ]


def critical_gate_ids(plan_text):
    match = re.search(
        r"^## (?:Critical Gates|Final Gates)\s*$(.*?)(?=^## |\Z)",
        plan_text,
        flags=re.MULTILINE | re.DOTALL,
    )
    if match is None:
        raise AssertionError("plan has no Critical Gates or Final Gates section")
    return re.findall(r"W(?:8[4-9]|9[0-2])-[GRC]\d+", match.group(1))


def is_canonical_repo_local_source_path(value):
    if (
        not isinstance(value, str)
        or not value
        or "\\" in value
        or value.startswith("/")
    ):
        return False
    path = PurePosixPath(value)
    return (
        path.as_posix() == value
        and bool(path.parts)
        and ":" not in path.parts[0]
        and all(part not in {"", ".", ".."} for part in path.parts)
    )


def manifest_semantic_projection(value, *, inside_source_collection=False):
    if isinstance(value, list):
        return [
            manifest_semantic_projection(
                item,
                inside_source_collection=inside_source_collection,
            )
            for item in value
        ]
    if not isinstance(value, dict):
        return value

    local_source_binding = is_canonical_repo_local_source_path(
        value.get("sourcePath")
    ) or (
        inside_source_collection
        and is_canonical_repo_local_source_path(value.get("path"))
    )
    projected = {}
    for key, item in value.items():
        pattern = REPO_LOCAL_SOURCE_IDENTITY_PATTERNS.get(key)
        if (
            local_source_binding
            and pattern is not None
            and isinstance(item, str)
            and pattern.fullmatch(item) is not None
        ):
            projected[key] = REPO_LOCAL_SOURCE_IDENTITY_MARKERS[key]
            continue
        projected[key] = manifest_semantic_projection(
            item,
            inside_source_collection=(
                inside_source_collection
                or key in REPO_LOCAL_SOURCE_COLLECTION_KEYS
            ),
        )
    return projected


def manifest_semantic_projection_sha256(value):
    projection = manifest_semantic_projection(value)
    raw = json.dumps(
        projection,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


class GateRequirementsManifestTests(unittest.TestCase):
    def test_raw_byte_goal_evidence_disables_checkout_conversion(self):
        self.assertEqual(
            [
                "# Raw-byte identities for immutable Goal evidence must survive Windows checkout.",
                ".gitattributes -text",
                "84_92_week_* -text",
                "84_week_renderer_listener_retention_refactor_baseline.plan.md -text",
                "85_week_cli_composition_diagnostics_modules.plan.md -text",
                "85_week_renderer_feature_boundaries_state_split.plan.md -text",
                "86_week_chat_first_shell_design_system.plan.md -text",
                "86_week_cli_jobs_review_session_modules.plan.md -text",
                "87_week_cli_exec_skills_queue_modules.plan.md -text",
                "87_week_conversation_projection_timeline.plan.md -text",
                "88_week_cli_packs_artifacts_modules.plan.md -text",
                "88_week_composer_inline_approval_task_controls.plan.md -text",
                "89_week_cli_automation_pipeline_compatibility.plan.md -text",
                "89_week_context_panel_terminal_review_workspace.plan.md -text",
                "90_week_cli_desktop_cross_lane_integration.plan.md -text",
                "91_week_refactor_security_resource_hardening.plan.md -text",
                "92_week_refactor_final_acceptance.plan.md -text",
                "84_92_evidence_anchors/*.json -text",
                "84_92_user_acceptance_requests/*.json -text",
                "84_92_command_control/** -text",
                "84_92_anchor_registry/** -text",
                "84_92_provider_boundary_decisions/** -text",
                "84_92_controlled_write_authorizations/** -text",
                "84_92_controlled_write_tombstones/*.json -text",
            ],
            RAW_BYTE_ATTRIBUTES_PATH.read_text(encoding="utf-8").splitlines(),
        )
        self.assertEqual(
            [
                "# Bootstrap control bytes are immutable across checkout platforms.",
                ".gitattributes -text",
                "07_cli_desktop_experience_refactor.plan.md -text",
            ],
            PLANS_ATTRIBUTES_PATH.read_text(encoding="utf-8").splitlines(),
        )
        self.assertEqual(
            [
                "# Bootstrap control bytes are immutable across checkout platforms.",
                ".gitattributes -text",
                "test_validate_week84_92_goal_evidence.py -text",
                "test_week84_92_evidence_anchor.py -text",
                "test_week84_92_gate_requirements.py -text",
                "test_week84_92_goal_integrity.py -text",
                "test_week84_92_trusted_executor.py -text",
                "validate-week84-92-goal-evidence.py -text",
                "week84_92_evidence_anchor.py -text",
                "week84_92_goal_integrity.py -text",
                "week84_92_provider_turn_harness.py -text",
                "week84_92_trusted_executor.py -text",
                "week84_92_commands/*.py -text",
                "week84_92_provider_scenarios/*.py -text",
                "week84_92_provider_drivers/*.mjs -text",
            ],
            TOOLS_ATTRIBUTES_PATH.read_text(encoding="utf-8").splitlines(),
        )

    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        cls.gates = cls.manifest["gates"]
        cls.by_id = {gate["gateId"]: gate for gate in cls.gates}

    def test_exact_ordered_104_gate_registry_matches_plans(self):
        self.assertEqual(
            "dfefe4f2c08cfc1d738587a66f1bbca637950420b80bc80b7b03e63674263418",
            manifest_semantic_projection_sha256(self.manifest),
        )
        expected_ids = expected_gate_ids()
        self.assertEqual(104, self.manifest["gateCount"])
        self.assertEqual(104, len(self.gates))
        self.assertEqual(expected_ids, [gate["gateId"] for gate in self.gates])
        self.assertEqual(104, len(self.by_id))

        expected_plan_order = [group[4] for group in GROUPS]
        self.assertEqual(expected_plan_order, self.manifest["sourcePlans"])

        offset = 0
        for week, lane, prefix, count, plan_name, _artifact_dir in GROUPS:
            plan_path = WEEKLY_ROOT / plan_name
            self.assertTrue(plan_path.is_file(), plan_path)
            plan_ids = critical_gate_ids(plan_path.read_text(encoding="utf-8"))
            group_ids = [f"W{week}-{prefix}{index}" for index in range(count)]
            self.assertEqual(group_ids, plan_ids, plan_name)

            manifest_group = self.gates[offset : offset + count]
            self.assertEqual(group_ids, [gate["gateId"] for gate in manifest_group])
            self.assertTrue(all(gate["week"] == week for gate in manifest_group))
            self.assertTrue(all(gate["lane"] == lane for gate in manifest_group))
            self.assertTrue(all(gate["sourcePlan"] == plan_name for gate in manifest_group))
            offset += count
        self.assertEqual(104, offset)

    def test_manifest_semantic_projection_is_cycle_free_and_substantive(self):
        projection = manifest_semantic_projection(self.manifest)
        baseline_sha = manifest_semantic_projection_sha256(self.manifest)
        policies = self.manifest["frozenPolicies"]
        projected_policies = projection["frozenPolicies"]

        source_bindings = [
            (
                policies["trusted-executor"],
                projected_policies["trusted-executor"],
                "sourceSha256",
            ),
            *(
                (source, projected, "sha256")
                for source, projected in zip(
                    policies["trusted-test-command"]["entrySources"],
                    projected_policies["trusted-test-command"]["entrySources"],
                    strict=True,
                )
            ),
            *(
                (source, projected, "sha256")
                for source, projected in zip(
                    policies["trusted-test-command"]["readSources"],
                    projected_policies["trusted-test-command"]["readSources"],
                    strict=True,
                )
            ),
            (
                policies["trusted-provider-command"],
                projected_policies["trusted-provider-command"],
                "sourceSha256",
            ),
            *(
                (
                    source,
                    projected_policies["trusted-provider-command"][
                        "scenarioSources"
                    ][phase],
                    "sha256",
                )
                for phase, source in policies["trusted-provider-command"][
                    "scenarioSources"
                ].items()
            ),
        ]
        self.assertEqual(21, len(source_bindings))
        for source, projected, identity_key in source_bindings:
            with self.subTest(path=source.get("path", source.get("sourcePath"))):
                self.assertRegex(
                    source[identity_key],
                    REPO_LOCAL_SOURCE_IDENTITY_PATTERNS[identity_key],
                )
                self.assertEqual(
                    REPO_LOCAL_SOURCE_IDENTITY_MARKERS[identity_key],
                    projected[identity_key],
                )
                self.assertEqual(set(source), set(projected))
                for key in set(source) - {
                    identity_key,
                    *REPO_LOCAL_SOURCE_COLLECTION_KEYS,
                }:
                    self.assertEqual(source[key], projected[key])

        test_source_mutation = json.loads(json.dumps(self.manifest))
        test_source = next(
            source
            for source in test_source_mutation["frozenPolicies"][
                "trusted-test-command"
            ]["entrySources"]
            if source["path"] == "tools/test_week84_92_gate_requirements.py"
        )
        test_source["sha256"] = hashlib.sha256(
            (REPO_ROOT / test_source["path"]).read_bytes()
        ).hexdigest()
        self.assertEqual(
            baseline_sha,
            manifest_semantic_projection_sha256(test_source_mutation),
        )
        test_source["sha256"] = "0" * 64
        self.assertEqual(
            baseline_sha,
            manifest_semantic_projection_sha256(test_source_mutation),
        )

        self.assertEqual(self.manifest["gates"], projection["gates"])
        self.assertEqual(
            policies["provider-budget"],
            projected_policies["provider-budget"],
        )
        self.assertEqual(
            policies["trusted-git"],
            projected_policies["trusted-git"],
        )
        substantive_mutations = []
        for path in ("gate", "policy", "trusted-git"):
            mutated = json.loads(json.dumps(self.manifest))
            if path == "gate":
                mutated["gates"][0]["minimumCommandCount"] += 1
            elif path == "policy":
                mutated["frozenPolicies"]["provider-budget"][
                    "goalTurnCap"
                ] -= 1
            else:
                mutated["frozenPolicies"]["trusted-git"][
                    "executableSha256"
                ] = "0" * 64
            substantive_mutations.append((path, mutated))
        for path, mutated in substantive_mutations:
            with self.subTest(substantive=path):
                self.assertNotEqual(
                    baseline_sha,
                    manifest_semantic_projection_sha256(mutated),
                )

    def test_real_manifest_loads_exact_product_and_provider_command_policies(self):
        from tools import week84_92_trusted_executor as executor

        product = self.manifest["frozenPolicies"]["trusted-product-command"]
        self.assertEqual(
            {
                "policyId": executor.TRUSTED_PRODUCT_POLICY_ID,
                "executableRole": executor.ISOLATED_PYTHON_EXECUTABLE_ROLE,
                "scriptRoot": executor.TRUSTED_PRODUCT_SCRIPT_ROOT,
                "argumentsTemplate": list(executor._PRODUCT_TEMPLATE_ARGUMENTS),
                "redactedInvocationTemplate": (
                    "python-current -I -S -E -B -X utf8 -c "
                    "<week84-92-in-memory-command-adapter-v1:{commandId}>"
                ),
                "shellAllowed": False,
            },
            product,
        )
        provider = self.manifest["frozenPolicies"]["trusted-provider-command"]
        self.assertEqual(
            {
                "policyId",
                "executableRole",
                "sourcePath",
                "sourceSha256",
                "scenarioSources",
                "argumentsTemplate",
                "redactedInvocation",
                "shellAllowed",
                "allowedBatchSizes",
                "observedRequestProtocol",
            },
            set(provider),
        )
        self.assertEqual(
            hashlib.sha256(
                (REPO_ROOT / provider["sourcePath"]).read_bytes()
            ).hexdigest(),
            provider["sourceSha256"],
        )
        self.assertEqual(
            {
                phase: {
                    "path": binding["path"],
                    "sha256": hashlib.sha256(
                        (REPO_ROOT / binding["path"]).read_bytes()
                    ).hexdigest(),
                }
                for phase, binding in provider["scenarioSources"].items()
            },
            provider["scenarioSources"],
        )
        loaded_product, product_sha = executor._trusted_product_policy(
            REPO_ROOT,
            self.manifest,
        )
        loaded_provider, provider_sha = executor._trusted_provider_policy(
            REPO_ROOT,
            self.manifest,
        )
        self.assertEqual(product, loaded_product)
        self.assertEqual(provider, loaded_provider)
        self.assertEqual(
            hashlib.sha256(executor.canonical_json_bytes(product)).hexdigest(),
            product_sha,
        )
        self.assertEqual(
            hashlib.sha256(executor.canonical_json_bytes(provider)).hexdigest(),
            provider_sha,
        )

    def test_each_gate_is_fail_closed_and_path_bound(self):
        evidence_kinds = {
            "json",
            "text-log",
            "test-report",
            "screenshot",
            "video",
            "identity",
            "diff",
            "manual-attestation",
            "plan-reference",
            "other",
        }
        required_fields = {
            "week",
            "lane",
            "gateId",
            "resultPath",
            "commandControl",
            "sourcePlan",
            "gateKind",
            "requiredCommandIds",
            "requiredAssertionIds",
            "minimumCommandCount",
            "minimumTestCount",
            "zeroMinimumTestBasis",
            "requiredEvidenceKinds",
            "requiredEvidenceBasenames",
            "manualAcceptanceId",
            "providerRequirement",
            "controlledWrite",
            "frozen",
        }
        group_by_key = {(week, lane): group for group in GROUPS for week, lane in [(group[0], group[1])]}

        for gate in self.gates:
            gate_id = gate["gateId"]
            self.assertEqual(required_fields, set(gate), gate_id)
            self.assertRegex(gate["gateKind"], r"^[a-z][a-z0-9-]+$")

            commands = gate["requiredCommandIds"]
            self.assertGreaterEqual(len(commands), 3, gate_id)
            self.assertEqual(len(commands), len(set(commands)), gate_id)
            self.assertEqual(len(commands), gate["minimumCommandCount"], gate_id)
            self.assertIn("goal-evidence-semantic-validate", commands, gate_id)
            for command_id in commands:
                self.assertRegex(command_id, r"^[a-z][a-z0-9-]+$", gate_id)

            assertions = gate["requiredAssertionIds"]
            self.assertGreaterEqual(len(assertions), 3, gate_id)
            self.assertEqual(len(assertions), len(set(assertions)), gate_id)
            for assertion_id in assertions:
                self.assertRegex(assertion_id, r"^[A-Za-z0-9][A-Za-z0-9.-]+$", gate_id)

            minimum_tests = gate["minimumTestCount"]
            self.assertGreaterEqual(minimum_tests, 0, gate_id)
            if minimum_tests == 0:
                self.assertIsInstance(gate["zeroMinimumTestBasis"], str, gate_id)
                self.assertTrue(gate["zeroMinimumTestBasis"].strip(), gate_id)
            else:
                self.assertIsNone(gate["zeroMinimumTestBasis"], gate_id)

            self.assertTrue(gate["requiredEvidenceKinds"], gate_id)
            self.assertTrue(set(gate["requiredEvidenceKinds"]) <= evidence_kinds, gate_id)
            basenames = gate["requiredEvidenceBasenames"]
            self.assertTrue(basenames, gate_id)
            self.assertEqual(len(basenames), len(set(basenames)), gate_id)
            source_text = (WEEKLY_ROOT / gate["sourcePlan"]).read_text(encoding="utf-8")
            for basename in basenames:
                self.assertEqual(PurePosixPath(basename).name, basename, gate_id)
                self.assertIn(basename, source_text, f"{gate_id}: {basename}")

            group = group_by_key[(gate["week"], gate["lane"])]
            artifact_dir = group[5]
            expected_result_path = f"artifacts/{artifact_dir}/gates/{gate_id}.json"
            self.assertEqual(expected_result_path, gate["resultPath"], gate_id)
            result_path = PurePosixPath(gate["resultPath"])
            self.assertFalse(result_path.is_absolute(), gate_id)
            self.assertNotIn("..", result_path.parts, gate_id)

    def test_each_gate_has_exact_prior_sealed_command_control_policy(self):
        offset = 0
        expected_role_policy = {
            "requiredAll": ["adapter"],
            "requiredAny": [["oracle", "test"]],
            "optional": ["fixture", "parser"],
        }
        observed_modes = Counter()
        for week, lane, _prefix, count, _plan, _artifact_dir in GROUPS:
            for index, gate in enumerate(self.gates[offset : offset + count]):
                gate_id = gate["gateId"]
                control = gate["commandControl"]
                if gate_id == "W84-G0":
                    expected_mode = "w84-bootstrap-exception"
                    expected_trust = "bootstrap-external-review"
                elif gate_id == "W90-G0":
                    expected_mode = "w90-integration-entry-base"
                    expected_trust = "prior-sealed"
                elif index == 0:
                    expected_mode = "entry-base"
                    expected_trust = "prior-sealed"
                else:
                    expected_mode = "prior-gate"
                    expected_trust = "prior-sealed"
                self.assertEqual(
                    {
                        "path": (
                            "docs_md/weekly/84_92_command_control/"
                            f"{gate_id}.json"
                        ),
                        "sourceTrust": expected_trust,
                        "predecessorMode": expected_mode,
                        "rolePolicy": expected_role_policy,
                    },
                    control,
                    gate_id,
                )
                observed_modes[expected_mode] += 1
            offset += count
        self.assertEqual(
            Counter(
                {
                    "w84-bootstrap-exception": 1,
                    "entry-base": 12,
                    "prior-gate": 90,
                    "w90-integration-entry-base": 1,
                }
            ),
            observed_modes,
        )

    def test_all_frozen_references_and_flags_are_declared(self):
        policies = self.manifest["frozenPolicies"]
        workloads = self.manifest["workloads"]
        for gate in self.gates:
            gate_id = gate["gateId"]
            frozen = gate["frozen"]
            self.assertEqual(
                {"thresholdRefs", "viewportSet", "workloadRefs", "compatibilityFlags"},
                set(frozen),
                gate_id,
            )
            self.assertTrue(set(frozen["thresholdRefs"]) <= set(policies), gate_id)
            self.assertTrue(set(frozen["workloadRefs"]) <= set(workloads), gate_id)
            self.assertTrue(frozen["compatibilityFlags"], gate_id)
            self.assertEqual(
                len(frozen["compatibilityFlags"]),
                len(set(frozen["compatibilityFlags"])),
                gate_id,
            )
            if frozen["viewportSet"] is not None:
                self.assertIn(frozen["viewportSet"], policies, gate_id)

        self.assertEqual(
            ["1440x900", "1024x768", "800x900"],
            policies["desktop-three-viewports"]["viewports"],
        )
        self.assertEqual(0, policies["desktop-three-viewports"]["horizontalOverflowPixelsMax"])
        self.assertEqual(15, policies["chat-shell-visual-matrix"]["caseCount"])
        self.assertEqual(40, policies["renderer-resource"]["listenerDeltaMax"])
        self.assertEqual(15, policies["renderer-resource"]["rendererPrivateBytesDeltaPercentMax"])
        self.assertEqual(39, policies["desktop-protocol-v1"]["invokeCount"])
        self.assertEqual(2, policies["desktop-protocol-v1"]["eventCount"])
        self.assertEqual(26, policies["cli-command-tree"]["topLevelCommandCount"])
        self.assertEqual(2000, policies["conversation-bounds"]["unitItemCount"])
        self.assertEqual(240, policies["conversation-bounds"]["e2eItemCount"])
        self.assertEqual(8192, policies["terminal-bounds"]["rendererTailBytes"])
        self.assertEqual(65536, policies["terminal-bounds"]["protocolBoundaryBytes"])
        self.assertEqual(120, policies["provider-budget"]["goalTurnCap"])
        self.assertEqual(5, policies["provider-budget"]["profileCount"])
        self.assertEqual(1, policies["provider-budget"]["warmupTurnsPerProfile"])
        self.assertEqual(5, policies["provider-budget"]["measuredTurnsPerProfile"])
        self.assertEqual(
            {
                "runnerId": "week84-92-trusted-executor-v1",
                "sourcePath": "tools/week84_92_trusted_executor.py",
                "sourceSha256": "7a1821e174d25e9456cd9d8f52a2397612a72cb5c64c653a7f81fc33050b1171",
                "shellAllowed": False,
            },
            policies["trusted-executor"],
        )
        trusted_test = policies["trusted-test-command"]
        self.assertEqual("week84-92-semantic-test-v1", trusted_test["policyId"])
        self.assertEqual(
            "goal-evidence-semantic-validate", trusted_test["commandId"]
        )
        self.assertEqual("python-current-isolated", trusted_test["executableRole"])
        self.assertEqual(
            ["-I", "-S", "-E", "-B", "-X", "utf8", "-c"],
            trusted_test["arguments"][:7],
        )
        self.assertIn(
            "tools.test_validate_week84_92_goal_evidence",
            trusted_test["arguments"][7],
        )
        self.assertEqual(
            "python-unittest-output", trusted_test["countsSource"]
        )
        self.assertIs(False, trusted_test["shellAllowed"])

    def test_provider_and_controlled_write_requirements_are_exact(self):
        provider_gates = {
            gate_id: gate["providerRequirement"]
            for gate_id, gate in self.by_id.items()
            if gate["providerRequirement"] is not None
        }
        self.assertEqual({"W84-G6", "W84-G7", "W84-G8", "W92-G7"}, set(provider_gates))

        self.assertEqual(3, provider_gates["W84-G6"]["minimumTurns"])
        self.assertEqual(
            ["provider-read-only", "provider-recovery"],
            provider_gates["W84-G6"]["scopes"],
        )

        w84_resource = provider_gates["W84-G7"]
        self.assertEqual(30, w84_resource["minimumTurns"])
        self.assertEqual(["provider-resource"], w84_resource["scopes"])
        self.assertEqual(5, w84_resource["consecutiveProfileCount"])
        self.assertEqual(1, w84_resource["warmupTurnsPerProfile"])
        self.assertEqual(5, w84_resource["measuredTurnsPerProfile"])
        self.assertEqual(5, len(w84_resource["receiptBasenames"]))

        self.assertEqual(1, provider_gates["W84-G8"]["minimumTurns"])
        self.assertEqual(["controlled-write"], provider_gates["W84-G8"]["scopes"])

        expected_boundary_paths = {
            "W84-G6": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
            "W84-G7": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
            "W84-G8": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
            "W92-G7": "docs_md/weekly/84_92_provider_boundary_decisions/W92-G7.json",
        }
        expected_launch_counts = {
            "W84-G6": 2,
            "W84-G7": 5,
            "W84-G8": 1,
            "W92-G7": 8,
        }
        for gate_id, requirement in provider_gates.items():
            self.assertEqual(
                expected_boundary_paths[gate_id],
                requirement["boundaryDecisionPath"],
            )
            self.assertEqual(
                ["strong-isolation", "cooperative-candidate"],
                requirement["allowedBoundaryModes"],
            )
            self.assertEqual(
                expected_launch_counts[gate_id],
                requirement["packageLaunchReceiptCount"],
            )
            flags = set(self.by_id[gate_id]["frozen"]["compatibilityFlags"])
            self.assertTrue(
                {
                    "declared-provider-boundary-mode",
                    "exact-package-tree-launched",
                    "package-launch-receipt",
                    "provider-runtime-drivers-prior-sealed",
                }.issubset(flags)
            )
            self.assertFalse(
                flags
                & {
                    "isolated-packaged-child",
                    "all-egress-mediated",
                    "isolated-child-secret-redaction",
                }
            )

        self.assertIn(
            "provider-runtime-drivers-first-add-sealed",
            self.by_id["W84-G5"]["frozen"]["compatibilityFlags"],
        )

        w92 = provider_gates["W92-G7"]
        self.assertEqual(34, w92["minimumTurns"])
        self.assertEqual(
            [
                "provider-read-only",
                "provider-recovery",
                "provider-resource",
                "controlled-write",
            ],
            w92["scopes"],
        )
        self.assertEqual(5, w92["consecutiveProfileCount"])
        self.assertEqual(1, w92["warmupTurnsPerProfile"])
        self.assertEqual(5, w92["measuredTurnsPerProfile"])
        self.assertEqual(
            [
                "provider-readonly.json",
                "provider-recovery.json",
                "provider-resource-profile-1.json",
                "provider-resource-profile-2.json",
                "provider-resource-profile-3.json",
                "provider-resource-profile-4.json",
                "provider-resource-profile-5.json",
                "controlled-write.json",
            ],
            w92["receiptBasenames"],
        )

        for gate_id, requirement in provider_gates.items():
            self.assertTrue(requirement["receiptBasenames"], gate_id)
            self.assertTrue(
                set(requirement["receiptBasenames"])
                <= set(self.by_id[gate_id]["requiredEvidenceBasenames"]),
                gate_id,
            )

        controlled_write_gates = {
            gate["gateId"] for gate in self.gates if gate["controlledWrite"]
        }
        self.assertEqual({"W84-G8", "W92-G7"}, controlled_write_gates)
        controlled_shape = self.manifest["frozenPolicies"]["controlled-write-shape"]
        self.assertEqual(1, controlled_shape["maximumRunsPerAuthorizedWeek"])
        self.assertEqual("result.txt", controlled_shape["onlyChangedPath"])
        self.assertEqual("fail-to-pass", controlled_shape["transition"])
        self.assertEqual(2, controlled_shape["durableApprovalCount"])
        self.assertEqual(0, controlled_shape["automaticReplayMax"])

    def test_manual_acceptance_and_explicit_case_minima(self):
        manual = {
            gate["gateId"]: gate["manualAcceptanceId"]
            for gate in self.gates
            if gate["manualAcceptanceId"] is not None
        }
        self.assertEqual(
            {
                "W86-R7": "W86-USER-VISUAL",
                "W89-R5": "W89-USER-VISUAL",
                "W92-G9": "W92-USER-VISUAL",
            },
            manual,
        )
        self.assertEqual(15, self.by_id["W86-R2"]["minimumTestCount"])
        self.assertEqual(2, self.by_id["W87-R3"]["minimumTestCount"])
        self.assertEqual(2, self.by_id["W84-G6"]["minimumTestCount"])
        self.assertEqual(5, self.by_id["W84-G7"]["minimumTestCount"])
        self.assertEqual(8, self.by_id["W92-G7"]["minimumTestCount"])
        self.assertEqual(
            {"W86-R7", "W88-C4", "W92-G9"},
            {
                gate["gateId"]
                for gate in self.gates
                if gate["minimumTestCount"] == 0
            },
        )

        for gate_id in ("W86-R2", "W86-R3", "W86-R7", "W87-R5", "W88-R4", "W88-R5", "W89-R5", "W89-R6", "W90-G5", "W91-G0", "W91-G2", "W92-G3", "W92-G9"):
            self.assertEqual(
                "desktop-three-viewports",
                self.by_id[gate_id]["frozen"]["viewportSet"],
                gate_id,
            )

    def test_gate_distribution_is_exact(self):
        expected = Counter(
            {
                (84, "baseline"): 10,
                (85, "renderer"): 8,
                (85, "cli"): 6,
                (86, "renderer"): 8,
                (86, "cli"): 6,
                (87, "renderer"): 6,
                (87, "cli"): 6,
                (88, "renderer"): 6,
                (88, "cli"): 6,
                (89, "renderer"): 8,
                (89, "cli"): 8,
                (90, "integration"): 8,
                (91, "hardening"): 8,
                (92, "acceptance"): 10,
            }
        )
        actual = Counter((gate["week"], gate["lane"]) for gate in self.gates)
        self.assertEqual(expected, actual)


if __name__ == "__main__":
    unittest.main()
