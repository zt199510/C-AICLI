# 第 54 周 CI/PR Artifacts Implementation Plan

状态：计划中

**Goal:** 为 CI/PR 场景提供稳定的 JSON/markdown/check summary artifacts 和 deterministic exit-code policy，让 0.4.0 的 job、queue、pipeline、automation 结果可以被外部流水线消费，但不绑定具体云端平台。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- `docs_md/weekly/50_week_job_history_artifact_store.plan.md`
- `docs_md/weekly/53_week_local_automation_commands.plan.md`

## 本周范围

- 定义 CI artifact schema：summary、check result、annotations、artifact pointers。
- 增加 `ci summarize` 或 `jobs export --format ci-json` 等价入口。
- 支持 deterministic exit-code policy：success/warning/failure/config-error。
- 输出 markdown summary，适合 GitHub Actions/Azure DevOps 手动接入。
- 只输出 artifact，不做 provider API 调用。

本周明确不做：

- 不直接调用 GitHub/GitLab/Azure DevOps API。
- 不创建 PR comment。
- 不做 webhook 或远程 callback。
- 不上传 artifact 到云端。

## 用户入口草案

```powershell
caicli jobs export <job-id> --format ci-json
caicli ci summarize --job <job-id> --output json
caicli ci summarize --job <job-id> --markdown-path .caicli/reports/ci-summary.md
caicli ci check --job <job-id> --fail-on risks
```

## 任务清单

- [x] Step 1: 定义 CI summary/check/annotation schema。
- [x] Step 2: 增加 job->CI artifact renderer。
- [x] Step 3: 实现 `ci summarize/check` 或等价 `jobs export` 格式。
- [x] Step 4: 明确 exit-code policy 并增加 tests。
- [x] Step 5: 确保 CI artifact 不包含 raw secrets、raw references、raw tool args 或完整 diff。
- [x] Step 6: 增加 smoke 覆盖 credential-free CI artifact generation。
- [x] Step 7: 更新 docs，给出 GitHub Actions/Azure DevOps 的手动调用示例但不内置 provider integration。
- [x] Step 8: 运行 build/test/smoke 并创建 `54_week_review.md`。

## 验收标准

- CI JSON schema 稳定可解析。
- markdown/check summary 可以从 job record 生成。
- exit code 行为 deterministic。
- 不做 provider-specific side effects。
