# 第 59 周 Gerber/TIFF Input Discovery、Preflight 与 Conversion Plan Implementation Plan

状态：已完成

**Goal:** 在不执行转换的前提下，将 workspace 内 Gerber/钻孔输入安全地投影为 bounded inventory、tool preflight 和 deterministic conversion plan，为 Week 60 staging 和 Week 61 执行提供冻结输入。

## 来源

- `docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md`
- `docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md`
- Week 58 review 和通过的 Toolchain Gate
- `src/CSharpAiCli.Core/Workspace`
- `src/CSharpAiCli.Core/Tools/TextFileUtilities.cs`

## 前置条件

- Week 58 已确认真实工具、fixture、v1 支持输入和 license 边界。
- 未通过 Week 58 Gate 时，本周只允许完善通用 contract，不得宣称 Gerber/TIFF plan 已可用于真实转换。

## 本周范围

- 实现 Gerber/TIFF bounded directory discovery 和 input inventory。
- 使用 case-insensitive allowlist 分类 Gerber layers、drill files 和明确支持的 sidecar；未知文件只报告，不自动传给工具。
- 对文件数、单文件大小、总字节数、递归深度、路径长度和扫描时间设置上限。
- 拒绝 workspace 外路径、network path、reparse/symlink escape、device path 和不安全相对路径。
- 计算受限输入 metadata/hash，形成 immutable plan fingerprint。
- 实现 `packs doctor gerber-tiff` 的静态 preflight，以及显式审批的 tool probe。
- 实现 `packs plan gerber-tiff --input ... --output ...` text/JSON；plan 不写 workspace、不创建 job、不运行转换。
- 定义 duplicate layer、missing required input、ambiguous mapping、unsupported extension 和 limit exceeded diagnostics。

本周明确不做：

- 不执行真实或 fake conversion。
- 不复制输入到 staging。
- 不支持 ZIP 自动解压、URL、UNC/network input 或任意 glob。
- 不让模型决定 layer mapping 或工具参数。
- 不写 output directory，仅验证目标边界和 no-overwrite 语义。

## 用户入口草案

```powershell
caicli packs doctor gerber-tiff --workspace .
caicli packs doctor gerber-tiff --tool-path "<tool>" --probe --workspace .
caicli packs plan gerber-tiff --input .\samples\board-a --output-dir .\out\board-a --workspace .
caicli packs plan gerber-tiff --input .\samples\board-a --output-dir .\out\board-a --output json --workspace .
```

Plan 至少包含：

- pack/schema version。
- workspace、canonical input/output 和 source。
- tool requirement/identity/probe status。
- sorted input inventory、kind、size、SHA256。
- layer mapping 和所有 warnings/diagnostics。
- planned stages、timeout、overwrite policy、expected artifact kinds。
- plan fingerprint 和 redaction declaration。

## 实施设计

建议在 `CSharpAiCli.ProjectPacks/GerberTiff` 增加：

- `GerberTiffInputDiscovery`
- `GerberTiffInputInventory`
- `GerberTiffInputFile`
- `GerberTiffLayerClassifier`
- `GerberTiffPreflight`
- `GerberTiffConversionPlanBuilder`
- text/JSON renderer

通用 `ProjectPackPlan` 只携带通用 stage/artifact/diagnostic 字段；Gerber layer mapping 作为 pack-specific structured payload。

## 安全与稳定性

- 目录枚举必须 best-effort 但 diagnostics 明确；权限拒绝不能被当成空输入。
- 哈希读取使用 bounded/streaming IO，不一次性加载大文件。
- 枚举顺序、JSON 数组和 plan fingerprint 必须稳定排序。
- 输出存在时 plan 返回 conflict，不提供隐式 overwrite flag。
- `--tool-path`、input/output 和 diagnostic path 输出经过 redaction；不把输入内容写入 plan。
- Tool hash 在 plan 与 run 之间变化时，后续 run 必须拒绝并要求重新 plan。

## 测试计划

- 正常单层/多层/钻孔 fixture inventory。
- mixed-case extension、unknown files、duplicate layers、missing inputs。
- nested directory、depth/file-count/byte/path/time limit。
- outside workspace、`..`、absolute escape、UNC、reparse point。
- locked/unreadable file 返回 stable diagnostic，不生成可执行 plan。
- plan deterministic ordering/fingerprint 和 JSON schema。
- tool missing/version mismatch/hash changed/probe denied。
- list/doctor/plan 不创建 job/queue/run directory，不调用模型/MCP/shell。

## 任务清单

- [x] Step 1: 冻结 v1 input allowlist、required/optional layer 和 inventory limits。
- [x] Step 2: 实现 bounded input discovery 和 canonical workspace guard。
- [x] Step 3: 实现 Gerber/drill/sidecar classifier 和 ambiguity diagnostics。
- [x] Step 4: 实现 streaming size/hash metadata 和 stable sort。
- [x] Step 5: 实现 static doctor 与 approval-gated tool probe。
- [x] Step 6: 实现 conversion plan builder、fingerprint 和 no-overwrite validation。
- [x] Step 7: 增加 `packs plan gerber-tiff` text/JSON CLI。
- [x] Step 8: 增加 discovery/preflight/plan unit 与 CLI tests。
- [x] Step 9: 增加 default smoke 的 list/doctor/plan credential-free 路径。
- [x] Step 10: 更新 quickstart/security/known limitations 草稿。
- [x] Step 11: 运行 build/test/default smoke。
- [x] Step 12: 创建 `59_week_review.md`，冻结 Week 60 staging 输入契约。

## 验收标准

- 同一输入和 tool identity 产生相同 inventory ordering 与 plan fingerprint。
- 任何无法安全读取、分类或约束的输入都不能生成 runnable plan。
- doctor/plan 不执行转换、不写 workspace、不创建持久 job/run。
- tool probe 是唯一允许的本周外部进程路径，并继续经过 approval。
- JSON plan 足以让 Week 60 staging 重验输入，而不包含 raw file content。
