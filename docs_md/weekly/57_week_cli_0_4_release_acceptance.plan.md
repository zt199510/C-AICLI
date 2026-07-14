# 第 57 周 CLI 0.4.0 发布验收 Implementation Plan

状态：已验收

**Goal:** 对 Week 50-56 的工程自动化平台化能力做最终验收，生成 0.4.0 发布记录和发布包。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- Week 50-56 周计划与回顾
- `docs_md/release/final_acceptance_0.3.3.md`
- `tools/Build-Release.ps1`
- `tools/Invoke-SmokeTests.ps1`

## 本周范围

- 回归 0.3.3 release line。
- 回归 Week 50-56 automation platform 能力。
- 更新版本元数据到 `0.4.0`。
- 更新 release docs。
- 生成 deterministic 0.4.0 release package。
- 记录 final acceptance、zip size 和 SHA256。

## 任务清单

- [x] Step 1: 汇总 Week 50-56 review 状态，列出未完成项。
- [x] Step 2: 确认所有 Deferred/Preview 能力边界仍准确。
- [x] Step 3: 更新 version metadata 到 `0.4.0`。
- [x] Step 4: 更新 CHANGELOG。
- [x] Step 5: 更新 configuration、quickstart、security model、known limitations、capability status、troubleshooting。
- [x] Step 6: 创建 `docs_md/release/final_acceptance_0.4.0.md`。
- [x] Step 7: 运行 `dotnet build src\CSharpAiCli.sln -c Release`。
- [x] Step 8: 运行 `dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- [x] Step 9: 运行 `tools\Invoke-SmokeTests.ps1`。
- [x] Step 10: 运行 `tools\Build-Release.ps1` 两次并确认 zip SHA256 deterministic。
- [x] Step 11: 确认 packaged `caicli.exe version` 输出 `0.4.0`。
- [x] Step 12: 创建 `57_week_review.md`，记录 release decision。

## 执行记录

- Step 1：Week 50-56 review 均为“已验收”。没有遗留实现范围；未完成项仅为 Week 57 发布工作：版本元数据与发布路径切换到 `0.4.0`、发布文档一致性、最终 build/test/default smoke、Local API Preview daemon opt-in smoke、deterministic 双构建、packaged version/zip size/SHA256 和 final acceptance/review。
- Step 2：Accepted 仅覆盖本地 job/artifact、手动 queue、固定顺序 pipeline、workspace-local automation validate/plan/dry-run/manual 和 provider-neutral CI artifact；Local API/daemon 仍为 default-off、IPv4 loopback-only、read-only Preview。Scheduler、concurrent/remote worker、provider API、control/SSE、authentication/TLS、remote bind、job/artifact retention/delete、remote/http MCP 与真实 Gerber/TIFF 执行继续 Deferred。

## 验收标准

- Release build/test/smoke 均通过。
- 0.4.0 release zip 存在并记录 size/SHA256。
- job history、artifact store、queue、pipeline、automation、CI artifacts 和 preview/deferred API 边界有 docs 与 smoke/test 证据。
- 默认 smoke credential-free；real model smoke 和 daemon/API smoke 仍 opt-in。
- capability status 准确标记 Accepted、Preview、Deferred。

## 验证基线

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
artifacts\release\caicli-0.4.0-win-x64\caicli.exe version
Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.4.0-win-x64.zip
```
