# 阶段 01 - CLI 基础与工作区计划

## 状态

`Accepted`

## 目标

创建 C# AI CLI 的项目骨架、终端命令结构、工作区检测、配置加载、日志记录和测试基础。

## 目标周数

第 1-4 周

## 基线

目标目录已存在：

```text
C:\Users\Administrator\Desktop\C#AICLI
```

不假定已经存在任何产品源代码。

## 范围

创建：

- solution 和项目布局
- `global.json` 或等价 SDK 锁定策略
- CLI 根命令
- `doctor`、`config`、`chat` 占位命令
- 工作区检测器
- 配置加载器
- 日志和诊断
- xUnit 测试

建议布局：

```text
src/
  CSharpAiCli.sln
  CSharpAiCli.Cli/
  CSharpAiCli.Core/
  CSharpAiCli.Tests/
docs_md/
  plans/
  weekly/
  spec/
```

## 架构

CLI 应用应保持轻量。业务逻辑放在 `CSharpAiCli.Core` 中。测试项目优先测试 core 行为，只在命令行为本身重要时测试 CLI 连接层。

运行时策略必须在本阶段落地。如果从当前时间重新启动，优先选择 .NET 10 LTS；如果坚持 .NET 8 LTS，必须创建 `global.json`、安装说明和 `doctor` 检查，避免本机只有 .NET 9 SDK 时出现不可重复构建。

## 必需行为

- `caicli --help` 打印命令帮助。
- `caicli doctor` 检查 .NET runtime、工作目录、配置路径和模型 key 是否存在，但不打印密钥。
- `caicli config get` 打印生效配置。
- `caicli chat` 在接入模型之前启动一个占位交互循环。
- 工作区检测默认选择当前目录。
- 工作区检测可以通过 `--workspace <path>` 覆盖。
- 配置从用户配置和可选的工作区配置加载。
- `doctor` 显示当前 SDK/runtime、目标框架和 SDK 锁定状态。

## 验收标准

1. Solution 可以构建。
2. 单元测试通过。
3. CLI 帮助可用。
4. `doctor` 在没有 API key 时也可运行。
5. 配置打印会遮蔽密钥。
6. 工作区覆盖有测试覆盖。
7. 阶段状态和验证输出已记录。
8. SDK 版本策略已记录，并能在 `doctor` 输出中诊断。

## 验证命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- --help
dotnet run --project src/CSharpAiCli.Cli -- doctor
dotnet run --project src/CSharpAiCli.Cli -- config get
```

## 风险与保护边界

- 本阶段不添加模型调用。
- 本阶段不添加文件编辑。
- 配置 schema 保持小而清晰。
- 不在仓库文件中存储 API key。

## 阶段交付物

- 可构建的 .NET solution
- 第一批 CLI 命令
- 测试框架
- 配置和工作区基础能力

## 阶段 01 验收记录

阶段 01 在第 4 周完成验收。验收记录见：

- `docs_md/spec/phase01_acceptance.md`
- `docs_md/spec/runtime_logging_diagnostics.md`
- `docs_md/weekly/04_week_review.md`

## 下一阶段输入

阶段 02 将使用 CLI 外壳、配置加载器和工作区上下文，添加模型流式输出和会话持久化。
