# 第 56 周 Automation Security / Smoke / Docs Hardening Implementation Plan

状态：计划中

**Goal:** 对 Week 50-55 引入的 job、queue、pipeline、automation、CI artifacts 和 API preview 做安全、观测、smoke、文档收口，避免在 release acceptance 周继续扩大范围。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- Week 50-55 review 文档
- `docs_md/release/security_model.md`
- `tools/Invoke-SmokeTests.ps1`

## 本周范围

- 回归 approval、workspace guard、secret redaction、dirty workspace、shell policy、disabled tools、MCP startup boundary。
- 加固 job/queue/artifact storage cleanup 和 corrupt record diagnostics。
- 加固 text/json schema docs 与 smoke 覆盖。
- 更新 release docs、known limitations、capability status 和 troubleshooting。
- 不新增大功能，只修正 scope drift、文档不一致和验收缺口。

本周明确不做：

- 不新增新的 automation target。
- 不新增 provider integration。
- 不扩展 daemon/API preview。
- 不做 0.5.0 垂直能力。

## 任务清单

- [ ] Step 1: 汇总 Week 50-55 Deferred/Preview 边界。
- [ ] Step 2: 回归所有新增命令是否只读/写入边界准确。
- [ ] Step 3: 增加 redaction/security regression tests。
- [ ] Step 4: 增加 smoke 覆盖 job/queue/pipeline/automation/CI artifact 的 credential-free 路径。
- [ ] Step 5: 更新 release docs 和 runtime logging diagnostics。
- [ ] Step 6: 修正 capability status 中 Accepted/Preview/Deferred 标记。
- [ ] Step 7: 运行 build/test/smoke。
- [ ] Step 8: 创建 `56_week_review.md`，列出 release acceptance 前剩余问题。

## 验收标准

- 0.4.0 新增能力都有 text/json/trace/report 或 job artifact 复核路径。
- 默认 smoke 不依赖模型凭据、不启动 daemon、不访问网络。
- docs 准确区分 current、preview、deferred。
- Week 57 可以只做 release acceptance，不再补实现范围。
