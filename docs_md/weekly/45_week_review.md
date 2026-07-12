## 第 45 周回顾

状态：已验收

已完成：
- 用 Subagent-Driven 方式执行：explorer 分别审查 smoke 脚本、agent runner/CLI 入口、release docs；worker 更新 release docs；主线程完成 smoke、测试、验证和回顾整合。
- 扩展 `tools/Invoke-SmokeTests.ps1`，增加 `src/BuggyApp/Calculator.txt` 小型 bugfix fixture 和 `tests/Verify-BuggyApp.ps1` 验证脚本。
- 默认 packaged smoke 新增本地 read/search/patch/verify/diff 覆盖：`workspace.read_text`、`workspace.search_text`、`workspace.apply_patch --approval always`、`workspace.run_shell --approval always`、`diff --stat`。
- smoke 临时 git repo 显式设置 `core.autocrlf=false`，避免用户全局 Git 换行配置把 warning 写入 stderr 并中断 PowerShell smoke。
- 新增 CLI fake model E2E 测试，使用真实 registry/executor 覆盖 `exec` 的 read/search/patch/verification、`patch.preview`、`patch.apply`、`changed.files`、`verification.result`、`review.gate`、`taskReport` 和 trace payload。
- 保留 real model smoke opt-in：默认不联网；`CAICLI_REAL_MODEL_SMOKE=1` 且缺 `OPENAI_API_KEY`/`OPENAI_MODEL` 时 skip，不 fail。
- 更新 quickstart、security model、known limitations、capability status，并新增 `docs_md/release/troubleshooting.md`。
- 文档明确 direct OpenAI SDK agent tool loop、fake/offline contract、`exec`、`review.gate`、`taskReport`、user-configured stdio MCP v1 是当前能力，不再标为 Deferred。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：未运行成功；本机仅安装 .NET SDK `10.0.301`，仓库 `global.json` 锁定 `9.0.308`。
- 命令：`dotnet 'C:\Program Files\dotnet\sdk\10.0.301\MSBuild.dll' src\CSharpAiCli.sln -restore -p:Configuration=Release -v:minimal`
- 结果：通过。
- 命令：`dotnet 'C:\Program Files\dotnet\sdk\10.0.301\vstest.console.dll' 'src\CSharpAiCli.Tests\bin\Release\net9.0\CSharpAiCli.Tests.dll'`
- 结果：通过，1020/1020。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过；输出 real model smoke 默认 skip，并通过默认 smoke。
- 命令：`$env:CAICLI_REAL_MODEL_SMOKE='1'; Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue; Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过；输出 `CAICLI_REAL_MODEL_SMOKE=1 requires caller OPENAI_API_KEY and OPENAI_MODEL`，真实模型路径 skip，不影响 smoke。

运行时说明：
- 默认 smoke 是 credential-free/local-only，不访问真实模型。
- fake/offline agent 语义通过单元测试覆盖完整 `exec` 报告链；packaged smoke 通过直接工具路径覆盖本地 bugfix fixture。
- real model smoke 仍限定为 opt-in read-only `exec --approval never` 路径。
- `review.gate` 是本地只读 diff summary，不是模型复核。
- 完整独立 markdown task report 仍 Deferred；text/JSON/trace/session 中的 `taskReport` 是当前能力。
- Week 45 未更新版本元数据或 release zip；0.3.0 版本 bump 和最终发布包属于 Week 46 范围。

风险：
- 本机缺 SDK `9.0.308`，标准 `dotnet build/test` 会被 `global.json` 阻断；已用 .NET 10 SDK 直接 MSBuild/VSTest 入口验证。
- smoke 当前仍依赖已有 `artifacts/release/caicli-0.2.1-win-x64/caicli.exe`；Week 46 需要生成 0.3.0 release artifact 后重新跑 smoke。
- 真实模型 write/patch 行为不进入默认 smoke；写路径由 fake/offline E2E 和本地 tool smoke 覆盖，真实模型质量仍依赖 provider/model。

第 46 周输入：
- 更新 version metadata 到 `0.3.0` 并生成 deterministic release package。
- 将 Week 39-45 能力纳入 final acceptance 和 CHANGELOG。
- 用 0.3.0 release artifact 重跑 build/test/smoke，并记录 zip size/SHA256。
