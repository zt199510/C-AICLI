## 第 41 周回顾

状态：已验收

已完成：
- 定义 `AgentTaskContext`/`AgentGitContextSummary`，在 `exec` request 中统一携带 cwd、workspace、instructions、session/resume 和 git summary。
- 在 agent run 首轮模型调用前记录 `context.workspace`、`context.instructions`、`context.session`、`context.git.status`、`context.git.diff` 和 `plan` 事件。
- 增加 bounded git status/diff stat 收集、startup plan truncation warning、`agent.plan` 工具和 planning 阶段只读工具约束。
- 将首个 plan summary 写入 session agent run summary，并在 transcript context/markdown export 中展示。
- 更新 release quickstart 和 capability status。

验证：
- 命令：`dotnet test D:\AI\C-AICLI\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj --no-restore`（从 `C:\Windows\Temp` 运行，避开仓库 `global.json` 对未安装 SDK 9.0.308 的解析限制）
- 结果：通过，993 passed，0 failed。

运行时说明：
- 仓库根目录直接运行 `dotnet test` 会被 `global.json` 要求的 SDK 9.0.308 阻断；本机安装的是 SDK 10.0.301，因此验证从仓库外 cwd 调用项目路径完成。
- `@file`/`@folder` 完整引用仍按计划 Deferred 到 0.3.1-0.3.2。

风险：
- 未执行真实模型 smoke；该路径仍依赖调用方提供 `OPENAI_API_KEY`/`OPENAI_MODEL` 并显式开启 real-model smoke。

第 42 周输入：
- 在 patch/verify workflow 中复用第 41 周的 task context、plan summary 和 changed files/diff summary。
