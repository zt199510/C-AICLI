## 第 33 周回顾

状态：已验证

已完成：
- 新增 `status` 命令，汇总 workspace、git 状态、有效配置与 approval mode；可在非 git workspace 下运行。
- 新增 `diff` / `diff --stat` 命令；`diff` / `review` 现在覆盖 staged tracked、unstaged tracked、untracked non-ignored user files、no-HEAD repo，以及 staged/unstaged tracked 互相抵消的场景。
- `diff` / `review` 在 diff 输出被截断时会显示明确 warning，避免静默使用不完整 diff。
- CLI 内部 `.caicli/logs` 已从 diff/review 噪音中排除；其他用户文件，包括其他 `.caicli` 文件，仍保持可见。
- `review` 保持 workspace 只读：不执行 patch/shell/tools，不写 workspace files、transcripts、logs、patches；diff 收集可能在 workspace 外创建并清理临时文件/目录；真实使用仍需有效模型凭据。
- diff 收集路径完成只读加固：使用 raw candidate 收集，避免 external diff、textconv、clean filter、index refresh 等副作用；unsupported metadata/non-regular tracked entries 与 symlink/reparse entries 会用 summary/warning 命名路径，而不是修改 repo 状态或静默漏报。
- `review` 输出支持默认 text、`--json` 与 `--output json`，并采用 findings-first 审查格式。
- 新增 `models` 命令，仅读取本地配置与静态示例，输出 `modelListApi: not called`；不调用外部模型列表 API，也不需要 API key。
- 补充 `status`、`diff`、`review`、`models` 相关测试，覆盖无 git/有 git workspace、空 diff、no-HEAD、staged/untracked/cancel-out、truncation、CLI log 排除、read-only diff hardening 与 review 模型失败等行为。
- 更新 release docs：quickstart、configuration、capability status、known limitations、CHANGELOG 等记录新命令能力与限制。
- 更新 release smoke 覆盖离线命令：`status`、`models`、`diff`、`diff --stat`；未运行 `review` smoke，因为它需要模型凭据。

验证：
- 命令：从 `C:\Users\10335\AppData\Local\Temp` 运行 `dotnet build D:\AI\C-AICLI\.worktrees\week-32-project-instructions-agents-md\src\CSharpAiCli.sln --no-restore -v minimal`
- 结果：通过。`CSharpAiCli.Core`、`CSharpAiCli.AgentFramework`、`CSharpAiCli.Cli`、`CSharpAiCli.ProjectPacks`、`CSharpAiCli.Tests` 均成功生成；0 个警告，0 个错误。需要从 repo/worktree 外运行，因为 `global.json` pins SDK `9.0.308`，本机可用 SDK 为 `10.0.301`。
- 命令：从 `C:\Users\10335\AppData\Local\Temp` 运行 `dotnet test D:\AI\C-AICLI\.worktrees\week-32-project-instructions-agents-md\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -v minimal`
- 结果：通过。失败 0，通过 678，跳过 0，总计 678。
- 命令：`git diff --check`
- 结果：通过，无 whitespace/error 输出。
- 命令：`rg -n "status|models|diff|review|modelListApi|review --json|diff --stat" docs_md\release tools\Invoke-SmokeTests.ps1 src\CSharpAiCli.Tests\SmokeTestScriptTests.cs`
- 结果：通过。命中 release docs、`tools\Invoke-SmokeTests.ps1` 与 smoke script tests 中的新命令、JSON review、`modelListApi: not called`、`diff --stat` 等预期引用。
- 命令：检查 `artifacts\release\caicli-0.1.0-win-x64\caicli.exe`
- 结果：文件不存在；因此未执行 packaged runtime smoke。

运行时说明：
- `review` 是 workspace 只读审查入口，不写 workspace files/logs/transcripts/patches，也不运行 shell/patch tools；diff 收集可能使用 workspace 外临时文件/目录并清理；但会把当前 git diff 发送给 configured model provider，真实使用需要有效模型配置、凭据与网络/配额。
- `diff` 使用 hardened read-only collection path；对 unsupported metadata/non-regular tracked entries 与 symlink/reparse entries 会输出 summary/warning 并命名路径，而不是为生成 diff 去变更 repo 状态或复制链接目标内容。
- `models` 是 local-only 配置查看入口，不需要 API key，也不调用 model-list API。
- smoke script 当前只覆盖无需模型凭据的离线路径：`status`、`models`、`diff`、`diff --stat`。
- `diff` 依赖 git workspace state；`status` 可在非 git workspace 中报告状态。
- Direct SDK agent tool continuation 仍 deferred。

风险：
- packaged release executable 缺失，release artifact 级 runtime smoke 仍待产物存在后执行。
- 本机缺少 `global.json` pins 的 SDK `9.0.308`，仅有 SDK `10.0.301`；repo-root dotnet commands 会被阻断，除非安装 pinned SDK，或从 repo/worktree 外使用 absolute paths 运行。
- `review` 真实效果依赖模型供应商响应质量与凭据配置，并会暴露当前 diff 给该供应商。
- Direct SDK agent tool continuation 仍 deferred。

第 34 周输入：
- 进入第 34 周工具系统增强与错误码统一计划。
- release artifact 可用后补跑 packaged `caicli.exe` runtime smoke。
- 在具备模型凭据的环境中补充 `review` 的手动或凭据化 smoke 验收。
- 补充 rendered docs/link review。
