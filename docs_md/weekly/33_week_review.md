## 第 33 周回顾

状态：已验收

已完成：
- 新增 `status` 命令，汇总 workspace、git 状态、有效配置与 approval mode；可在非 git workspace 下运行。
- 新增 `diff` / `diff --stat` 命令，基于当前 git workspace state 输出 diff 或 stat 摘要。
- 新增只读 `review` 命令，默认不执行 patch/shell/tools，不写 transcripts/logs/files/patches；会把当前 git diff 发送给配置的模型，真实使用需要模型凭据。
- `review` 输出支持默认 text、`--json` 与 `--output json`，并采用 findings-first 审查格式。
- 新增 `models` 命令，仅读取本地配置与静态示例，输出 `modelListApi: not called`；不调用外部模型列表 API，也不需要 API key。
- 补充 `status`、`diff`、`review`、`models` 相关测试，覆盖无 git/有 git workspace、空 diff、review 模型失败与 clean diff review behavior。
- 更新 release docs：quickstart、configuration、capability status、known limitations、CHANGELOG 等记录新命令能力与限制。
- 更新 release smoke 覆盖离线命令：`status`、`models`、`diff`、`diff --stat`；未运行 `review` smoke，因为它需要模型凭据。

验证：
- 命令：从 `C:\Users\10335\AppData\Local\Temp` 运行 `dotnet build D:\AI\C-AICLI\.worktrees\week-32-project-instructions-agents-md\src\CSharpAiCli.sln --no-restore -v minimal`
- 结果：通过。`CSharpAiCli.Core`、`CSharpAiCli.AgentFramework`、`CSharpAiCli.Cli`、`CSharpAiCli.ProjectPacks`、`CSharpAiCli.Tests` 均成功生成；0 个警告，0 个错误。需要从 repo/worktree 外运行，因为 `global.json` pins SDK `9.0.308`，本机可用 SDK 为 `10.0.301`。
- 命令：从 `C:\Users\10335\AppData\Local\Temp` 运行 `dotnet test D:\AI\C-AICLI\.worktrees\week-32-project-instructions-agents-md\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -v minimal`
- 结果：通过。失败 0，通过 632，跳过 0，总计 632。
- 命令：`git diff --check`
- 结果：通过，无 whitespace/error 输出。
- 命令：`rg -n "status|models|diff|review|modelListApi|review --json|diff --stat" docs_md\release tools\Invoke-SmokeTests.ps1 src\CSharpAiCli.Tests\SmokeTestScriptTests.cs`
- 结果：通过。命中 release docs、`tools\Invoke-SmokeTests.ps1` 与 smoke script tests 中的新命令、JSON review、`modelListApi: not called`、`diff --stat` 等预期引用。
- 命令：检查 `artifacts\release\caicli-0.1.0-win-x64\caicli.exe`
- 结果：文件不存在；因此未执行 runtime release smoke。

运行时说明：
- `review` 是只读审查入口，但会把当前 git diff 发给 configured model provider；真实使用需要有效模型配置、凭据与网络/配额。
- `models` 是 local-only 配置查看入口，不触发外部 model-list API。
- smoke script 当前只覆盖无需模型凭据的离线路径：`status`、`models`、`diff`、`diff --stat`。
- `diff` 依赖 git workspace state；`status` 可在非 git workspace 下报告状态。

风险：
- packaged release executable 缺失，release artifact 级 runtime smoke 仍待产物存在后执行。
- `review` 真实效果依赖模型供应商响应质量与凭据配置，并会暴露当前 diff 给该供应商。
- Direct SDK agent tool continuation 仍 deferred。

第 34 周输入：
- 进入第 34 周工具系统增强与错误码统一计划。
- release artifact 可用后补跑 packaged `caicli.exe` runtime smoke。
- 在具备模型凭据的环境中补充 `review` 的手动或凭据化 smoke 验收。
