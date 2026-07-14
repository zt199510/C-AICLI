# 第 63 周 Artifact Lifecycle、Human Acceptance 与 Safe Resume Implementation Plan

状态：计划中

**Goal:** 将 0.5.0 垂直运行形成的 managed artifacts 变成可查看、校验、导出和安全清理的交付物，并补齐人工 accept/reject、失败恢复和 interrupted execute 的显式 restart 语义。

## 来源

- `docs_md/weekly/60_week_isolated_run_staging_checkpoint.plan.md`
- `docs_md/weekly/62_week_tiff_artifact_inspection_verification.plan.md`
- `src/CSharpAiCli.Core/Jobs/JobRecords.cs`
- `src/CSharpAiCli.Core/Queue/TaskQueueStore.cs`
- `docs_md/release/known_limitations.md`

## 本周范围

- 定义 managed artifact id、manifest、kind、owner run/job、path、size/hash、verification 和 retention metadata。
- 增加只读 `artifacts list/show/verify/export` text/JSON/markdown。
- 增加 `artifacts prune --dry-run` 和显式 apply 入口；只处理 managed root 内符合条件的 terminal artifacts。
- 增加 `packs accept <run-id>` / `packs reject <run-id> --reason ...` 人工结论。
- accept/reject 只允许从 `awaiting-acceptance` 进入 terminal；verification failed 不能 accept，除非未来另有显式 exception policy，本版不提供 bypass。
- 实现 safe resume：staged/ready/verifying/awaiting-acceptance 可重验恢复；所有执行 approval 重新申请。
- 为 `interrupted` 提供显式 restart plan：保留 partial evidence、创建新 attempt/output 位置、重新审批，不覆盖旧输出。
- 修复 stale running job/queue/run 的本地诊断和人工恢复入口，不实现后台 lease/heartbeat worker。
- 定义 artifact retention config/default preview，避免大 TIFF 无限增长。

本周明确不做：

- 不删除源 Gerber、baseline 或显式 workspace output。
- 不让 prune 跟随 symlink/reparse point 或删除 managed root 外路径。
- 不自动 accept 模型/工具输出。
- 不级联删除 session/trace/job，除非 manifest 明确声明其为同一 managed run 的 owned artifact；v1 建议保留 job metadata 和 tombstone。
- 不实现远程/shared artifact store、云上传或多用户 retention policy。
- 不实现并发 worker lease/heartbeat。

## 用户入口草案

```powershell
caicli artifacts list --run <run-id>
caicli artifacts show <artifact-id> --output json
caicli artifacts verify <artifact-id>
caicli artifacts export <artifact-id> --format markdown
caicli artifacts prune --older-than 30d --dry-run
caicli artifacts prune --older-than 30d --apply

caicli packs accept <run-id>
caicli packs reject <run-id> --reason "DPI mismatch"
caicli packs resume <run-id>
caicli packs restart <run-id> --from execute
```

`restart --from execute` 不是 approval bypass：必须重新显示 tool/input/output/risk，生成新 attempt id，并使用新的 no-overwrite output directory。

## Artifact Ownership 与 Prune

- `managed`：位于 C-AICLI run root，manifest 声明 owned，可 prune。
- `workspace-output`：用户显式输出路径，只记录 pointer，永不由 prune 删除。
- `source`：输入/baseline，只读 reference，永不删除。
- `external`：工具生成但不在 managed root 的未知 path，标记 boundary violation，不纳入自动清理。

Prune 必须：

- 默认 dry-run，输出候选数量、总大小、run/status/age/reason。
- 只接受 terminal accepted/rejected/failed/canceled run；pending/running/interrupted/corrupt 默认保留。
- 删除前重验 root containment、manifest ownership、reparse status 和 current hash/path identity。
- 单项失败继续处理其余项并返回 diagnostics；保留 tombstone/summary 供 job history 解释。

## 测试计划

- Artifact id/manifest/store missing/corrupt/unsupported schema。
- list/show/verify/export read-only 和 stable JSON/markdown。
- accept/reject state transitions、reason redaction、double decision rejection。
- resume eligibility 和 tool/input/policy change invalidation。
- interrupted restart creates new attempt/output，不删除 partial evidence。
- prune dry-run/apply status/age/size filters、locked files、race、reparse/outside-root protection。
- workspace-output/source/external pointers 永不删除。
- stale running diagnostics 和手动 recovery 不导致双执行。

## 任务清单

- [ ] Step 1: 定义 artifact identity/ownership/retention/tombstone schema。
- [ ] Step 2: 实现 managed artifact store/index 和 corrupt diagnostics。
- [ ] Step 3: 实现 `artifacts list/show/verify/export` renderers/CLI。
- [ ] Step 4: 实现 accept/reject human gate 和 terminal state transition。
- [ ] Step 5: 实现 safe resume eligibility/revalidation。
- [ ] Step 6: 实现 interrupted execute 的 new-attempt restart plan。
- [ ] Step 7: 实现 prune dry-run/apply、root/reparse/race protections。
- [ ] Step 8: 接入 job/queue/run correlation 和 retained tombstone。
- [ ] Step 9: 增加 lifecycle/acceptance/resume/prune/security tests。
- [ ] Step 10: 扩展 smoke：accept/reject、resume plan、prune dry-run 和受控 apply。
- [ ] Step 11: 更新 configuration/security/known limitations/troubleshooting 草稿。
- [ ] Step 12: 运行 build/test/default smoke/real-tool smoke。
- [ ] Step 13: 创建 `63_week_review.md`，列出 Week 64 hardening 输入。

## 验收标准

- 用户可从 run/job 找到所有 declared artifacts，并验证 path/hash/verification/acceptance。
- verification failed 的 run 不能被普通 accept 命令提升为 accepted。
- resume/restart 不复用 approval，不覆盖旧 artifact，不吞掉 partial evidence。
- prune 默认 dry-run，且测试证明不能删除 source、workspace output、running/interrupted/corrupt 或 managed-root 外文件。
- 大型 artifact 生命周期有明确配置、文档、统计和人工执行路径。
