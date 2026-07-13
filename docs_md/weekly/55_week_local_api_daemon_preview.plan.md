# 第 55 周 Local API / Daemon Preview Implementation Plan

状态：已验收

**Goal:** 在 job/queue/pipeline/automation/CI artifacts 稳定后，评估并可选实现本地 daemon/HTTP API/SSE preview，为 IDE/Web UI/远程控制预留入口；preview 默认关闭，仅绑定 localhost，且不能扩大 CLI 权限。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- `docs_md/weekly/54_week_ci_pr_artifacts.plan.md`
- `docs_md/release/security_model.md`

## 本周范围

- 明确 daemon/API 是否进入 0.4.0 preview；如进入，只实现 localhost-only read/control 最小入口。
- API 只暴露 job/queue/pipeline/automation 的既有操作，不新增执行权限。
- SSE 只用于 job/queue event stream preview。
- 默认关闭，必须显式命令启动。
- 增加 threat model、docs 和 smoke opt-in。

本周明确不做：

- 不绑定 0.0.0.0。
- 不做公网访问、认证平台、多用户权限或远程 agent。
- 不做浏览器 UI。
- 不让 API 绕过 approval、workspace guard、shell policy 或 disabled tools。

## 用户入口草案

```powershell
caicli daemon doctor
caicli daemon start --preview --bind 127.0.0.1 --port 8787
caicli api routes
caicli api smoke --port 8787
```

## 任务清单

- [x] Step 1: 写明 preview threat model 和默认关闭策略。
- [x] Step 2: 定义 API route contract，优先 read-only jobs/queue endpoints。
- [x] Step 3: 如实现 start，限制 localhost，并拒绝 remote bind。
- [x] Step 4: 将 API 操作全部复用 CLI service path 和 existing security boundaries。
- [x] Step 5: 增加 opt-in smoke，不进入默认 CI 前提。
- [x] Step 6: 更新 docs，将 daemon/API 标为 Preview 或 Deferred。
- [x] Step 7: 运行 build/test/smoke 并创建 `55_week_review.md`。

## 验收标准

- 默认 CLI 行为不启动 daemon。
- preview 不扩大工具权限。
- remote bind 被稳定拒绝。
- docs 明确 preview 风险和 Deferred 边界。
