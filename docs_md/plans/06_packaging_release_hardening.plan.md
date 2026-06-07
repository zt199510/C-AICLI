# 阶段 06 - 打包、发布与加固计划

## 状态

`Accepted`

## 目标

通过打包、文档、smoke tests、失败恢复和安全评审，为 C# AI CLI 准备第一个严肃的本地发布版本。

## 目标周数

第 23-26 周

## 范围

创建：

- 发布构建脚本
- Windows 自包含可执行文件包
- 可选的 dotnet tool 包
- 安装文档
- 用户快速开始
- 安全和权限模型文档
- smoke test 脚本
- 已知限制文档
- 从阶段 03 累积的安全测试和失败案例清单
- direct 后端发布候选验收记录

## 必需行为

- 发布产物可以在干净 Windows 机器上运行，并有明确的前置条件文档。
- `caicli doctor` 能诊断缺失配置和凭据。
- `caicli run` 可以在测试工作区完成一个小型编码任务。
- 日志可检查。
- 会话可以导出或清理。
- 用户可以禁用工具或命令执行。
- 如果 Microsoft Agent Framework 或 MCP 未通过验收，发布文档必须明确标注为实验、禁用或不包含；不得影响 direct 后端发布。
- Smoke tests 必须覆盖缺少 API key、工具禁用、审批拒绝、路径越界拒绝和命令执行超时。

## 验收标准

1. 发布构建成功。
2. Smoke tests 通过。
3. 文档覆盖安装、配置、chat、run、tools、MCP 和安全。
4. 安全评审记录当前限制。
5. 直接后端和 Microsoft Agent Framework 后端都经过测试，或被清晰标注。
6. 已创建版本号和 changelog。
7. Windows 发布包包含可运行的 direct 后端。
8. 发布前测试覆盖干净测试目录中的 `doctor`、`chat` 缺 key 错误、`run` 小任务、禁用工具、导出/清理会话。

## 验证命令

```powershell
dotnet build src/CSharpAiCli.sln -c Release
dotnet test src/CSharpAiCli.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1
```

## 风险与保护边界

- 不声称与 Codex 完全对等。
- 记录该工具能安全做什么、不能安全做什么。
- 清晰说明本地用户数据路径。
- 不在发布产物中包含密钥。
- 不把阶段 04 或阶段 05 的增强目标失败包装成核心产品失败；文档必须诚实描述可用能力。

## 阶段交付物

- 发布包
- 安装文档
- 快速开始文档
- smoke tests
- changelog
- 安全说明

## 最终六个月验收

当满足以下条件时，MVP 发布被接受：

- 阶段 01、02、03 和 06 达到 `Accepted`
- 发布产物存在
- 开发者可以用 CLI 操作本地工作区
- 文件编辑和命令执行仍受审批门禁控制
- direct 后端可以完成 chat、run、工具调用、会话转录和安全门禁
- 阶段 04 和阶段 05 达到 `Accepted`，或被清晰标注为 `Deferred` 且不影响 MVP

当满足以下条件时，完整增强计划被接受：

- 所有六个阶段计划都达到 `Accepted`
- 后端适配器可以被替换，而不需要重写 CLI
- MCP 和项目工作流可用，或有单独发布计划承接

## 阶段 06 验收记录

- `dotnet build src\CSharpAiCli.sln -c Release`：通过，0 warning，0 error。
- `dotnet test src\CSharpAiCli.sln -c Release --no-build`：通过，246 tests passed。
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1`：通过，生成 `artifacts/release/caicli-0.1.0-win-x64` 和 zip。
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1`：通过，覆盖缺 model/key、审批拒绝、路径越界、工具禁用、shell timeout、`run` 和 session export/clear。
- 发布文档位于 `docs_md/release/`，包含 installation、configuration、security model、quickstart、capability status、changelog、known limitations 和 final acceptance。
