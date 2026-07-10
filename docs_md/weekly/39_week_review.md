## 第 39 周回顾

状态：已稳固

已完成：
- 使用 Subagent-Driven execution：一个 explorer 梳理 fake/offline loop、direct OpenAI client、tool schema/result/event 分层；一个 worker 独立完成 opt-in real model smoke 脚本改造。
- Step 1：确认 `OfflineAgentRunner` 是唯一 agent loop state machine，direct OpenAI 通过 `OpenAiToolCallingModel` 复用同一 `IToolCallingModel` contract。
- Step 2：保留 provider-neutral `OpenAiAgentRequest` / `OpenAiResponseEnvelope` / `OpenAiToolResultInput` DTO，SDK 类型只留在 OpenAI gateway 内部。
- Step 3：`ToolDefinition` 继续通过 `ToolSchemaRenderer` 和 `OpenAiToolDefinitionMapper` 暴露为 Responses function tool schema。
- Step 4：实现 `SdkOpenAiResponsesGateway.CreateAgentResponse`，将 SDK `FunctionCallResponseItem` 解析为 `OpenAiToolCall`，再进入现有 tool registry dispatch。
- Step 5：`OpenAiToolResultInput` 增加 `Retryable` 和 `StructuredPayload`，并以稳定 JSON 写回 SDK `FunctionCallOutputResponseItem`，包含 `summary`、`errorCode`、`approvalStatus`、`retryable` 和 `structuredPayload`。
- Step 6：真实 continuation 复用 `OfflineAgentRunner` 的 `AgentRunEvent`，现有 `AgentExecResultAdapter`、text/NDJSON renderer、session transcript 和 trace path 无需分叉。
- Step 7：补充 SDK gateway、OpenAI tool-calling adapter、OpenAI agent runner 测试，覆盖 function tools、function-call output、structured payload、tool failure、HTTP/SDK safe failure 映射。
- Step 8：`tools/Invoke-SmokeTests.ps1` 增加 `CAICLI_REAL_MODEL_SMOKE=1` opt-in real model smoke；默认跳过，缺 `OPENAI_API_KEY` 或 `OPENAI_MODEL` 时输出 skip。
- Step 9：更新 capability status、known limitations、quickstart 和 changelog。
- Step 10：运行 build/test 并创建本回顾。

验证：
- 命令：`dotnet build D:\AI\C-AICLI\src\CSharpAiCli.sln`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test D:\AI\C-AICLI\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj --filter "FullyQualifiedName~SdkOpenAiResponsesGatewayAgentTests|FullyQualifiedName~OpenAiToolCallingModelTests|FullyQualifiedName~OpenAiAgentRunnerTests"`
- 结果：通过，15 passed，0 failed，0 skipped。
- 命令：`dotnet test D:\AI\C-AICLI\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj --filter "FullyQualifiedName~SmokeTestScriptTests"`
- 结果：通过，1 passed，0 failed，0 skipped。
- 命令：PowerShell parser check for `tools\Invoke-SmokeTests.ps1`
- 结果：通过。
- 命令：`dotnet test D:\AI\C-AICLI\src\CSharpAiCli.sln --no-build`
- 结果：通过，978 passed，0 failed，0 skipped。
- 命令：`git diff --check`
- 结果：通过，仅显示既有 LF/CRLF warning。

运行时说明：
- 仓库根 `global.json` 锁定 .NET SDK `9.0.308`，当前机器只有 `10.0.301`；验证命令从临时目录调用 solution 绝对路径，避免修改 SDK 锁定文件。
- 默认 smoke 不访问真实模型；真实模型 smoke 需要显式设置 `CAICLI_REAL_MODEL_SMOKE=1`，并复用调用者原始 `OPENAI_API_KEY` / `OPENAI_MODEL`。
- opt-in real smoke 使用 `--approval never` 和只读 prompt，并断言输出包含 `workspace.read_text`，避免只验证无工具的普通模型回答。
- `OpenAiAgentRunner` 现在将 SDK HTTP/transport failures 映射为脱敏 `AgentRunResult.Failure`，避免 raw SDK exception 泄出 CLI。

风险：
- 真实模型是否实际选择工具仍受模型行为影响；prompt 和 smoke 断言已收紧，但 opt-in smoke 仍可能因模型/provider 差异失败，需要作为人工凭据环境验证。
- SDK Responses API 仍带 experimental warning 抑制；后续 SDK 升级可能改变 response item 类型或 factory 签名。
- `agent-backend-unavailable` 仅保留为旧 NotSupported gateway 兼容路径，默认 direct SDK path 已不再依赖它。
- CLI 仍不是 sandbox；写入和 shell 工具继续依赖 approval policy、workspace guard 和 tool-level safety checks。

第 40 周输入：
- 在真实凭据环境运行 `CAICLI_REAL_MODEL_SMOKE=1` release smoke，并记录 provider/model。
- 继续推进 agent state machine limits、retry/failure feedback 和 trace schema 收敛。
- 为真实 provider 差异补充更窄的 tool-selection prompt 或模型兼容说明。
