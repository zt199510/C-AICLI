# Week 58-65 Execution Prompts

更新时间：2026-07-14

用途：本文件集中保存 C-AICLI 0.5.0 Week 58-65 的执行提示词。每一段都可以直接交给执行 Agent，用于按周推进实现、验证、文档更新和周回顾。

## 通用要求

- 工作目录：`D:\AI\C-AICLI`。
- 先阅读本周 plan、0.5.0 总排期、roadmap、上一周 review 和相关代码/安全/发布文档，再开始修改。
- 按 plan checkbox 逐项执行，完成一步就更新对应 checkbox，不要在周末一次性全部勾选。
- 先检查 Git 状态，保留用户已有改动；只修改本周范围需要的文件。
- 优先复用 0.4.0 的 approval、workspace guard、queue、job、pipeline、artifact pointer、trace、session、report、redaction 和 smoke 路径。
- Project Pack 是确定性领域工具流程，不等同于 skill，不让模型生成或扩展真实工具参数。
- 不从零实现完整 Gerber/Excellon/TIFF parser；使用 Week 58 验证过的成熟工具或库。
- 所有外部工具执行必须使用 typed executable/arguments、显式 approval、bounded cwd/env/output、timeout/cancel 和 process cleanup。
- 不持久化 approval bypass；resume/restart 必须重新验证 tool/input/output/policy 并重新申请 approval。
- 默认测试和 smoke 不依赖模型、真实工具或网络；真实工具 smoke 只通过 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` 显式启用。
- 不把 fake driver、文件存在、metadata valid 或 preview image 单独写成“真实业务验证通过”。
- 不新增 scheduler、并行写 worker、remote runner、provider routing、API control/SSE、团队平台、marketplace 或 UI。
- 每周收尾运行 plan 要求的 build/test/smoke，并创建对应 `NN_week_review.md`，记录真实命令、计数、风险、Accepted/Preview/Deferred 和下一周输入。

## Week 58 Prompt

```text
你现在执行 C-AICLI 第 58 周计划：Project Pack Contract 与 Gerber/TIFF Toolchain Spike。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/spec/product_positioning_and_roadmap.md
- docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md
- docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md
- docs_md/plans/05_mcp_project_workflows.plan.md
- docs_md/release/final_acceptance_0.4.0.md
- docs_md/release/capability_status.md
- src/CSharpAiCli.ProjectPacks/GerberTiff/GerberTiffWorkflowPack.cs

目标：
冻结 Project Pack v1 契约，并完成真实 Windows Gerber/TIFF 工具、合法 fixture 和 TIFF inspection 依赖的技术/许可 Gate。现有 GerberTiffWorkflowPack 只是 status/validation suggestion，不能视为真实转换能力。

硬性边界：
- 至少验证一个真实工具候选的安装/定位、版本、最小转换、退出码、stdout/stderr、timeout/cancel 和输出行为。
- 明确工具 license、是否允许再分发、用户如何配置路径，以及真实 smoke 如何 opt-in。
- 至少确认一个可合法进入测试仓库的 fixture 和可复核预期输出/metadata。
- Project Pack 通用 DTO 不包含 Gerber/TIFF 领域字段或用户机器绝对路径。
- doctor 默认不启动工具；显式 --probe 才可执行，并必须经过 approval。
- 不实现真实 run、staging、TIFF verification、artifact prune 或任意 repo hooks。
- 如果真实工具/fixture/license Gate 无法通过，Week 58 review 必须明确标记 blocked；不能只完成 fake driver 后宣称通过。

执行步骤：
1. 确认 0.4.0 baseline 来自干净、可引用的 source revision，并记录 build/test/default smoke 起点。
2. 收集实际工具、命令行参数、输入/输出格式、Windows 部署和许可信息。
3. 用受控样本完成至少一次真实最小转换 spike，记录命令、版本、SHA256、退出码和结果。
4. 评估 TIFF metadata/preview 的成熟库或工具，记录 license、multipage、compression、DPI 和 pixel format 支持。
5. 写清 Project Pack、skill、pipeline、automation 的职责差异。
6. 定义 ProjectPackManifest、capability、dependency、tool identity、plan、stage、artifact、diagnostic 和 registry contract。
7. 定义 packs list/doctor CLI 和 text/JSON schema；本周只实现 contract/skeleton 或安全静态路径。
8. 定义 tool identity/probe/trust/hash-change 语义和 fake tool driver protocol。
9. 增加 contract/registry/doctor/fake-driver tests；默认测试不得依赖真实工具。
10. 更新本周涉及的 capability/known-limitations 草稿，但保持 real execution 未 Accepted。
11. 运行：
    $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
    dotnet build src\CSharpAiCli.sln -c Release
    dotnet test src\CSharpAiCli.sln -c Release --no-build
