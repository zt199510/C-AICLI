## 第 38 周回顾

状态：已验收

已完成：
- 使用 Subagent-Driven execution：一个 explorer 汇总 Week 27-37 状态与 caveats，另一个 explorer 审查 CLI command surface 与 smoke coverage gaps。
- Step 1：汇总 Week 27-37 review 状态。Week 27-28、30-37 为 Accepted；Week 29 agent run loop 为 Solidified，真实 direct OpenAI SDK tool-call continuation 继续 Deferred。
- Step 2：扩展 `tools/Invoke-SmokeTests.ps1`，覆盖 config、exec、approval、session、instructions、MCP stdio、workflow、tools JSON/stdin、logs/trace、status/models/diff。
- Step 3：将版本元数据更新到 `0.2.0`。
- Step 4：更新 `docs_md/release/CHANGELOG.md`，新增 `0.2.0 - 2026-07-10` 发布记录。
- Step 5：更新 configuration、quickstart、security model、known limitations、capability status、installation、final acceptance 文档。
- Step 6：完成 Release build。
- Step 7：完成 Release test。
- Step 8：两次运行 release build 脚本，确认 zip size/SHA256 deterministic。
- Step 9：完成 packaged smoke tests。
- Step 10：创建 `docs_md/weekly/38_week_review.md` 和 `docs_md/release/final_acceptance_0.2.0.md`。
- 验收中修复了 MCP stdio 测试 fixture 的 Windows timing/cleanup 抖动：放宽真实 PowerShell stdio fake server 成功路径超时，并为测试临时目录清理增加短重试。
- 调整 smoke stdin 覆盖：Windows PowerShell pipeline 对 native stdin 的编码会导致 JSON 解析失败，smoke 改用临时 ASCII 文件加 `cmd /c type` 管道验证 `tools call --stdin`。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --filter "McpStdioTransportTests|McpDoctorReportTests|McpStdioToolClientTests"`
- 结果：通过，48 passed，0 failed，0 skipped。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，970 passed，0 failed，0 skipped。
- 命令：`tools\Build-Release.ps1` 两次。
- 结果：通过，两次 zip SHA256 一致。
- Release zip：`artifacts/release/caicli-0.2.0-win-x64.zip`
- Release zip size：`32247811` bytes。
- Release zip SHA256：`A65CB4AC5276C54E0C6DCB6104D438B6D8F18D705E760AAB84F1B7B00EB21565`。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`。

运行时说明：
- `0.2.0` release package 仍是 Windows `win-x64` self-contained single-file package。
- Stdio MCP v1 已可用于 user-configured stdio servers 的 registry/tool paths；workspace-configured MCP servers 仍不会在普通 registry creation 中自动启动。
- `exec` agentic v1 surface、offline/fake loop contracts、events、limits、approval、session/resume/context 已固化；真实 direct OpenAI SDK tool-call continuation 仍 Deferred。
- `review` 是 workspace-read-only，但真实使用会把当前 diff 发送给 configured model provider，仍需要模型凭据。
- `--trace`/`logs` 是诊断能力，trace payload shape 不是稳定 public API。

风险：
- CLI 是本地安全边界，不是完整 sandbox。
- 没有 interactive approval UI；`on-request` / `on-failure` 在非交互 CLI 中返回 approval-required。
- Remote/http MCP、real Microsoft Agent Framework backend、real Gerber/TIFF execution、dotnet tool packaging 仍 Deferred。
- Windows PowerShell 到 native stdin 的直接 pipeline 不适合作为 release smoke 的稳定输入方式；当前 smoke 使用 `cmd /c type` 保持 Windows package 验证稳定。

后续输入：
- 继续推进 direct OpenAI SDK tool-call continuation。
- 设计 remote/http MCP transport 前，保持 stdio MCP 安全模型稳定。
- 如要把 trace JSONL 作为公共接口，需要单独设计 schema/versioning。
- 后续 0.2.x 可考虑交互式 approval UI、配置向导、会话 pruning/context-size controls、review credentialed smoke。
