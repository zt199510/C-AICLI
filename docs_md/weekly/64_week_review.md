# 第 64 周回顾

状态：已完成；Vertical Workflow security/schema/smoke/docs/release-process hardening 已收口，未发现需要 Week 65 继续实现的功能缺口。Week 65 只剩版本、干净提交、最终 release acceptance build、复验和发布决定。

## 范围结论

- Week 58-63 的 Project Pack、tool/input、plan、staging、run/checkpoint、真实 Gerber/TIFF conversion、bounded TIFF verification、preview、human gate、resume/restart 和 managed artifact lifecycle 已完成 Week 64 安全与故障收口。
- Project Pack 仍是确定性领域工具流程，不是 skill；真实 executable、argv、cwd、env 和 output 均由 pack-owned typed contract 固定，模型不能生成或扩展参数。
- 本周没有新增 pack、tool、input type、scheduler、parallel writer/worker、remote runner、provider routing、API control/SSE、team platform、marketplace 或 UI。
- default smoke 继续 model-free、network-free、real-tool-free；真实工具只由 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` 和四个显式 prerequisite 启用。
- [Vertical Workflow Hardening Contract](../spec/vertical_workflow_hardening.md) 已记录 Week 58-63 基线、release blockers、threat model、命令边界、冻结 schema/error/exit policy 和测试映射。
- 0.4.0 Accepted 边界与既有 release artifact 未改变。本周 dirty-source validation package 不是 0.5.0 release，也不能替代 Week 65 clean-source acceptance build。

## Week 58-63 收口

| 周 | 进入 Week 64 的证据 | Week 64 结论 |
|---:|---|---|
| 58 | Gerbv 2.13.0、ImageMagick 7.1.2-27、LibTIFF、CC0 fixture、许可与固定参数 Gate | 工具/fixture/executable identity 原样复用；未实现新 parser 或新工具链 |
| 59 | bounded input discovery、static/probe preflight、deterministic plan/fingerprint | outside/reparse/TOCTOU/argument-injection 与 output boundary 纳入 threat/test contract |
| 60 | managed run root、staging、checkpoint、transition、resume foundation | atomic run-root、staging cleanup、corrupt split state、stale running/invalid transition 已回归 |
| 61 | approval-gated typed conversion、bounded process/cwd/env/output、真实 TIFF evidence | tool swap、partial output、timeout/cancel、process/temp cleanup 和 approval exit policy 已固化 |
| 62 | mature managed TIFF codec、resource limits、strict baseline、verification/preview | dimension/frame bomb、corrupt decode、baseline identity 和 independent levels 已收口 |
| 63 | accept/reject、safe resume/restart、artifact list/verify/export/prune/tombstone | fake/changed/double decision、restart revalidation、prune race/reparse/tombstone 已回归 |

Week 63 结束时唯一垂直 release blocker 是扩展后的真实 lifecycle smoke 尚未实际通过；本周最终 packaged opt-in smoke 已覆盖 conversion、strict content comparison、preview、accept/reject、artifact verify/export、prune 和 cleanup 并通过。

## 实现与契约

- 增加 Windows run-root 原子创建、staging TOCTOU failure cleanup、disk-write/lock-conflict 分类、corrupt run/checkpoint/manifest diagnostics、TIFF dimension/frame bomb 和 process/temp cleanup 回归。
- prune、tombstone、restart、preview 与 corrupt diagnostics 使用结构化 JSON projection 和 central redaction，不回显 secret-like raw path/content/exception。
- schema contract 冻结当前 schema version、12 个 run state、46 个 run/tool/process code、27 个 TIFF code、14 个 artifact code，以及 pack/run/artifact/verification 顶层 JSON shape。
- `packs run/restart` 的 approval-required 统一为 runtime/policy exit `1`；usage/config error 保持 `2`。
- default packaged smoke 增加 missing tool、approval denied-before-start、controlled partial output、corrupt state、negative verify/accept/reject、artifact lifecycle 和 prune；fake evidence 不提升为真实 conversion/verification。
- real-tool smoke 必须显式提供 Gerbv、ImageMagick、授权 fixture 和 strict baseline，并检查 executable filename/reparse/hash、plan fingerprint、content comparison、human gate、managed prune、temp 和 residual process。
- `Build-Release.ps1` 默认拒绝 dirty source；`-ReleaseAcceptance` 与 `-AllowDirtySource` 互斥，并记录 source revision/dirty flag/SDK/configuration/runtime/PDB policy、payload inventory 和 ZIP checksum。
- release/runtime docs 已更新 CHANGELOG candidate、configuration、quickstart、security、known limitations、capability、troubleshooting 和 runtime diagnostics，且明确 Accepted/Preview/Deferred。

## Fail-Closed 发现与修复

真实 smoke 没有被脚本存在或文件存在替代，实际执行暴露并关闭了两个 release blocker：

1. 第一轮在 `packs verify` 返回 `pack-tiff-metadata-mismatch`。Week 64 baseline 错把冻结 TIFF orientation 写成 `unspecified`，实际 Week 58/61/62 output 是 `top-left`。没有放宽 verifier；修正 fixture 字段，并新增真实 baseline contract test，冻结 input fingerprint、TIFF hash、尺寸、DPI、格式、压缩和方向。
2. 第二轮在 `packs resume` 返回 `pack-run-tool-changed`。smoke 没有为 read-only resume 重传 invocation-local tool bindings，运行时按设计 fail closed。修正 smoke 与 quickstart/troubleshooting 示例，resume 现在显式重传同一 tool bindings，仍重新验证 tool/input/output/policy，且不持久化 approval。
3. 第三轮完整 real-tool lifecycle smoke 通过。上述两次失败均保留在本 review，不计为成功运行，也没有通过降低校验强度掩盖。

标准并发 full test 还保留一次既有 MCP wall-clock assertion 失败：`1259 passed / 1 failed`，失败测试为 `McpDoctorReportTests.Create_reports_stdio_initialize_timeout_as_unavailable`，观测 elapsed `2.2044068s`。本周未修改 MCP implementation/assertion；单处理器受控 full run 在变更前为 `1260/1260`，加入 baseline contract 后最终为 `1261/1261`。

环境事件不属于产品成功证据：GitHub direct download 曾 timeout/reset；最终仅使用 Week 62 已用传输代理获取 bytes，并在解压前后校验冻结 archive/executable size 和 SHA256。一次本机 Magick.NET NuGet cache 缺少 `.nupkg` 与非 Windows runtime 文件；仅移除精确 `magick.net-q8-x64/14.15.0` cache 目录后 restore，随后 build/test 通过。

## Build 与测试

开发阶段相关批次真实计数分别为 security `62/62`、schema/CLI `25/25`、smoke/CLI/lifecycle `33/33`、real-smoke contract/baseline `28/28`、release-script `8/8`；最终复验使用以下可重放命令。

Targeted vertical security/runtime：

```powershell
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"
$env:DOTNET_PROCESSOR_COUNT="1"
dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-build --no-restore `
  --filter "FullyQualifiedName~GerberTiffControlledConversionTests|FullyQualifiedName~ProjectPackRunRuntimeTests|FullyQualifiedName~ManagedArtifactLifecycleTests|FullyQualifiedName~TiffVerificationTests" `
  --logger "console;verbosity=minimal"