12. 逐项更新 58_week_project_pack_contract_toolchain_spike.plan.md checkbox。
13. 创建 docs_md/weekly/58_week_review.md，明确 Gate Passed/Blocked、真实证据、Deferred 和 Week 59 输入。
```

## Week 59 Prompt

```text
你现在执行 C-AICLI 第 59 周计划：Gerber/TIFF Input Discovery、Preflight 与 Conversion Plan。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md
- docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md
- docs_md/weekly/58_week_review.md
- docs_md/weekly/59_week_gerber_tiff_discovery_preflight.plan.md
- docs_md/release/security_model.md
- src/CSharpAiCli.Core/Workspace

目标：
在不执行转换的前提下，把 workspace 内受支持的 Gerber/钻孔输入转换为 bounded inventory、静态/tool-probe preflight 和 deterministic conversion plan，为 Week 60 staging 提供冻结输入。

硬性边界：
- Week 58 Toolchain Gate 必须已经通过；否则只允许完善通用 contract，不得实现或宣称 runnable Gerber/TIFF plan。
- v1 只支持明确的 workspace directory input；不做 ZIP 自动解压、URL、UNC/network input 或任意 glob。
- unknown files 只报告，不自动传给外部工具。
- doctor/plan 不执行转换、不创建持久 run/job、不写 workspace。
- tool probe 是唯一允许的外部进程路径，必须显式请求并经过 approval。
- 输出目录参数使用 --output-dir；--output 保留 text/json 输出模式。

执行步骤：
1. 冻结 v1 extension allowlist、required/optional layers 和 file/depth/byte/path/time limits。
2. 实现 canonical workspace guard、bounded directory discovery 和 reparse/symlink/UNC/device path 拒绝。
3. 实现 Gerber/drill/sidecar classifier、duplicate/ambiguous/missing diagnostics。
4. 实现 streaming file metadata/SHA256 和 stable sorting。
5. 实现 static doctor 与 approval-gated tool probe。
6. 实现 conversion plan、expected artifacts、no-overwrite validation 和 deterministic fingerprint。
7. 增加 packs plan gerber-tiff text/JSON CLI；不保存 raw input content。
8. 增加 normal/mixed-case/unknown/duplicate/limit/locked/outside/reparse/tool-change tests。
9. 增加 credential-free packs list/doctor/plan packaged smoke。
10. 更新 quickstart/security/known limitations/runtime diagnostics 草稿。
11. 运行 build、full tests 和 default smoke。
12. 逐项更新 59_week_gerber_tiff_discovery_preflight.plan.md checkbox。
13. 创建 docs_md/weekly/59_week_review.md，记录 plan schema/fingerprint、验证结果、风险和 Week 60 输入。
```

## Week 60 Prompt

```text
你现在执行 C-AICLI 第 60 周计划：Isolated Run Staging、Checkpoint 与 Safe Resume Foundation。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md
- docs_md/weekly/59_week_gerber_tiff_discovery_preflight.plan.md
- docs_md/weekly/59_week_review.md
- docs_md/weekly/60_week_isolated_run_staging_checkpoint.plan.md
- src/CSharpAiCli.Core/Jobs/JobRecords.cs
- src/CSharpAiCli.Core/Queue/TaskQueueStore.cs
- docs_md/spec/runtime_logging_diagnostics.md

目标：
建立每次 Project Pack 运行的 managed run directory、staging、run state machine、atomic checkpoint、cancel 和 resume eligibility，并通过 fake driver 验证完整阶段流转。

