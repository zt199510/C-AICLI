## 第 47 周回顾

状态：已稳固，待 0.3.1 release acceptance

已完成：

- `exec` 支持 inline `@file:<path>` / `@folder:<path>` bounded workflow references。
- Reference resolver 走 workspace guard；workspace 外路径、missing、binary explicit file 会在模型前失败；大文本文件截断并记录 warning。
- `AgentTaskContext`、startup prompt、startup plan、`context.references` event、text result、JSON `taskReport`、trace、session task report 均记录 reference metadata；task report 不持久化 raw referenced content。
- 新增只读 `caicli changes`，支持 text、`--json`、`--output json`、`--session <name>` 和显式 trace。
- `changes` 默认不写 command log，不调用模型，不运行 shell/patch/MCP，不写 workspace/session/report。
- 更新 smoke script、release docs、runtime logging diagnostics、capability status、known limitations、quickstart、security model、troubleshooting。

验证：

- 命令：`$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1029 passed，0 failed，0 skipped。
- 命令：`$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过；默认 real model smoke 按设计跳过。

运行时说明：

- `@file:` / `@folder:` 是 0.3.1 唯一 reference 语法；未新增 `--ref`。
- `@file` 大文件采用截断 warning；explicit binary/unreadable file 仍 hard failure。
- `changes` 默认不写 command log；显式 `--trace` 或 `CAICLI_TRACE=1` 才写 redacted trace。
- 为验证 smoke，本地重建了当前版本名下 release artifact；这不是 0.3.1 final acceptance，也不更新 `final_acceptance_0.3.0.md`。

风险：

- 当前版本 metadata 仍是 `0.3.0`；0.3.1 发布验收时需要统一 bump version、生成 deterministic package，并记录 zip size/SHA256。
- `@folder` 是 bounded recursive include，不是 semantic retrieval；大型仓库仍需要用户给出更小路径。
- `changes` 是复核视图，不是 correctness proof；仍需要用户检查 diff/test。

后续输入：

- 进入 0.3.1 release acceptance：版本元数据、deterministic package、final acceptance doc。
- 0.3.2 才处理 markdown report / expert profiles。
- 0.3.3 才处理 skills / workflow packs。

