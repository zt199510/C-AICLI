# 第 25 周回顾

状态：已稳固

已完成：
- 添加 `tools/Invoke-SmokeTests.ps1`，使用干净临时 workspace/user profile 验证发布包。
- 添加 `disabledTools` 配置字段，并在 CLI 工具注册时跳过禁用工具。
- 新增 `caicli tools list` 和 `caicli tools call`，支持 `--arguments-file` 以稳定传递 JSON 参数。
- 新增 deterministic `caicli run` 发布 smoke 任务：`create smoke note`、`read <path>`、`shell <command>`。
- 新增 `caicli session export` 和 `caicli session clear`。
- 更新 release 文档，补充工具禁用、`run`、`tools` 和 session 管理路径。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln --no-build`
- 结果：通过，246 tests passed。
- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，246 tests passed。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -NoZip`
- 结果：通过，生成 Windows self-contained 发布产物。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`。

Smoke 覆盖：
- `version` 可运行。
- `doctor` 在无 key 干净环境中成功。
- `chat` 无 model 返回 `missing-model`。
- `chat` 有 model 无 key 返回 `missing-openai-api-key`。
- patch 未审批返回 `approval-denied`。
- 工作区外读取返回 `workspace-boundary-denied`。
- `disabledTools` 禁用 shell 后返回 `unknown-tool`。
- 已审批 shell 超时返回 `shell-timeout`。
- `run --approve "create smoke note"` 修改 workspace 内文件。
- `session export` 和 `session clear` 可用。

运行时说明：
- `run` 是 deterministic direct-tool release smoke/task entry，不声称已接入真实模型 agent orchestration。
- `tools call --arguments-file` 是 Windows PowerShell 下推荐的 JSON 参数传递方式。

风险：
- `run` 的自然语言能力仍是 MVP release-smoke 级别；真实 OpenAI tool calling 尚未作为首版发布硬门槛接入。

第 26 周输入：
- 创建 changelog、known limitations 和最终验收清单。
- 运行 Release build、tests、smoke tests，并生成首个 zip 发布包。
