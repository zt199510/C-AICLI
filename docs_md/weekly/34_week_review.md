## 第 34 周回顾

状态：已验收

已完成：
- Step 1：新增 `ToolErrorCode` 常量/registry，统一工具错误码命名；final review 后补齐 workspace guard 传播的 workspace error code 常量。
- Step 2：`ToolExecutionResult` 支持 optional structured payload，并在工厂方法中 clone/preserve payload。
- Step 3：read/search/git/shell/patch 工具返回统一 `errorCode` 与结构化 payload，runner/applier 错误码已向上传递。
- Step 4：实现 `tools list --json`，输出稳定对象：`type`、排序后的 `tools[]`（`name`、`description`、`riskLevel`、`parameters`）与排序后的 `disabledTools[]`；保留原文本输出。
- Step 5：实现 `tools call <name> --stdin` 从 stdin 读取 JSON arguments，并保持 inline JSON / `--arguments-file` 既有行为与冲突校验。
- Step 6：新增 `ToolSchemaRenderer` / `RenderedToolDefinition`，由 OpenAI mapper、AgentFramework bridge 与 `tools list --json` 共享；已评审 immutable schema string / fresh object 行为。
- Step 7：补充 JSON schema、stdin、错误码稳定性、disabled tool 覆盖；最新测试包含 `CliCommandFactoryTests.Tools_list_json_omits_disabled_mcp_tools_and_reports_disabled_names`。
- Step 8：更新 `docs_md/release/capability_status.md` 与 `docs_md/release/quickstart.md`，记录当前能力状态与快速上手说明。

验证：
Canonical verification：
- 命令：`dotnet --list-sdks`
- 结果：初始环境仅有 `10.0.301 [C:\Program Files\dotnet\sdk]`，不满足 `global.json` 锁定的 SDK `9.0.308`。
- 命令：`Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1` 后执行 `dotnet-install.ps1 -Version 9.0.308 -InstallDir $env:USERPROFILE\.dotnet -NoPath`
- 结果：通过；用户级安装完成，`C:\Users\10335\.dotnet\dotnet.exe --list-sdks` 显示 `9.0.308 [C:\Users\10335\.dotnet\sdk]`。
- 命令：`C:\Users\10335\.dotnet\dotnet.exe build src\CSharpAiCli.sln`
- 结果：通过；`已成功生成`，0 warnings，0 errors。
- 命令：`C:\Users\10335\.dotnet\dotnet.exe test src\CSharpAiCli.sln`
- 结果：通过；0 failed，719 passed，0 skipped，总计 719。

Formatting check：
- 命令：`git diff --check`
- 结果：退出码 0；未发现 whitespace error。Git 同时提示多个现有 dirty 文件在下次 Git 触碰时会发生 `LF will be replaced by CRLF`，包括 release docs、`docs_md/weekly/38_week_cli_enhancement_schedule.md`、Step 1-7 涉及的 source/test 文件；这些 broader dirty-tree CRLF warnings 不影响本次验收。
- 验收状态：canonical build/test 已在 SDK `9.0.308` 下完成，因此本周状态更新为已验收。

运行时说明：
- direct OpenAI SDK tool-call continuation 仍延后；当前重点是稳定 tool schema、错误码与 payload contract。
- structured payload 已支持 offline/fake/in-process agent loop 直接消费工具结果。
- CLI 文本输出兼容性已保留；新增 JSON/stdin 行为作为结构化入口提供。

风险：
- 后续验收环境仍需要 .NET SDK `9.0.308`，或安装符合 `global.json` policy 的兼容 SDK。
- 工作区存在较多既有 dirty source/test/release-doc 变更；本回顾只记录状态，不修改这些文件。
- direct OpenAI SDK tool-call continuation 仍为 Deferred，不能把本周 structured payload 支持误读为真实 OpenAI 工具循环已完成。

第 35 周输入：
- MCP real stdio v1 可以基于本周完成的 schema/error/payload contract 继续扩展。
- agent loop 后续可把 structured tool result 接入真实运行路径，并补齐 direct OpenAI SDK continuation。
- 根据稳定的 `tools list --json` 与 `tools call --stdin` 体验继续收敛 CLI/MCP 文档与验收脚本。