```

结果：`62 passed / 0 failed / 0 skipped`，30 秒。

CLI/schema/smoke/release-script contract：

```powershell
dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-build --no-restore `
  --filter "FullyQualifiedName~ProjectPackCliTests|FullyQualifiedName~SmokeTestScriptTests|FullyQualifiedName~VerticalWorkflowSchemaContractTests|FullyQualifiedName~ReleaseBuildScriptTests" `
  --logger "console;verbosity=minimal"
```

结果：`25 passed / 0 failed / 0 skipped`。

最终 build/full test：

```powershell
dotnet build src\CSharpAiCli.sln -c Release --no-restore --verbosity minimal
dotnet test src\CSharpAiCli.sln -c Release --no-build --no-restore --logger "console;verbosity=minimal"
```

- build：`0 warnings / 0 errors`。
- controlled full test：`1261 passed / 0 failed / 0 skipped`，3 分 43 秒。
- 前述标准 full test 的 `1259/1` MCP timing failure 没有从计数中删除。

Default packaged smoke：

```powershell
Remove-Item Env:CAICLI_GERBER_TIFF_TOOL_SMOKE -ErrorAction SilentlyContinue
Remove-Item Env:CAICLI_GERBV_PATH -ErrorAction SilentlyContinue
Remove-Item Env:CAICLI_IMAGEMAGICK_PATH -ErrorAction SilentlyContinue
Remove-Item Env:CAICLI_GERBER_TIFF_FIXTURE -ErrorAction SilentlyContinue
Remove-Item Env:CAICLI_GERBER_TIFF_BASELINE -ErrorAction SilentlyContinue
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1 `
  -ExecutablePath artifacts\week64-validation\caicli-0.4.0-win-x64\caicli.exe
```

