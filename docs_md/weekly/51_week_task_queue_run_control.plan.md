# 第 51 周 Task Queue 与 Run Control Implementation Plan

状态：已验收

**Goal:** 在 Week 50 job history/artifact store 底座之上，引入本地 task queue v1 和 run control，让工程自动化任务可以形成 pending/running/succeeded/failed/canceled 的可审计状态流，同时保持所有实际执行仍走现有 `exec`/`skills` 安全路径。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- `docs_md/weekly/50_week_job_history_artifact_store.plan.md`
- `docs_md/release/final_acceptance_0.3.3.md`

## 本周范围

- 定义 `TaskQueueItem`、queue state transition、run attempt 和 queue diagnostics。
- 增加本地 `queue add/list/show/run/cancel` 或等价 CLI，优先支持 text/json。
- `queue add` 只记录待执行 request，不立即绕过审批执行。
- `queue run` 复用 `exec`/`skills run` request builder、job store、approval、workspace guard、shell policy 和 tool boundary。
- 增加 cancel/retry/cleanup 的最小语义，但不做后台常驻。

本周明确不做：

- 不做 daemon、scheduler、远程 runner 或并发 worker pool。
- 不做多角色 pipeline。
- 不做 CI/PR provider integration。
- 不让 queue item 自动提升权限或覆盖用户 approval 设置。

## 用户入口草案

```powershell
caicli queue add exec --workspace . -- "Review @folder:src"
caicli queue add skill review-only --workspace . -- "@file:README.md"
caicli queue list
caicli queue list --output json
caicli queue show <queue-id>
caicli queue run <queue-id>
caicli queue cancel <queue-id>
caicli queue cleanup --status succeeded --older-than-days 30
```

## 任务清单

- [x] Step 1: 定义 queue item、status、attempt、error code 和 JSON schema。
- [x] Step 2: 实现本地 queue store，复用 Week 50 storage/redaction 策略。
- [x] Step 3: 增加 `queue add/list/show` text/json 命令。
- [x] Step 4: 实现 `queue run <id>`，将 queue item 展开为受控 `exec` 或 `skills run`。
- [x] Step 5: 实现 `queue cancel <id>` 的 pending-only 语义和稳定错误码。
- [x] Step 6: 实现最小 cleanup，避免删除 running/unknown 状态。
- [x] Step 7: 将 queue run 结果写入 job history，并建立 queue->job pointer。
- [x] Step 8: 增加 queue store、CLI、state transition、redaction 和 security boundary tests。
- [x] Step 9: 更新 smoke script 与 release docs。
- [x] Step 10: 运行 build/test/smoke 并创建 `51_week_review.md`。

## 验收标准

- queue 状态流稳定且可测试：`pending`、`running`、`succeeded`、`failed`、`canceled`。
- `queue run` 不绕过 approval、workspace guard、shell policy、disabled tools 或 expert/skill boundary。
- queue list/show JSON 可解析，text 可读。
- 默认 smoke credential-free。