硬性边界：
- 源输入只读；只复制 inventory 声明的文件，复制后重验 SHA256。
- managed run root 必须 canonical containment，拒绝 reparse escape。
- run/checkpoint corrupt 或 schema 不支持时 fail closed，不猜测状态。
- checkpoint 不保存 approval token/override。
- running/interrupted execute 不自动重跑；只允许输出明确 restart requirement。
- 本周不调用真实 Gerber/TIFF 工具，不实现 TIFF verifier、accept/reject 或 prune。
- 不实现 concurrent worker、cross-process lease/heartbeat 或 remote worker。

执行步骤：
1. 定义 run id、run record、stage/checkpoint DTO、transition table 和 stable error codes。
2. 实现 %USERPROFILE%\.caicli\runs\<run-id> managed layout 与 containment/reparse protection。
3. 实现 run/checkpoint atomic store、corrupt/unsupported diagnostics。
4. 实现 bounded staging copy、safe rename mapping 和 post-copy hash verification。
5. 实现 plan/input/tool/output/policy pre-run revalidation。
6. 实现 fake driver 的 success/failure/timeout/cancel/partial-output stage events。
7. 实现 cancel/resume eligibility；明确 staged/ready/verifying/awaiting-acceptance/running/interrupted/terminal 语义。
8. 接入 queue/job/run/artifact correlation，但不复制 taskReport 真相。
9. 增加 store/staging/state/restart/concurrent-conflict/redaction/security tests。
10. 增加 dry-run packaged smoke 和 process/temp cleanup checks。
11. 更新 runtime diagnostics/security/known limitations 草稿。
12. 运行 build、full tests 和 default smoke。
13. 逐项更新 60_week_isolated_run_staging_checkpoint.plan.md checkbox。
14. 创建 docs_md/weekly/60_week_review.md，冻结 Week 61 真实外部执行前置条件。
```

## Week 61 Prompt

```text
你现在执行 C-AICLI 第 61 周计划：Gerber/TIFF Controlled Real Conversion。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md
- docs_md/weekly/58_week_review.md
- docs_md/weekly/60_week_isolated_run_staging_checkpoint.plan.md
- docs_md/weekly/60_week_review.md
- docs_md/weekly/61_week_gerber_tiff_controlled_conversion.plan.md
- src/CSharpAiCli.Core/Shell/RestrictedShellRunner.cs
- src/CSharpAiCli.Core/Approvals
- docs_md/release/security_model.md

目标：
将 Week 58 已验证的真实外部工具接入 Week 60 隔离运行状态机，通过 typed arguments、显式 approval 和有界进程控制完成第一条真实 Gerber -> TIFF 转换路径。

硬性边界：
- Week 58 Toolchain Gate 和 Week 60 run/staging Gate 必须已通过。
- 必须使用 ProcessStartInfo.ArgumentList 或等价结构化参数，禁止拼接任意 shell command string。
- 参数来自冻结模板；模型、workspace manifest 和输入文件名不能注入额外 flags。
- approval 摘要必须与实际 tool path/hash/version/input/output/timeout/operations 一致。
- 只在 managed working/output directory 执行，不覆盖，不自动安装/下载工具。
- exit 0 但缺少 declared outputs 仍是 partial/failed，不是 succeeded。
- 本周只验收 conversion executed，不提前宣称 TIFF verification passed。
- 只有 fake driver 通过不能完成 Week 61；必须有授权 fixture 的真实转换证据。

