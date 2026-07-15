# 第 65 周 CLI 0.5.0 发布验收 Implementation Plan

状态：计划中

**Goal:** 对 Week 58-64 的 Vertical Workflow Runtime 与 Gerber/TIFF v1 做最终发布验收，生成可追溯、可复现的 0.5.0 Windows 发布包、checksum 和 Accepted/Preview/Deferred 记录。

## 来源

- `docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md`
- Week 58-64 plans/reviews
- `docs_md/release/final_acceptance_0.4.0.md`
- `tools/Build-Release.ps1`
- `tools/Invoke-SmokeTests.ps1`
- Week 64 release-process hardening 结论

## 发布前置 Gate

- Week 58-64 review 均已验收且没有遗留实现项。
- 至少一个真实工具/授权 fixture 的 conversion + TIFF verification + preview + human gate 已通过。
- default fake-tool smoke 和 real-tool opt-in smoke 均有当前 source revision 证据。
- release source 是 clean、可引用 commit；版本/tag/checksum 流程不会改变被构建的 source ref。

任一 Gate 不满足时，0.5.0 状态保持 candidate/blocked，不用文档降级方式绕过。

## 本周范围

- 汇总 Week 58-64 状态、风险和 Deferred 边界。
- 更新版本元数据到 `0.5.0`。
- 更新 CHANGELOG、installation、configuration、quickstart、security model、known limitations、capability status、troubleshooting、roadmap 和 runtime diagnostics。
- 创建 `docs_md/release/final_acceptance_0.5.0.md`。
- 从干净 source revision 执行 Release build、full tests、default smoke 和 real-tool opt-in smoke。
- 连续两次构建同一 source revision，比较目录 artifact inventory、zip size 和 SHA256。
- 验证 manifest version/runtime/framework/sourceRevision/SDK/artifact checksums。
- 记录 packaged CLI 的 version、packs/artifacts capability、missing-tool behavior 和 no-residual-process/temp evidence。
- 生成 release checksum artifact，创建 `65_week_review.md` 和 release decision。

本周明确不做：

- 不修补 Week 58-64 未完成功能；发现问题回退对应周并重新验收。
- 不新增工具、fixture、input type、pack command 或 retention policy。
- 不将真实工具二进制打入 release，除非 Week 58 license Gate 明确允许并已完成安全审查。
- 不把 scheduler、并行 worker、remote/API control、provider routing、marketplace 或 UI 标为当前能力。

## 0.5.0 Accepted 候选

- Project Pack v1 manifest/registry/tool dependency/doctor/plan。
- Gerber/TIFF bounded input inventory 和真实外部工具结构化转换。
- managed staging/run/checkpoint/cancel/safe resume/restart。
- TIFF metadata/preview/baseline verification。
- managed artifact list/show/verify/export/prune。
- human accept/reject gate 和 job/queue/run/artifact correlation。
- default fake-tool smoke 与显式 real-tool smoke。

继续 Preview/Deferred：

- 模型辅助视觉 review 只能作为非决定性提示，若实现则标 Preview。
- ZIP/network input、完整 Gerber parser、通用 C++/EDA pack。
- scheduler、parallel/concurrent writer、remote runner、provider routing。
- API control/SSE/auth/TLS/remote bind、团队权限/知识库。
- plugin marketplace、remote pack distribution、desktop/Web viewer。

## 任务清单

- [x] Step 1: 汇总 Week 58-64 review，确认没有未完成实现范围。
- [ ] Step 2: 确认真实工具/fixture/license/verification Gate 证据仍有效。
- [x] Step 3: 确认 release source clean、revision 固定且可 tag/引用。
- [x] Step 4: 更新 version/assembly/file metadata 到 `0.5.0`。
- [x] Step 5: 更新 CHANGELOG 和全部 release/runtime docs。
- [x] Step 6: 创建 `docs_md/release/final_acceptance_0.5.0.md`。
- [x] Step 7: 运行 Release build 和 full test suite。
- [x] Step 8: 构建 packaged executable，运行 default fake-tool smoke。
- [ ] Step 9: 运行 real-tool opt-in smoke，记录 tool/version/hash/fixture fingerprint。
- [x] Step 10: 连续两次从同一 clean source revision 构建 release package。
- [x] Step 11: 比较 artifact inventory、zip size/SHA256 和 manifest sourceRevision/checksums。
- [x] Step 12: 验证 packaged `caicli.exe version`、`packs list/doctor`、`artifacts list` 和 missing-tool diagnostics。
- [x] Step 13: 检查无残留 `caicli`/tool process 和 smoke/run temp directories。
- [ ] Step 14: 生成独立 checksum artifact/发布说明，创建 release tag。
- [x] Step 15: 创建 `65_week_review.md`，记录最终 Accepted/Blocked decision。

## 验收矩阵

| 能力 | 必须证据 | 失败条件 |
|---|---|---|
| Pack contract | schema/registry/doctor/plan tests + packaged smoke | unknown/unsafe pack 或 tool path 可绕过诊断 |
| 真实转换 | 授权 fixture、tool identity、execution log、output inventory | 只有 fake driver 或真实工具结果不可复核 |
| 隔离/恢复 | source hash、run state、cancel/interrupted/resume/restart tests | 源输入被修改、双执行或旧 approval 被复用 |
| TIFF verification | metadata/baseline/preview/report + corrupt/oversized tests | 仅检查文件存在或 preview 被当作 correctness proof |
| Artifact lifecycle | list/show/verify/export/prune + ownership/security tests | prune 可删除 source/workspace/outside-root 文件 |
| Human gate | awaiting-acceptance -> accepted/rejected evidence | verification failed 可被普通 accept 绕过 |
| Release | clean source revision、双构建、manifest、zip SHA256 | 制品不能从记录的 source ref 重现 |

## 验证基线

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
$env:CAICLI_GERBER_TIFF_TOOL_SMOKE = "1"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
Remove-Item Env:CAICLI_GERBER_TIFF_TOOL_SMOKE
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
artifacts\release\caicli-0.5.0-win-x64\caicli.exe version
Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.5.0-win-x64.zip
```

## Release Decision 标准

- 只有所有 Gate、验收矩阵和验证命令通过，才将 roadmap/capability status 标记为 0.5.0 Accepted。
- 真实工具缺失、真实 fixture 未通过、artifact prune 越界、resume 双执行或 package source revision 不可复现均为 release blocker。
- 外部工具不能再分发不构成 blocker，只要配置/doctor/docs/opt-in smoke 和用户安装路径完整可复核。
