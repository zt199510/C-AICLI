# 第 64 周 Vertical Workflow Security、Smoke、Docs 与 Release Process Hardening Implementation Plan

状态：计划中

**Goal:** 对 Week 58-63 的 Project Pack、Gerber/TIFF、external tool、staging、checkpoint、TIFF verifier、artifact lifecycle 和 human gate 做系统性收口，使 Week 65 只承担发布验收，不继续补功能。

## 来源

- `docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md`
- Week 58-63 plans/reviews
- `docs_md/release/security_model.md`
- `docs_md/spec/runtime_logging_diagnostics.md`
- `tools/Invoke-SmokeTests.ps1`
- `tools/Build-Release.ps1`

## 本周范围

- 汇总 Week 58-63 Accepted/Preview/Deferred 和所有未完成项。
- 威胁建模 external executable、input discovery、staging copy、decoder、baseline、preview、resume/restart、accept/reject 和 prune。
- 回归 approval、workspace/run-root guard、dirty workspace、secret redaction、disabled tools、MCP、shell 和 trace/session/job/report 边界。
- 增加 adversarial path、reparse、TOCTOU/tool-swap、corrupt state、disk full、locked file、process leak、oversized TIFF 和 prune race tests。
- 固化 text/JSON schema、error code、exit policy 和 default/real-tool smoke。
- 更新 configuration、quickstart、security model、known limitations、capability status、troubleshooting、runtime diagnostics 和 CHANGELOG candidate 内容。
- 加固 release reproducibility：构建前 clean-tree/source revision 检查、manifest commit SHA、PDB/source revision 策略和 checksum 生成流程。
- 不新增大功能或新的 pack/tool/input type。

本周明确不做：

- 不新增第二个 vertical pack。
- 不增加 ZIP/network input、scheduler、parallel writer、remote runner/provider API。
- 不提升 daemon/API Preview。
- 不引入 UI、marketplace、plugin distribution 或 arbitrary hooks。
- 不在 hardening 周改变已经冻结的 tool argument/verification semantics；发现不安全设计时回退为 Deferred 或阻塞 release。

## 安全回归矩阵

至少覆盖：

- Input：outside workspace、UNC/device path、reparse、case collision、huge tree、file mutation during hash/copy。
- Tool：path swap、hash/version change、argument injection、untrusted env、unexpected child process、timeout/cancel leak。
- Output：outside run root、overwrite attempt、unexpected files、partial/oversized output、post-run mutation。
- TIFF：malformed/truncated/decompression bomb、unsupported compression/page count、preview write escape。
- State：corrupt/unsupported run/artifact schema、stale running、invalid transition、resume after policy/tool/input change。
- Cleanup：prune reparse/race/locked/outside-root、source/workspace output preservation。
- Diagnostics：secret-like filenames/arguments/reasons/stdout/stderr/path redaction，JSON 不能直接序列化未脱敏 exception。

## Smoke 分层

默认 packaged smoke：

- 不需要模型凭据、真实工具或网络。
- 使用受控 fake tool + fixture 覆盖 packs list/doctor/plan/run/stage/verify/preview/accept/reject/artifacts/prune dry-run。
- 覆盖 approval denied、tool missing、partial output、corrupt state 和 safe cleanup。

真实工具 opt-in smoke：

- 仅 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` 启用。
- 要求显式 tool path/fixture/baseline；记录工具名、version、SHA256 和 fixture fingerprint，不打印秘密。
- 完成真实 conversion、verification、preview 和 artifact checks；结束后无残留进程/临时目录。

真实模型 smoke 与真实工具 smoke 独立；0.5.0 垂直确定性路径不得要求模型。

## Release Process Hardening

- `Build-Release.ps1` 在 release acceptance 模式拒绝 dirty tracked worktree，或显式记录 dirty=false/source revision。
- `release-manifest.json` 增加 `sourceRevision`、SDK version、build configuration 和 artifact inventory/checksums。
- 固定是否包含 PDB；包含时承认 source revision 会影响 hash，并保证从同一 clean commit 双构建一致。
- 最终 checksum 不在改变 release source commit 的同一个提交循环中维护；优先生成独立 checksum artifact/release notes。
- 增加 release script tests 覆盖 source revision、dirty tree policy、manifest 和 deterministic zip。

## 任务清单

- [ ] Step 1: 汇总 Week 58-63 review、未完成项和 release blockers。
- [ ] Step 2: 完成 vertical workflow threat model 和命令读写/执行边界表。
- [ ] Step 3: 增加 input/tool/output/TIFF/state/prune adversarial tests。
- [ ] Step 4: 增加 redaction、corrupt diagnostics 和 process/temp cleanup tests。
- [ ] Step 5: 固化 pack/run/artifact/verification JSON schema、error code 和 exit policy。
- [ ] Step 6: 扩展 default fake-tool packaged smoke。
- [ ] Step 7: 固化 real-tool opt-in smoke 和环境前置诊断。
- [ ] Step 8: 加固 Build-Release clean source/sourceRevision/checksum 流程和测试。
- [ ] Step 9: 更新全部 release/runtime docs，准确区分 Accepted/Preview/Deferred。
- [ ] Step 10: 运行 targeted security tests、full build/test/default smoke/real-tool smoke。
- [ ] Step 11: 执行 `git diff --check` 和 docs link/scope consistency 检查。
- [ ] Step 12: 创建 `64_week_review.md`，列出 Week 65 唯一剩余发布动作。

## 验收标准

- Week 58-63 功能均有成功、失败、恶意输入、corrupt state 和 cleanup 证据。
- 默认 smoke local-only/fake-only；真实工具 smoke 明确 opt-in 且实际通过至少一次。
- docs 不把 preview、fake driver、格式有效或 preview image 误写成业务验证通过。
- release script 能把制品绑定到干净 source revision，并避免提交前制品哈希不可复现问题。
- Week 65 不需要新增实现范围；否则 Week 64 不得标记已验收。
