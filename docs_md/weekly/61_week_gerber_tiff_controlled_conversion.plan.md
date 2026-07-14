# 第 61 周 Gerber/TIFF Controlled Real Conversion Implementation Plan

状态：计划中

**Goal:** 将 Week 58 选定的真实 Gerber/TIFF 工具接入 Week 60 隔离运行状态机，通过结构化参数、显式审批和有界进程控制完成第一条真实 Gerber -> TIFF 转换路径。

## 来源

- `docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md`
- `docs_md/weekly/60_week_isolated_run_staging_checkpoint.plan.md`
- Week 58 Toolchain Gate 和 Week 60 review
- `src/CSharpAiCli.Core/Shell/RestrictedShellRunner.cs`
- `src/CSharpAiCli.Core/Approvals`
- `src/CSharpAiCli.Core/Diagnostics`

## 前置条件

- 真实工具和 fixture Gate 已通过，tool path/version/license 和 v1 参数集已冻结。
- managed run/staging/checkpoint 已验收；如果 Week 60 仍可能双执行或自动恢复 running，不进入真实工具接入。

## 本周范围

- 实现 pack-owned external process adapter，使用 `ProcessStartInfo.ArgumentList` 或等价结构化参数，不将不可信值拼接为 shell command。
- approval request 展示 canonical tool path、SHA256/version、input/output、planned operations、timeout 和 overwrite policy。
- 仅允许冻结的参数模板；模型、workspace manifest 和输入文件名不能注入额外开关。
- 在 managed working/output 目录执行，源输入只读；拒绝工具将输出写到声明目录之外。
- 有界捕获 stdout/stderr、exit code、duration、timeout/cancel 和 process-tree cleanup。
- 检测 missing output、unexpected output、partial output、output-too-large 和 changed tool identity。
- 将 execution event、redacted log、tool identity 和 output pointers 写入 run/job/artifact evidence。
- 默认 smoke 使用 fake driver；新增显式 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` 真实工具 smoke。

本周明确不做：

- 不让模型生成命令行参数。
- 不通过通用 shell 执行整段命令文本。
- 不支持批量并发转换或后台 worker。
- 不把 TIFF 存在视为验证成功；Week 62 负责内容/metadata 验证。
- 不覆盖既有 managed artifact 或显式 workspace output。
- 不自动安装、下载或更新外部工具。

## 用户入口草案

```powershell
caicli packs run gerber-tiff --plan <plan.json>
caicli packs run gerber-tiff --input .\samples\board-a --output-dir .\out\board-a
caicli packs runs show <run-id> --output json
```

建议 `run` 行为：

- 没有 plan file 时内部先生成 plan 并输出摘要，获得 approval 后才创建/执行 run。
- tool identity 与 plan 不一致时返回 `pack-tool-identity-changed`，要求重新 doctor/plan。
- output 已存在时返回 `pack-output-conflict`，不提供隐式 force overwrite。
- exit 0 但没有完整 declared outputs 时仍返回 failed/partial，不进入 awaiting-acceptance。

## 错误码草案

- `pack-tool-not-found`
- `pack-tool-version-unsupported`
- `pack-tool-identity-changed`
- `pack-approval-required`
- `pack-execution-timeout`
- `pack-execution-canceled`
- `pack-execution-failed`
- `pack-partial-output`
- `pack-output-conflict`
- `pack-output-boundary-violation`
- `pack-output-limit-exceeded`

## 测试计划

- ArgumentList quoting：spaces、Unicode、special characters 不改变参数边界。
- Approval denied/default-deny 不启动进程。
- Fake driver exit codes、stderr secret、hang、child process、partial/unexpected/oversized output。
- Tool hash 在 approval 前后变化时 fail closed。
- Cancel/timeout 终止进程树并写 terminal/interrupted evidence。
- 外部工具尝试 workspace/source/outside-run 写入时被边界检测或拒绝。
- job/run/log/report redaction 不泄露 secret-like arguments/environment。
- 真实 fixture smoke 验证 tool/version/input/output，并在未配置时明确 skip。

## 任务清单

- [ ] Step 1: 将 Week 58 参数模板编码为 typed adapter，不暴露自由参数拼接。
- [ ] Step 2: 定义 tool execution risk summary 和 approval request。
- [ ] Step 3: 实现 structured process start、bounded env、cwd 和 output capture。
- [ ] Step 4: 实现 timeout/cancel/process-tree cleanup。
- [ ] Step 5: 实现 declared output inventory、boundary、size 和 partial-output checks。
- [ ] Step 6: 将 real adapter 接入 run state machine/job/artifacts/trace。
- [ ] Step 7: 增加稳定 error code 和 text/JSON execution result。
- [ ] Step 8: 增加 fake malicious/failure adapter tests。
- [ ] Step 9: 使用授权 fixture 执行真实转换并记录工具版本/hash。
- [ ] Step 10: 增加独立 real-tool opt-in smoke 和 smoke contract tests。
- [ ] Step 11: 更新 configuration/quickstart/security/troubleshooting 草稿。
- [ ] Step 12: 运行 build/test/default smoke/real-tool smoke。
- [ ] Step 13: 创建 `61_week_review.md`，记录真实输出和 Week 62 verifier 输入。

## 验收标准

- 至少一个授权 fixture 在目标 Windows 环境完成真实转换并形成 run/job/artifact evidence。
- 外部工具启动只使用 typed executable/arguments；审批摘要与实际执行一致。
- timeout、cancel、failure、partial output 都有稳定状态和无残留进程验证。
- 默认 build/test/smoke 不依赖真实工具；真实 smoke 只有显式 opt-in 才运行。
- 本周只接受“conversion executed”，不提前宣称 TIFF engineering verification passed。
