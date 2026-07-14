# 第 58 周 Project Pack Contract 与 Gerber/TIFF Toolchain Spike Implementation Plan

状态：已完成（Toolchain Gate Passed；真实执行 Deferred）

**Goal:** 冻结 0.5.0 Project Pack v1 边界，并用真实 Windows 工具、授权 fixture 和 TIFF inspection 候选完成技术/许可 spike，避免后续围绕不可用工具或 fake-only contract 开发。

## 来源

- `docs_md/spec/product_positioning_and_roadmap.md`
- `docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md`
- `docs_md/plans/05_mcp_project_workflows.plan.md`
- `src/CSharpAiCli.ProjectPacks/GerberTiff/GerberTiffWorkflowPack.cs`
- `docs_md/release/final_acceptance_0.4.0.md`

## 前置检查

- 确认 0.4.0 baseline 对应干净、可引用的 Git commit/tag；记录当前 build/test/default smoke 状态。
- 现有 `GerberTiffWorkflowPack` 只是 status/validation suggestion MVP，不得把它标记为真实工具链。
- 盘点调用方实际使用的 Gerber/TIFF 工具、命令行参数、许可、输入格式、输出格式和 Windows 部署方式。

## 本周范围

- 定义 Project Pack 与 skill/pipeline/automation 的职责差异。
- 定义 pack manifest、capability、dependency、plan、run stage、artifact declaration 和 diagnostic DTO。
- 确定 CLI 命令族草案：`packs list/doctor/plan/run/resume/accept/reject`；本周只要求 list/doctor contract 或 skeleton，不执行转换。
- 对至少一个真实 Gerber/TIFF 外部工具执行 Windows spike：安装/定位、版本探测、最小转换、退出码、stdout/stderr、超时/取消行为。
- 确定至少一个可合法进入仓库的最小 fixture 和预期输出/metadata。
- 评估成熟 TIFF metadata/preview 方案，记录 license、multipage、compression、DPI、pixel format 支持。
- 定义 fake tool driver protocol，供默认 test/smoke 使用，但不将 fake 结果作为真实工具验收。

本周明确不做：

- 不实现完整 Gerber input discovery。
- 不接入 queue/job run execution。
- 不实现 staging、checkpoint、artifact prune 或 TIFF baseline compare。
- 不下载或执行 workspace 自带的未知二进制。
- 不开放用户自定义任意 hooks/scripts。

## Project Pack 边界

- Skill：为模型提供可复用指导、references 和可选 scripts。
- Pipeline：按角色组合 agent/skill/queue/job。
- Project Pack：声明确定性领域输入、外部工具依赖、结构化执行阶段、验证器和 artifact schema。
- Automation：触发已经稳定的 skill/pipeline/pack，不拥有 pack 的领域执行逻辑。

建议 pack v1 包含：

- `ProjectPackManifest`
- `ProjectPackCapability`
- `ExternalToolRequirement`
- `ExternalToolIdentity`
- `ProjectPackPlan`
- `ProjectPackStage`
- `ProjectPackArtifactDeclaration`
- `ProjectPackDiagnostic`
- `IProjectPack` / registry

Gerber/TIFF 具体 DTO 留在 `CSharpAiCli.ProjectPacks/GerberTiff`，通用 Core 不出现 Gerber 扩展名、TIFF 字段或用户机器路径。

## 工具链 Gate 记录

每个候选工具必须记录：

- 工具名称、来源、版本、license、是否允许再分发。
- Windows 可执行路径和最小支持版本。
- 静态身份检查与实际 `--version`/probe 方式。
- 输入文件/目录规则和 v1 支持参数。
- 是否产生临时文件、是否覆盖输出、是否访问网络。
- 退出码、错误输出、超时、中断和部分输出语义。
- 同一输入/版本/参数的输出是否 byte-deterministic；如果不是，哪些 metadata 可稳定比较。

真实工具不能随 release 分发时，设计 `CAICLI_GERBER_TIFF_TOOL` 或等价显式 opt-in 路径，默认 smoke 继续使用 fake driver。

## 用户入口草案

```powershell
caicli packs list
caicli packs list --output json
caicli packs doctor gerber-tiff
caicli packs doctor gerber-tiff --tool-path "<configured-tool>" --probe
```

- 静态 doctor 只检查配置、canonical path、文件 metadata/hash，不启动工具。
- `--probe` 会执行外部程序，必须展示 tool identity/risk 并走 approval。
- workspace config 不得静默指定或启动任意 executable；可执行路径优先来自 user config 或显式 CLI 参数。

## 测试计划

- Manifest/registry duplicate、unknown capability、unsupported schema。
- Tool path canonicalization、missing file、directory path、reparse point、changed hash。
- Doctor text/JSON schema 和 secret/path redaction。
- Fake tool 协议 success/failure/timeout/partial-output contract。
- Core/CLI 不硬编码 Gerber/TIFF tool path。
- 默认测试不要求安装真实工具；真实 spike 证据单独记录。

## 任务清单

- [x] Step 1: 记录 0.4.0 干净 release baseline 和本周起点验证。
- [x] Step 2: 收集实际 Gerber/TIFF 工具、参数、fixture 和验收规则。
- [x] Step 3: 至少完成一个真实工具的 Windows 命令行转换 spike。
- [x] Step 4: 完成 tool/fixture/TIFF library license 与分发结论。
- [x] Step 5: 编写 Project Pack 与 skill/pipeline/automation 的职责说明。
- [x] Step 6: 定义 Project Pack v1 manifest、dependency、plan、stage、artifact 和 diagnostic DTO。
- [x] Step 7: 定义 pack registry 与 `packs list/doctor` command contract。
- [x] Step 8: 定义 external tool identity、probe、trust/approval 和 hash-change 语义。
- [x] Step 9: 定义 fake tool driver protocol 和测试 fixture 目录布局。
- [x] Step 10: 增加 contract/registry/doctor/fake-driver tests。
- [x] Step 11: 更新 capability/known-limitations 草稿，保持 real execution 未 Accepted。
- [x] Step 12: 运行 build/test。
- [x] Step 13: 创建 `58_week_review.md`，明确 Gate 通过/阻塞结论和 Week 59 输入。

## 验收标准

- 至少一个真实工具候选在目标 Windows 环境完成最小转换，且 license/配置/版本边界明确。
- 至少一个 fixture 可合法进入自动化测试，预期结果或 metadata 可复核。
- Project Pack v1 不泄漏 Gerber/TIFF 领域字段到通用 agent/tool contracts。
- doctor 默认不执行外部程序；probe 不能绕过 approval。
- 如果真实工具 Gate 未通过，本周 review 必须标记 blocked，不得推进 fake-only release claim。

## Week 59 输入

- 已冻结的 manifest/tool identity/diagnostic schema。
- 已确认的 v1 输入扩展名、数量/大小上限和 fixture。
- 已确认的真实工具 probe 方式，但 Week 59 仍不执行转换。
