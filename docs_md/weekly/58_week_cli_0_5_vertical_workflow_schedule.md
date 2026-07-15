# C# AI CLI 0.5.0 Vertical Workflow Runtime 与 Gerber/TIFF v1 排期

更新时间：2026-07-15

状态：执行中；Week 58-63 已完成，Week 64-65 待执行

逐周执行提示统一维护在 `docs_md/weekly/58_65_week_execution_prompts.md`，用于把本排期和各周 plan 直接交给执行 Agent。

## 起点确认

0.4.0 已完成工程自动化平台化功能验收。当前可复用基础包括：

- `exec` 的真实模型/离线契约、approval、workspace guard、shell policy、trace、session 和 `taskReport`。
- 本地 job history/artifact pointers、手动 queue、固定顺序 multi-role pipeline、workspace-local automation manual trigger 和 provider-neutral CI artifacts。
- `skills`、expert profiles、workflow profiles、stdio MCP v1 和独立的 `CSharpAiCli.ProjectPacks` 程序集。
- Gerber/TIFF 现有代码只读取 `docs_md/plans` 并返回 validation command suggestion，不包含真实输入发现、转换、TIFF 验证或 artifact 生命周期。

进入 Week 58 前必须确认 0.4.0 release baseline 来自可引用的干净提交，并保留 build/test/smoke/checksum 证据。该动作是 0.5.0 的起点检查，不新增 0.4.x 功能范围。

## 产品目标

0.5.0 的目标不是继续横向堆叠通用 Agent 功能，而是打通第一个真实垂直工程闭环：

```text
pack/tool doctor
-> bounded input discovery
-> deterministic plan
-> explicit approval
-> isolated conversion
-> TIFF verification and preview
-> human accept/reject
-> job/artifact/report/history
-> safe resume or prune
```

版本名称建议：`0.5.0 Vertical Workflow Runtime & Gerber/TIFF v1`。

## 关键范围决策

- Project Pack 是确定性领域工具和验证流程，不等同于 model-guided skill，也不替代 multi-role pipeline。
- 真实转换交给经过 Week 58 验证的成熟外部工具；C-AICLI 负责工具身份、参数边界、审批、执行、证据和报告，不从零实现完整 Gerber parser。
- v1 输入优先限定为 workspace 内的 Gerber/钻孔文件目录。ZIP、网络路径和任意归档解压默认 Deferred，除非 Week 58 形成单独安全结论。
- 每次运行使用独立 managed run directory。源输入只读，输出不覆盖，workspace 输出必须显式指定。
- checkpoint 不持久化 approval bypass。resume 前重新校验工具身份、输入/输出边界和当前策略；崩溃发生在 execute 阶段时不得自动重跑。
- artifact retention/prune 只处理 C-AICLI managed artifacts，不删除源输入或显式 workspace 输出。
- 默认测试和 smoke 使用受控 fake tool/fixture；真实工具 smoke 必须显式 opt-in 并记录工具名、版本和哈希。

## 明确非目标

- 不自研完整 RS-274X/Excellon parser 或渲染引擎。
- 不同时建设通用 C++ Agent、完整 EDA 平台或多种垂直行业 pack。
- 不做后台 scheduler、Windows service、并发写 worker、远程 runner 或 webhook。
- 不做自动 model/provider routing、团队权限、知识库、远程控制或多用户 API。
- 不做插件 marketplace、远程 pack 下载、自动更新或签名分发平台。
- 不做桌面/Web artifact viewer；0.5.0 保持 CLI-first，可生成本地 PNG preview/contact sheet 和报告。
- 不开放任意 repo script hooks。只实现 typed、pack-owned、经过信任和审批边界的 lifecycle stages/validators。

## 0.5.0 成功标准

- 在干净 Windows 环境使用一个可合法分发或明确配置的真实工具，对授权 fixture 完成 Gerber -> TIFF 转换。
- 工具缺失、版本不兼容、输入缺失/超限、执行超时、部分输出、TIFF 无效和 baseline mismatch 都有稳定错误码。
- `packs list/doctor/plan` 和 default smoke credential-free、model-free、network-free。
- `packs run` 使用结构化参数，不拼接任意 shell 字符串，并继续经过 approval、workspace/output guard、timeout、redaction 和 trace。
- run state 可安全 cancel/resume；execute 中断不会被静默判定 succeeded 或自动重复执行。
- job、pack run、artifact manifest、TIFF verification 和人工 accept/reject 可以相互追踪，但不创建第二套 task report truth。
- managed artifact 可 list/show/verify/export/prune；prune 有 dry-run、范围保护和 reparse-point 防护。
- release build/test/default smoke/real-tool opt-in smoke/package reproducibility 和 source revision evidence 均通过。

## 阶段划分