结果：`smoke tests passed`；real tool、daemon/API、real model 分支明确 skipped。default vertical 路径仅使用受控 fake/dry-run fixtures，不调用模型、网络或真实工具。

Real-tool packaged smoke：

```powershell
$env:CAICLI_GERBER_TIFF_TOOL_SMOKE="1"
$env:CAICLI_GERBV_PATH="<verified-outside-workspace>\gerbv.exe"
$env:CAICLI_IMAGEMAGICK_PATH="<verified-outside-workspace>\magick.exe"
$env:CAICLI_GERBER_TIFF_FIXTURE=(Resolve-Path "src\CSharpAiCli.Tests\Fixtures\GerberTiff\real\minimal-square.gbr").Path
$env:CAICLI_GERBER_TIFF_BASELINE=(Resolve-Path "src\CSharpAiCli.Tests\Fixtures\GerberTiff\real\verification-baseline.json").Path
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1 `
  -ExecutablePath artifacts\week64-validation\caicli-0.4.0-win-x64\caicli.exe
```

最终结果：`smoke tests passed`，并打印 `real Gerber/TIFF conversion, hard verification, explicit human accept/reject, and controlled managed prune passed`。

## 真实工具与输出证据

| Evidence | Size | SHA256 / identity |
|---|---:|---|
| Gerbv archive | 17,962,489 | `BB77865DA031DF83482196A6F6E9FA98298433979D995C068C4533B9B5A9EE7D` |
| `gerbv.exe` | 10,779,843 | `8BC29F799D0FD0CE522B489040E814F11B2B491E60E1E13803CBDE8C32621E4A` |
| ImageMagick archive | 22,076,074 | `DE1B67753F86A838F41754FE3BCE168C9C7AE1BD16706777FF60A3C1915EABA9` |
| `magick.exe` | 31,147,184 | `86F7225B9A72D2FC71D284F078E392A6911E2CB1F7C106DEA8ECEE91B0608C57` |
| authorized Gerber input | 257 | `D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54` |
| strict baseline | repository fixture | `970194F62A063B4F5442E726C2861B5E01086B3636F20CAB18C6B1F4EE75B9AC` |
| real TIFF output | 1,122 | `FDDFA21D29EF870D94EC953ADF757BD61585957E1519D79BB2480AFF81C89823` |

Probe version：

```text
gerbv version v2.13.0-dirty
Version: ImageMagick 7.1.2-27 Q8 x64 b661ac9:20260705 https://imagemagick.org
```

最终 plan fingerprint 为 `ABF2E954576C57E76D3022F986AB0DC7BB936289A52CC73A5ED1019B5047C402`。verification 明确得到 `file-valid=passed`、`metadata-valid=passed`、`content-compared=passed`，随后仍要求 human review；preview 报告保持 `correctnessProof=false`。这证明冻结工具/fixture/baseline 的确定性路径通过，不证明完整 CAM/EDA manufacturing correctness。

## Release Process 证据

dirty validation build 实际命令执行两次并得到相同 ZIP：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 `
  -OutputRoot artifacts\week64-validation -AllowDirtySource
