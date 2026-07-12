## 第 46 周回顾

状态：已验收

已完成：
- 使用 Subagent-Driven 方式执行：explorer 汇总 Week 39-45 review 状态与 Week46 欠账，explorer 审查 release docs/Deferred 边界，worker 起草 `CHANGELOG.md` 和 `final_acceptance_0.3.0.md`，主线程完成版本、文档、验证、release package 和最终整合。
- 将版本元数据更新到 `0.3.0`：`Directory.Build.props` 的 `Version`、`AssemblyVersion`、`FileVersion` 均已更新，release build 测试断言同步。
- 更新 0.3.0 release docs：`CHANGELOG.md`、`configuration.md`、`installation.md`、`final_acceptance.md`、`final_acceptance_0.3.0.md`、`capability_status.md`、`security_model.md`、`troubleshooting.md`。
- 修正 smoke disabled-tool 断言：当前稳定错误码是 `tool-disabled`，同步 `tools/Invoke-SmokeTests.ps1`、脚本测试和 release docs。
- 生成 0.3.0 release package：`artifacts/release/caicli-0.3.0-win-x64` 和 `artifacts/release/caicli-0.3.0-win-x64.zip`。
- 记录 deterministic zip size/SHA256，并将 0.3.0 标记为当前 accepted release。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln -c Release`
- 结果：通过；0 warnings，0 errors；使用 SDK `9.0.308`。
- 命令：`dotnet test src/CSharpAiCli.sln -c Release --no-build`
- 结果：通过；1020 passed，0 failed，0 skipped。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1` 连续运行两次。
- 结果：通过；两次 zip size 均为 `32312392` bytes，SHA256 均为 `030BF5AFCAB3365A15D296A73DA8967ED87D8ECF816F3B61ED990F97CB3FDFDA`。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过；默认真实模型 smoke skip，不访问网络。
- 命令：`CAICLI_REAL_MODEL_SMOKE=1` 且移除 `OPENAI_API_KEY`/`OPENAI_MODEL` 后运行 `tools\Invoke-SmokeTests.ps1`。
- 结果：通过；真实模型路径输出缺凭据 skip，不影响 smoke。
- 命令：`artifacts\release\caicli-0.3.0-win-x64\caicli.exe version`
- 结果：输出 `caicli 0.3.0`、`target framework: net9.0`、`release runtime: win-x64`。

运行时说明：
- 当前 PATH 的系统 `dotnet` 仍优先解析到 .NET 10 SDK，会被仓库 `global.json` 的 `9.0.308` 阻断；验证时临时将 `%USERPROFILE%\.dotnet` 放到 PATH 前面，使用已安装的用户级 SDK `9.0.308`。
- 默认 smoke 是 credential-free/local-only；真实模型 smoke 必须显式设置 `CAICLI_REAL_MODEL_SMOKE=1` 且调用方提供 `OPENAI_API_KEY` 和 `OPENAI_MODEL`。
- 0.3.0 当前能力包含 direct OpenAI SDK agent tool-call continuation、fake/offline agent contract、`exec`、`review.gate`、`taskReport` 和 user-configured stdio MCP v1。
- Deferred 边界保留：Microsoft Agent Framework real backend、remote/http MCP、Gerber/TIFF real execution、dotnet tool packaging、独立 markdown task report 文件、交互式 approval UI。

风险：
- 本次没有使用真实凭据运行 real model smoke；真实模型质量、延迟、配额和 provider 可用性仍是外部依赖。
- Windows `win-x64` self-contained package 是当前 release artifact；非 Windows 包和 dotnet tool packaging 未验收。

后续输入：
- 0.3.1 可考虑 `@file`/`@folder` 引用、独立 markdown task report、richer task history 或 `caicli changes`。
- 如有真实模型凭据，单独执行 opt-in real model smoke 并记录 provider/model/trace 证据。
