# 第 65 周 CLI 0.5.0 发布验收 Implementation Plan

状态：已完成；0.5.0 Accepted

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
- Release build、full tests、default fake-tool smoke、packaged diagnostics 和 cleanup 通过。
- 同一 clean source revision 的连续 package build 可复现，manifest/inventory/checksum 一致。
- release source 是 clean、可引用 commit；版本/tag/checksum 流程不会改变被构建的 source ref。

Real-tool opt-in smoke 是 caller 提供 reviewed Gerbv/ImageMagick、fixture 和 baseline 时的可选环境验证，不是 0.5.0 发布 Gate。未运行时必须如实记录，且不得把 fake、文件存在、metadata 或 preview 描述为真实 CAM/EDA 业务正确性通过。

## 本周范围

- 汇总 Week 58-64 状态、风险和 Deferred 边界。
- 更新版本元数据到 `0.5.0`。
- 更新 CHANGELOG、installation、configuration、quickstart、security model、known limitations、capability status、troubleshooting、roadmap 和 runtime diagnostics。
- 创建 `docs_md/release/final_acceptance_0.5.0.md`。
- 从干净 source revision 执行 Release build、full tests 和 default smoke；记录 real-tool opt-in smoke 的可选状态。
- 连续两次构建同一 source revision，比较目录 artifact inventory、zip size 和 SHA256。
- 验证 manifest version/runtime/framework/sourceRevision/SDK/artifact checksums。
- 记录 packaged CLI 的 version、packs/artifacts capability、missing-tool behavior 和 no-residual-process/temp evidence。
- 生成 release checksum artifact，创建 `65_week_review.md` 和 release decision。

本周明确不做：

- 不修补 Week 58-64 未完成功能；发现问题回退对应周并重新验收。
- 不新增工具、fixture、input type、pack command 或 retention policy。
- 不将真实工具二进制打入 release，除非 Week 58 license Gate 明确允许并已完成安全审查。
- 不把 scheduler、并行 worker、remote/API control、provider routing、marketplace 或 UI 标为当前能力。

## 0.5.0 Accepted 范围

- Project Pack v1 manifest/registry/tool dependency/doctor/plan。
- Gerber/TIFF bounded input inventory 和真实外部工具结构化转换。
- managed staging/run/checkpoint/cancel/safe resume/restart。
- TIFF metadata/preview/baseline verification。
- managed artifact list/show/verify/export/prune。
- human accept/reject gate 和 job/queue/run/artifact correlation。
- default fake-tool smoke，以及显式、可选的 real-tool environment smoke 入口。

继续 Preview/Deferred：

- 模型辅助视觉 review 只能作为非决定性提示，若实现则标 Preview。
- ZIP/network input、完整 Gerber parser、通用 C++/EDA pack。
- scheduler、parallel/concurrent writer、remote runner、provider routing。
- API control/SSE/auth/TLS/remote bind、团队权限/知识库。
- plugin marketplace、remote pack distribution、desktop/Web viewer。

## 任务清单

- [x] Step 1: 汇总 Week 58-64 review，确认没有未完成实现范围。
- [x] Step 2: 确认真实工具/fixture/license/verification 仅作为可选环境验证，不作为 release Gate。
- [x] Step 3: 确认 release source clean、revision 固定且可 tag/引用。
- [x] Step 4: 更新 version/assembly/file metadata 到 `0.5.0`。
- [x] Step 5: 更新 CHANGELOG 和全部 release/runtime docs。
- [x] Step 6: 创建 `docs_md/release/final_acceptance_0.5.0.md`。
- [x] Step 7: 运行 Release build 和 full test suite。
- [x] Step 8: 构建 packaged executable，运行 default fake-tool smoke。
- [x] Step 9: 记录 real-tool opt-in smoke 未运行且为 optional/non-blocking；未伪造 tool evidence。
- [x] Step 10: 连续两次从同一 clean source revision 构建 release package。
- [x] Step 11: 比较 artifact inventory、zip size/SHA256 和 manifest sourceRevision/checksums。
- [x] Step 12: 验证 packaged `caicli.exe version`、`packs list/doctor`、`artifacts list` 和 missing-tool diagnostics。
- [x] Step 13: 检查无残留 `caicli`/tool process 和 smoke/run temp directories。
- [x] Step 14: 生成独立 checksum artifact 和发布说明；release tag 不属于本次标记操作，尚未创建。
- [x] Step 15: 创建 `65_week_review.md`，记录最终 Accepted decision。

## 验收矩阵

| 能力 | 必须证据 | 失败条件 |
|---|---|---|
| Pack contract | schema/registry/doctor/plan tests + packaged smoke | unknown/unsafe pack 或 tool path 可绕过诊断 |
| 受控转换契约 | typed adapter/argv/tool identity/approval/process boundary tests + default fixture smoke | 可绕过固定参数、审批、工具身份或 workspace/run 边界 |
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
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
artifacts\release\caicli-0.5.0-win-x64\caicli.exe version
Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.5.0-win-x64.zip
```

Reviewed external tools 可用时，可另外设置 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` 运行环境验证；该命令不属于 release Gate。

## Release Decision 标准

- 所有发布前置 Gate、验收矩阵和必需验证命令通过后，将 roadmap/capability status 标记为 0.5.0 Accepted。
- Artifact prune 越界、resume 双执行或 package source revision 不可复现是 release blocker。
- 真实工具缺失或 opt-in smoke 未运行不构成 release blocker；配置、doctor、文档和可选 smoke 入口必须完整，且不得声称未经验证的真实 CAM/EDA 业务正确性。