执行步骤：
1. 将 Week 58 参数模板实现为 typed Gerber/TIFF process adapter。
2. 定义 execution risk summary、approval request 和 tool identity recheck。
3. 实现 bounded executable/ArgumentList/env/cwd/stdout/stderr。
4. 实现 timeout、cancel、process-tree cleanup 和残留进程检查。
5. 实现 declared output inventory、boundary、no-overwrite、size/unexpected/partial checks。
6. 接入 run state machine、job、artifact、trace 和 redacted logs。
7. 增加 tool missing/version/hash changed/approval/timeout/cancel/partial/boundary stable errors。
8. 增加 argument quoting、tool swap、secret stderr、hang/child process 和 malicious fake adapter tests。
9. 使用授权 fixture 完成真实转换，记录 tool name/version/SHA256 和 input/output hashes。
10. 增加 CAICLI_GERBER_TIFF_TOOL_SMOKE=1 独立真实工具 smoke；未配置时默认 smoke 只 skip，不伪造成功。
11. 更新 configuration/quickstart/security/troubleshooting 草稿。
12. 运行 build、full tests、default smoke 和 real-tool opt-in smoke。
13. 逐项更新 61_week_gerber_tiff_controlled_conversion.plan.md checkbox。
14. 创建 docs_md/weekly/61_week_review.md，记录真实输出、进程清理、风险和 Week 62 verifier 输入。
```

## Week 62 Prompt

```text
你现在执行 C-AICLI 第 62 周计划：TIFF Artifact Inspection、Preview 与 Verification。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md
- docs_md/weekly/61_week_gerber_tiff_controlled_conversion.plan.md
- docs_md/weekly/61_week_review.md
- docs_md/weekly/62_week_tiff_artifact_inspection_verification.plan.md
- docs_md/release/security_model.md
- docs_md/spec/runtime_logging_diagnostics.md

目标：
对真实或 fake conversion 输出执行有界 TIFF decode/metadata、declared output validation、baseline comparison 和 PNG preview/contact sheet，生成稳定 JSON/markdown verification evidence。

硬性边界：
- 使用 Week 58 选定并验证过的成熟 TIFF library/tool，不手写 TIFF parser。
- 设置 file size、dimensions、pixel count、page/frame count、decoded memory 和 decode timeout 限制。
- verification 分层：file-valid、metadata-valid、content-compared、human-review-required；低层通过不能代表高层通过。
- exact hash 只用于已证明 byte-deterministic 的工具输出。
- preview 只用于人工复核，不是 correctness proof，不自动上传或打开。
- verifier 只读原始 TIFF/source/baseline；生成物只进入 managed artifacts/reports。
- 不做完整 CAM/EDA 制造正确性分析，不以模型视觉结论作为 hard pass/fail。

执行步骤：
1. 冻结 TIFF metadata fields、supported formats/compressions 和 resource limits。
2. 实现 bounded TIFF reader/decoder wrapper 和 corrupt/oversized diagnostics。
3. 重验 declared output inventory、file hash 和 post-conversion mutation。
4. 定义 baseline schema、source boundary、exact/tolerance/pixel comparison 语义。
5. 实现 metadata/exact hash/pixel comparison，并明确算法、颜色空间、orientation/alpha 和 tolerance。
6. 生成 managed PNG preview/contact sheet，稳定命名且 no-overwrite。
7. 实现 packs verify/preview text/JSON 和 markdown report。
8. 接入 run/job/artifact；通过 hard verification 后只进入 awaiting-acceptance。
9. 增加 single/multipage/corrupt/truncated/huge/unsupported/mismatch/preview tests。
10. 扩展 default fake fixture smoke 和 real-tool verification smoke。
11. 更新 quickstart/security/known limitations/runtime diagnostics 草稿。
12. 运行 build、full tests、default smoke 和 real-tool smoke。
13. 逐项更新 62_week_tiff_artifact_inspection_verification.plan.md checkbox。
14. 创建 docs_md/weekly/62_week_review.md，记录 verification level、preview、真实输出和 Week 63 输入。
```

## Week 63 Prompt

```text
你现在执行 C-AICLI 第 63 周计划：Artifact Lifecycle、Human Acceptance 与 Safe Resume。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/60_week_isolated_run_staging_checkpoint.plan.md
- docs_md/weekly/60_week_review.md
- docs_md/weekly/62_week_tiff_artifact_inspection_verification.plan.md
- docs_md/weekly/62_week_review.md
- docs_md/weekly/63_week_artifact_lifecycle_acceptance_resume.plan.md
- src/CSharpAiCli.Core/Jobs/JobRecords.cs
- src/CSharpAiCli.Core/Queue/TaskQueueStore.cs
- docs_md/release/known_limitations.md

目标：
实现 managed artifact list/show/verify/export/prune、人工 accept/reject 和 safe resume/restart，让大体积 TIFF 有明确生命周期，并保留所有关键证据。

