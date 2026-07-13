# 第 49 周 Review: 0.3.3 Local Skills 与 .NET Workflow Packs

## 实际交付

- 新增 `caicli skills list`，支持 text 与 `--output json` / `--json`，列出内置 packs 与 workspace `.caicli/skills` JSON packs。
- 新增 `caicli skills run <name> --dry-run -- <task>`，输出 expanded plan，不调用模型、不运行工具、不写 report、不保存 session。
- 新增非 dry-run `skills run`，复用现有 agentic `exec` 安全路径：approval、workspace guard、disabled tools、shell policy、tool boundary、trace、session、review gate、task report 与 markdown report。
- 新增内置 .NET packs：`test-fix`、`review-only`、`upgrade-package`、`doc-sync`。
- 新增 JSON manifest model、workspace-local loading、validation、error codes、duplicate/invalid diagnostics。
- 新增 skill metadata 到 `AgentRunRequest`、`AgentTaskReport`、text/JSON output、trace payload、session markdown export 与 markdown task report。
- 默认 smoke 增加 credential-free `skills list` text/json 与 `skills run review-only --dry-run` text/json。
- 版本元数据更新为 `0.3.3`，release docs 更新当前能力与边界。

## 验证记录

- Passed: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
- Passed: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test src\CSharpAiCli.sln -c Release --no-build --filter "FullyQualifiedName~SkillPackTests|FullyQualifiedName~CliCommandFactoryTests|FullyQualifiedName~SmokeTestScriptTests|FullyQualifiedName~ProductInfoTests"`
- Passed: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test src\CSharpAiCli.sln -c Release --no-build`
- Passed: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- Passed: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- Release artifact: `artifacts/release/caicli-0.3.3-win-x64.zip`, size `32371265` bytes, SHA256 `3EA45A7366BD3E7EDC940E1737D786EA0D4DFB6C8ECB963D6FED668F6E903BF1`.

## 验收要点

- `skills list` 是只读 catalog 入口；invalid/duplicate local pack 只产生 diagnostics，不隐藏 built-ins。
- `skills run --dry-run` 支持 task token 合并，`-- Fix failing tests` 不需要额外 shell quoting。
- `review-only` 通过 reviewer expert 与 skill boundary 保持 read-only；patch/shell/MCP 请求被 `tool-disabled` 阻断。
- `validationCommand` 只作为 plan/report hint，不绕过 shell policy 或 approval。
- Suggested references 不自动注入为 `@file:` / `@folder:` token；用户在 task 中显式提供的 references 继续走 0.3.1 bounded resolver。

## Deferred

- 不支持 remote marketplace、自动更新、签名 trust chain、YAML manifests、用户级 skill 目录、团队知识库或远程执行。
- 不支持 custom expert files 或 automatic model role routing。
- 不做 Gerber/TIFF real toolchain execution。
- `skills run` 当前复用 agentic `exec` 路径；没有单独的 review command backend。