```

| Artifact / field | Evidence |
|---|---|
| source revision | `5930bae89339016d2ed1b954656ac6d8596c6cff` |
| source state | `sourceDirty=true`, `releaseAcceptance=false` |
| SDK / config / runtime | `9.0.308` / `Release` / `win-x64` |
| PDB policy | `excluded` |
| publish inventory | `caicli.exe` + exact Magick.NET notice; checksum file另列 manifest，共 3 项 |
| executable | 61,460,587 bytes; `C7C3A30F5F36A32BA3E7D406C7357CD2DC433A35659ABE58CE18E00F921FDEF0` |
| ZIP | 55,966,132 bytes; `696B3917BB7FAE63A126D6DDC2B1CF816DC37F814C6DAF7F70764C1EED834704` |

默认不带 `-AllowDirtySource` 的 build 已在当前 dirty tree 上按预期拒绝；release script tests 覆盖 clean/dirty、`-ReleaseAcceptance` 互斥、revision/SDK/inventory/checksum/PDB policy。上述 ZIP 只用于验证和 packaged smoke，因为它明确记录 dirty/non-acceptance；Week 65 必须在 Week 64 变更提交后从 clean revision 重建，不能发布这个 ZIP。

## Cleanup 与一致性

- final real smoke 后 `caicli`、`gerbv`、`magick` process count 均为 `0`。
- `caicli-smoke-*`、`caicli-pack-probe-*`、`caicli-controlled-process-*`、`caicli-tiff-verification-tests-*`、`caicli-pack-run-tests-*` temp directory count 均为 `0`。
- 已校验绝对 temp root/name 后删除真实工具下载/解压目录；已删除 deterministic repeat validation 副本，保留主 dirty validation evidence。
- `git diff --check` 通过；只有仓库既有 LF -> CRLF warning。
- 10 个变更 Markdown 文件的本地链接解析通过；PowerShell parser 对 `Build-Release.ps1` / `Invoke-SmokeTests.ps1` 均为 `0` errors；baseline JSON 解析通过。
- scope scan 未发现 Pending verification 残留、缺少 `--tool-path` 的 resume 命令示例、禁区 capability source addition，或把 fake/metadata/preview 单独写成真实业务正确性通过的文案。

## Accepted / Preview / Deferred

- Accepted：仍为已发布 0.4.0 能力与 artifact；本周没有修改版本号、tag 或 Accepted release decision。
- 0.5.0 source Preview：Week 58-64 的 deterministic Project Pack、bounded discovery/plan/staging/run、typed external conversion、TIFF verification/preview、human gate、resume/restart、managed artifacts，以及本周 security/schema/smoke/release hardening。
- Gate Passed evidence：冻结 Gerbv/ImageMagick executable、CC0 fixture、strict baseline 的真实 packaged lifecycle smoke；这是特定确定性路径证据，不是通用 PCB 制造正确性证明。
- Deferred：ZIP/network input、完整 CAM/EDA manufacturing correctness、model vision hard gate、accept undo、artifact restore/quarantine recovery、background retention/quota worker、scheduler、parallel/concurrent writer/worker、lease/heartbeat、remote runner、provider routing、API control/SSE、team platform、marketplace 和 UI。

## 残余风险

- C-AICLI 不是 OS sandbox；同一 OS user 的恶意进程仍可竞争本地文件。path/hash/reparse/read-lock/no-overwrite/revalidation 降低风险，但不能提供内核隔离。
- Magick.NET/ImageMagick 是 native in-process decoder attack surface。当前使用固定版本、format allowlist、dimension/frame/pixel/memory/time limits 和串行 process-global policy；native deadlock 不能像外部进程一样 tree-kill。
- run/checkpoint/manifest 是多个原子文件，不是单 filesystem transaction；crash 仍可能留下 fail-closed split evidence，自动 repair 仍 Deferred。
- external tool output create 前仍存在同用户有限 TOCTOU；post-run identity/inventory checks 可检测，不等同于不可竞争创建。
- 真实 smoke 只覆盖一个 single-layer CC0 Gerber 和一个 single-output TIFF baseline；完整 board/drill/multi-input manufacturing semantics 未被证明。
- 既有 MCP stdio timeout wall-clock assertion 在标准并发 full test 下仍可能 flake；本周没有扩大范围修改 MCP。

## Week 65 唯一输入

1. 提交 Week 64 全部变更，确认 `git status --porcelain` 为空，并记录最终 clean commit SHA。
2. 按发布决定更新 0.5.0 version/changelog/date；不再新增或修补 vertical 功能。
3. 从 clean tree 运行 `tools\Build-Release.ps1 -ReleaseAcceptance`，要求 `sourceRevision=<clean commit>`、`sourceDirty=false`、`releaseAcceptance=true`，复核 SDK、PDB policy、notice、payload inventory、ZIP SHA256 和 deterministic rebuild。
4. 对最终 clean-source executable 重跑 full build/test、default packaged smoke 和显式 real-tool smoke；确认 cleanup 与 `git diff --check`，且复验本身不产生 source diff。
5. 仅在上述证据全部通过后作 0.5.0 Accepted 决定、tag 和发布；任一失败都阻塞发布，不以制品存在、metadata valid 或 preview 代替验收。

因此 Week 65 不需要实现新能力；当前仅剩版本与发布动作。