硬性边界：
- 只有 awaiting-acceptance 且 hard verification passed 的 run 才能 accept。
- accept/reject 是人工显式动作；不允许模型或工具自动接受结果。
- restart execute 必须生成 new attempt/new output directory，保留 partial evidence，重新审批。
- prune 默认 dry-run，只删除 managed-root 内 owned terminal artifacts。
- prune 永不删除 source、baseline、显式 workspace output、running/interrupted/corrupt 或 outside-root path。
- 不跟随 symlink/reparse point，不实现 remote/shared store 或后台 retention worker。
- job metadata 和 tombstone 应保留，避免历史记录无法解释已清理 artifact。

执行步骤：
1. 定义 artifact id、manifest、ownership、retention 和 tombstone schema。
2. 实现 managed artifact store/index、corrupt diagnostics。
3. 实现 artifacts list/show/verify/export text/JSON/markdown，只读且 stable。
4. 实现 packs accept/reject human gate、reason redaction 和 terminal transitions。
5. 实现 staged/ready/verifying/awaiting-acceptance safe resume revalidation。
6. 实现 interrupted execute 的 explicit new-attempt restart plan。
7. 实现 artifacts prune --dry-run/--apply、age/status/size filter 和 root/reparse/race protection。
8. 接入 job/queue/run correlation 和 retained tombstone。
9. 增加 double-decision、verification-failed accept、tool/input/policy change、prune escape/race/locked tests。
10. 扩展 smoke：accept/reject、resume plan、restart evidence、prune dry-run 和受控 apply。
11. 更新 configuration/security/known limitations/troubleshooting 草稿。
12. 运行 build、full tests、default smoke 和 real-tool smoke。
13. 逐项更新 63_week_artifact_lifecycle_acceptance_resume.plan.md checkbox。
14. 创建 docs_md/weekly/63_week_review.md，记录 lifecycle 安全证据和 Week 64 hardening 输入。
```

## Week 64 Prompt

```text
你现在执行 C-AICLI 第 64 周计划：Vertical Workflow Security、Smoke、Docs 与 Release Process Hardening。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md
- docs_md/weekly/58_week_review.md
- docs_md/weekly/59_week_review.md
- docs_md/weekly/60_week_review.md
- docs_md/weekly/61_week_review.md
- docs_md/weekly/62_week_review.md
- docs_md/weekly/63_week_review.md
- docs_md/weekly/64_week_vertical_workflow_security_smoke_docs_hardening.plan.md
- docs_md/release/security_model.md
- docs_md/spec/runtime_logging_diagnostics.md
- tools/Build-Release.ps1
- tools/Invoke-SmokeTests.ps1

目标：
对 Week 58-63 的 pack/tool/input/staging/run/checkpoint/TIFF/artifact/human gate 做安全、故障、schema、smoke、文档和发布流程收口，使 Week 65 只做最终验收。

硬性边界：
- 本周不新增 pack、tool、input type、scheduler、parallel writer、remote/provider/API/UI/marketplace。
- 只修复安全缺口、状态错误、schema drift、smoke/docs/release readiness 问题。
- 如果既有设计无法安全收口，将能力回退 Deferred 或阻塞 0.5.0，不在 hardening 周扩大范围掩盖问题。
- default smoke 必须 fake-tool/model-free/network-free；real-tool smoke 独立 opt-in 并实际通过至少一次。
- release build 必须绑定 clean source revision，避免提交前制品无法从记录 source ref 重现。

