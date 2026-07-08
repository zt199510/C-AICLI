## 第 32 周回顾

状态：已验收

已完成：
- Step 1：定义项目指令发现规则，`AGENTS.md` 在同目录优先，保留 `AICLI.md` 作为 legacy fallback。
- Step 2：实现从 workspace root 到目标路径的层级加载，并限制目标必须在 workspace 内。
- Step 3：合并指令时记录 source path 与 root-to-leaf 顺序。
- Step 4：更新 `CliEnvironmentSnapshot`，携带 ordered instruction sources。
- Step 5：更新 `doctor`、`config get`、`config list` 的 snapshot/report 展示。
- Step 6：`chat --cwd` 与 agentic `exec --cwd` 使用目标路径选择 instruction target，并将合并后的指令传给模型。
- Step 7：补充 root/subdir、多文件合并、越界路径、空文件、同目录 `AGENTS.md` 优先等 edge-case tests。
- Step 8：更新 release quickstart、security model、capability status、changelog 等文档。

验证：
- 命令：`D:\AI\.dotnet\dotnet.exe test src\CSharpAiCli.sln --no-restore`
- 结果：通过。`CSharpAiCli.Tests.dll (net9.0)`：失败 0，通过 608，跳过 0，总计 608。
- 命令：`rg -n "AGENTS|AICLI|--cwd|instruction" docs_md\release`
- 结果：通过。匹配覆盖 `docs_md\release\quickstart.md`、`docs_md\release\security_model.md`、`docs_md\release\capability_status.md`、`docs_md\release\CHANGELOG.md` 等 release docs。

运行时说明：
- `--workspace` 仍是 workspace root 与 tool boundary。
- `--cwd` 仅为 `chat` 与 agentic `exec` 选择 instruction target。
- workspace 外目标不会加载 workspace 外的 instruction paths/content。
- report surfaces 只展示 source paths/order 与 warnings，不展示 instruction contents。
- direct SDK exec tool-call continuation 仍 deferred。

风险：
- instruction contents 会发送给 configured model provider。
- direct SDK tool-call continuation deferred。
- 未执行 rendered docs/link review。

第 33 周输入：
- 以本周工作作为 release acceptance 基础。
- 推进 agentic exec SDK tool-call continuation。
- 补充 smoke docs 或 rendered docs/link review。