| 阶段 | 周次 | 主题 | 目标 |
|---|---:|---|---|
| Phase 20 | 58-59 | Pack contract、真实工具 Gate 与输入计划 | 确定真实工具/fixture，建立 Project Pack、tool identity、input inventory 和 doctor/plan 契约。 |
| Phase 21 | 60-61 | 隔离运行与真实转换 | 建立 managed staging/checkpoint，再通过结构化外部进程适配完成真实转换。 |
| Phase 22 | 62-63 | TIFF 验证、artifact 生命周期与人工验收 | 形成可复核输出、safe resume、accept/reject 和 managed retention/prune。 |
| Phase 23 | 64-65 | Hardening 与 0.5.0 release acceptance | 安全/故障/smoke/docs 收口，完成版本、制品和发布证据。 |

## 逐周计划

| 周 | 计划文档 | 状态 | 主要目标 | 周末验收 |
|---:|---|---|---|---|
| 58 | `58_week_project_pack_contract_toolchain_spike.plan.md` | 已完成 | 定义 Project Pack v1，并完成真实工具、fixture、TIFF metadata 库的技术/许可 Gate。 | Gate Passed；Gerbv/ImageMagick/LibTIFF、CC0 fixture、契约和 Deferred 边界见 `58_week_review.md`。 |
| 59 | `59_week_gerber_tiff_discovery_preflight.plan.md` | 已完成 | 实现 bounded input inventory、tool doctor/preflight 和 deterministic conversion plan。 | list/doctor/plan text+JSON 可用；不执行转换；路径、reparse、文件数/大小边界有测试。 |
| 60 | `60_week_isolated_run_staging_checkpoint.plan.md` | 已完成 | 建立 managed run directory、staging、run state machine、checkpoint/cancel/resume 基础。 | fake driver 可完成阶段流转；源输入不变；execute 中断进入明确状态且不自动重跑。 |
| 61 | `61_week_gerber_tiff_controlled_conversion.plan.md` | 已完成 | 接入真实外部工具，完成审批式结构化执行、日志、timeout 和 output capture。 | Gate Passed；Gerbv/ImageMagick typed adapter、授权 fixture 真实 conversion、run/job/artifact evidence 与 opt-in smoke 见 `61_week_review.md`；TIFF verification 仍 Deferred。 |
| 62 | `62_week_tiff_artifact_inspection_verification.plan.md` | 已完成 | TIFF metadata、preview/contact sheet、baseline verification 与报告。 | bounded Magick.NET decoder、strict baseline、exact/pixel comparison、stable JSON/markdown、managed preview、awaiting-acceptance gate 与 real-tool smoke 见 `62_week_review.md`。 |
| 63 | `63_week_artifact_lifecycle_acceptance_resume.plan.md` | 已完成 | artifact list/show/verify/export/prune、accept/reject 和 safe resume/restart。 | strict artifact manifest/index、human hard gate、new-attempt restart、managed prune/tombstone 与 local-only smoke 见 `63_week_review.md`；本机 real-tool smoke 未配置并明确留给 Week 64 重验。 |
| 64 | `64_week_vertical_workflow_security_smoke_docs_hardening.plan.md` | 计划中 | 对 Week 58-63 做安全、corrupt-state、smoke、文档和 release-process 收口。 | adversarial paths/redaction/crash/cleanup 回归；default/real-tool smoke 边界准确。 |
| 65 | `65_week_cli_0_5_release_acceptance.plan.md` | 计划中 | 0.5.0 最终验收、版本、双构建、source revision、发布包和 final acceptance。 | build/test/smoke 通过；干净 source ref、zip size/SHA256、manifest revision 和 Deferred 清单完整。 |

## Critical Gate

Week 58 必须回答以下问题，否则不得进入 Week 61 真实转换实现：

1. 真实转换工具是什么，Windows 安装/许可/再分发边界是什么？
2. CLI 如何稳定探测工具版本和能力，哪些参数是 v1 支持集？
3. 哪些 Gerber/钻孔 fixture 可合法进入测试仓库，预期 TIFF 如何建立？
4. TIFF metadata/preview 使用哪个成熟库或工具，许可和多页/压缩支持是否满足？
5. 真实工具不可再分发时，release smoke 如何通过显式路径和 opt-in 环境变量运行？

如果 Gate 未通过，允许 Week 58 形成 blocked review，但不允许以 fake-only pack 宣称 0.5.0 真实垂直能力完成。

## 公共验证基线

每周至少运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

Week 59 起涉及 CLI/存储/pack 的周额外运行默认 packaged smoke。Week 61 起增加独立的真实工具 opt-in smoke，但默认 smoke 不得依赖外部工具或模型凭据。

## 周回顾要求

每周创建对应 `NN_week_review.md`，至少记录：

- 实际完成范围和未完成项。
- build/test/smoke 命令与计数。
- 工具/fixture/许可或安全 Gate 结论。
- Accepted/Preview/Deferred 变化。
- 下一周允许依赖的稳定契约。