执行步骤：
1. 汇总 Week 58-63 review、未完成项、Accepted/Preview/Deferred 和 release blockers。
2. 完成 external tool/input/staging/output/TIFF/state/resume/prune threat model 和命令边界表。
3. 增加 outside/reparse/TOCTOU/tool-swap/argument injection/disk-full/process-leak/decoder-bomb/prune-race tests。
4. 增加 corrupt schema、stale running、invalid transition、secret diagnostics 和 process/temp cleanup tests。
5. 固化 pack/run/artifact/verification JSON schema、error codes 和 exit policy。
6. 扩展 packaged default fake-tool smoke，覆盖 success、approval denied、missing tool、partial output、corrupt state、accept/reject 和 prune。
7. 固化 CAICLI_GERBER_TIFF_TOOL_SMOKE=1 前置诊断、真实转换/验证/preview 和 cleanup checks。
8. 加固 Build-Release：clean-tree/sourceRevision/SDK/artifact inventory/checksums/PDB 策略，并增加 release script tests。
9. 更新 CHANGELOG candidate、configuration、quickstart、security、known limitations、capability、troubleshooting、runtime diagnostics。
10. 运行 targeted security tests、full build/test/default smoke/real-tool smoke、git diff --check。
11. 逐项更新 64_week_vertical_workflow_security_smoke_docs_hardening.plan.md checkbox。
12. 创建 docs_md/weekly/64_week_review.md，证明 Week 65 只剩版本和发布动作；如仍有实现缺口，不得标记已验收。
```

## Week 65 Prompt

```text
你现在执行 C-AICLI 第 65 周计划：CLI 0.5.0 发布验收。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/spec/product_positioning_and_roadmap.md
- docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md
- docs_md/weekly/58_week_review.md
- docs_md/weekly/59_week_review.md
- docs_md/weekly/60_week_review.md
- docs_md/weekly/61_week_review.md
- docs_md/weekly/62_week_review.md
- docs_md/weekly/63_week_review.md
- docs_md/weekly/64_week_review.md
- docs_md/weekly/65_week_cli_0_5_release_acceptance.plan.md
- docs_md/release/final_acceptance_0.4.0.md
- tools/Build-Release.ps1
- tools/Invoke-SmokeTests.ps1

目标：
从干净、可引用的 source revision 对 Week 58-64 能力做最终验收，更新版本到 0.5.0，生成可复现 Windows package、manifest、checksum、final acceptance 和 release decision。

发布 Gate：
- Week 58-64 review 均已验收且没有遗留实现项。
- Release build、full tests、default fake-tool smoke、packaged diagnostics 和 cleanup 通过。
- 同一 clean source revision 的 package build 可复现，manifest/sourceRevision/checksum 一致。
- artifact prune 不能越界，resume/restart 不能双执行或复用 approval。
- release source clean，manifest/sourceRevision/checksum 能指向同一构建输入。
- Real-tool opt-in smoke 是 reviewed external tools 可用时的可选环境验证，不是 release Gate；未运行时如实记录，不能声称真实 CAM/EDA 业务正确性通过。

范围边界：
- 本周只做版本、release docs、最终验证、阻塞级缺陷回退和 package evidence。
- 不新增工具、fixture、input type、pack command、retention policy 或平台功能。
- 不把 scheduler、parallel worker、remote/provider/API control、marketplace、UI 或通用 C++/EDA 写成 Accepted。
- 外部真实工具不能再分发或当前环境缺失不构成 blocker，但用户配置、doctor、docs 和可选 opt-in smoke 入口必须完整。

执行步骤：
1. 汇总 Week 58-64 review，确认所有 release Gate 和 Deferred 边界。
2. 确认真实工具/fixture/license/verification 边界完整，并记录 opt-in smoke 为可选环境验证。
3. 固定 clean source revision；确认 tag/checksum 流程不会改变被构建 source ref。
4. 更新 Version/AssemblyVersion/FileVersion 到 0.5.0。
5. 更新 CHANGELOG、installation、configuration、quickstart、security、known limitations、capability、troubleshooting、roadmap、runtime diagnostics。
6. 创建 docs_md/release/final_acceptance_0.5.0.md。
7. 运行：
   $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
   dotnet build src\CSharpAiCli.sln -c Release
   dotnet test src\CSharpAiCli.sln -c Release --no-build
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
   如 reviewed external tools 可用，可另行设置 CAICLI_GERBER_TIFF_TOOL_SMOKE=1 运行可选环境验证。
8. 从同一 clean source revision 再运行一次 Build-Release，比较 artifact inventory、zip size/SHA256 和 manifest sourceRevision/checksums。
9. 验证 packaged caicli.exe version、packs list/doctor、artifacts list 和 missing-tool diagnostics。
10. 检查无残留 caicli/tool processes、smoke temp 和 run temp directories。
11. 生成独立 checksum artifact/发布说明并创建 release tag。
12. 逐项更新 65_week_cli_0_5_release_acceptance.plan.md checkbox。
13. 创建 docs_md/weekly/65_week_review.md，记录最终 Accepted decision、version、source revision、可选 tool/fixture smoke 状态、test count、zip size/SHA256 和 Deferred 清单。
```
