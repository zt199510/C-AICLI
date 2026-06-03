# 第 1 周 - CLI 骨架与仓库布局执行计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 创建可构建、可测试的 C# AI CLI 空骨架，为阶段 01 后续 CLI 命令、配置、workspace 和 doctor 能力打基础。

**Architecture:** 本周只搭建 solution、CLI 项目、Core 项目和 xUnit 测试项目。CLI 作为薄入口，Core 承载后续业务逻辑；本周不接入模型、不实现文件编辑、不实现真实命令行为。

**Tech Stack:** C#、.NET SDK `9.0.308` 当前本机可用、xUnit、console app、class library。

---

## 来源

- 阶段计划：`docs_md/plans/01_foundation_cli_workspace.plan.md`
- 总排期：`docs_md/weekly/26_week_goal_schedule.md`

第 1 周对应总排期目标：

```text
创建 solution 骨架、仓库布局、第一批文档和测试项目。
周末验收：dotnet build 和 dotnet test 可在空骨架上运行。
```

## 本周边界

本周必须完成：

- `src/CSharpAiCli.sln`
- `src/CSharpAiCli.Cli/`
- `src/CSharpAiCli.Core/`
- `src/CSharpAiCli.Tests/`
- `docs_md/spec/`
- 一个能通过的 Core 单元测试
- `dotnet build src/CSharpAiCli.sln`
- `dotnet test src/CSharpAiCli.sln`

本周不做：

- System.CommandLine 根命令
- `doctor`、`config get`、`chat` 命令
- OpenAI SDK 或模型调用
- workspace guard、patch、shell runner
- MCP 或 Microsoft Agent Framework

## Runtime 决策

当前本机只安装 .NET SDK `9.0.308`，未检测到 `global.json`。为保证第 1 周可以立即构建：

- 本周项目初始目标框架使用 `net9.0`。
- 第 1 周记录此临时选择。
- 阶段 01 后续必须决定是否改为 `.NET 10 LTS` 或安装并锁定 `.NET 8 LTS`。
- 如果执行前已安装 .NET 10 SDK，则把下面命令中的 `net9.0` 改为 `net10.0`，并在周回顾中记录。

## Task 1: 创建源码与文档目录

**Files:**

- Create: `src/`
- Create: `docs_md/spec/`
- Verify: `docs_md/weekly/01_week_foundation_skeleton.plan.md`

- [ ] **Step 1: 创建目录**

Run:

```powershell
New-Item -ItemType Directory -Force src
New-Item -ItemType Directory -Force docs_md/spec
```

Expected:

```text
src 和 docs_md/spec 目录存在。
```

- [ ] **Step 2: 验证目录**

Run:

```powershell
Test-Path src
Test-Path docs_md/spec
```

Expected:

```text
True
True
```

## Task 2: 创建 solution 和项目

**Files:**

- Create: `src/CSharpAiCli.sln`
- Create: `src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj`
- Create: `src/CSharpAiCli.Core/CSharpAiCli.Core.csproj`
- Create: `src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj`

- [ ] **Step 1: 创建 solution**

Run:

```powershell
dotnet new sln -n CSharpAiCli -o src
```

Expected:

```text
The template "Solution File" was created successfully.
```

- [ ] **Step 2: 创建 Core class library**

Run:

```powershell
dotnet new classlib -n CSharpAiCli.Core -o src/CSharpAiCli.Core -f net9.0
```

Expected:

```text
The template "Class Library" was created successfully.
```

- [ ] **Step 3: 创建 CLI console app**

Run:

```powershell
dotnet new console -n CSharpAiCli.Cli -o src/CSharpAiCli.Cli -f net9.0
```

Expected:

```text
The template "Console App" was created successfully.
```

- [ ] **Step 4: 创建 xUnit 测试项目**

Run:

```powershell
dotnet new xunit -n CSharpAiCli.Tests -o src/CSharpAiCli.Tests -f net9.0
```

Expected:

```text
The template "xUnit Test Project" was created successfully.
```

- [ ] **Step 5: 加入 solution**

Run:

```powershell
dotnet sln src/CSharpAiCli.sln add src/CSharpAiCli.Core/CSharpAiCli.Core.csproj
dotnet sln src/CSharpAiCli.sln add src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj
dotnet sln src/CSharpAiCli.sln add src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj
```

Expected:

```text
Project ... added to the solution.
```

## Task 3: 建立项目引用

**Files:**

- Modify: `src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj`
- Modify: `src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj`

- [ ] **Step 1: CLI 引用 Core**

Run:

```powershell
dotnet add src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj reference src/CSharpAiCli.Core/CSharpAiCli.Core.csproj
```

Expected:

```text
Reference ... added to the project.
```

- [ ] **Step 2: Tests 引用 Core**

Run:

```powershell
dotnet add src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj reference src/CSharpAiCli.Core/CSharpAiCli.Core.csproj
```

Expected:

```text
Reference ... added to the project.
```

## Task 4: 写第一个 Core 测试和最小实现

**Files:**

- Create: `src/CSharpAiCli.Core/ProductInfo.cs`
- Modify: `src/CSharpAiCli.Tests/UnitTest1.cs`

- [ ] **Step 1: 写失败测试**

Replace `src/CSharpAiCli.Tests/UnitTest1.cs` with:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void Name_returns_product_command_name()
    {
        Assert.Equal("caicli", ProductInfo.CommandName);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
测试失败，因为 ProductInfo 类型尚不存在。
```

- [ ] **Step 3: 写最小实现**

Create `src/CSharpAiCli.Core/ProductInfo.cs`:

```csharp
namespace CSharpAiCli.Core;

public static class ProductInfo
{
    public const string CommandName = "caicli";
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Passed!  - Failed: 0, Passed: 1
```

## Task 5: 让 CLI 入口使用 Core 常量

**Files:**

- Modify: `src/CSharpAiCli.Cli/Program.cs`

- [ ] **Step 1: 更新 CLI 入口**

Replace `src/CSharpAiCli.Cli/Program.cs` with:

```csharp
using CSharpAiCli.Core;

Console.WriteLine($"{ProductInfo.CommandName} foundation skeleton");
```

- [ ] **Step 2: 运行 CLI smoke check**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli
```

Expected:

```text
caicli foundation skeleton
```

## Task 6: 周末验收

**Files:**

- Verify: `src/CSharpAiCli.sln`
- Verify: `src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj`
- Verify: `src/CSharpAiCli.Core/CSharpAiCli.Core.csproj`
- Verify: `src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj`

- [ ] **Step 1: 构建 solution**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 2: 运行全部测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 3: 记录第 1 周回顾**

Create `docs_md/weekly/01_week_review.md`:

```markdown
## 第 1 周回顾

状态：Solidified

已完成：
- 创建 `src/CSharpAiCli.sln`
- 创建 CLI、Core、Tests 三个项目
- 创建 `docs_md/spec/`
- Core 第一个单元测试通过

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0

风险：
- 当前本机只有 .NET SDK 9.0.308；阶段 01 后续需要决定是否升级到 .NET 10 LTS 或锁定其他 LTS SDK。

下周输入：
- 添加 System.CommandLine 根命令、`doctor`、`config get` 和帮助输出。
```

## Acceptance

第 1 周完成时必须满足：

- `src/CSharpAiCli.sln` 存在。
- CLI、Core、Tests 三项目都在 solution 中。
- CLI 和 Tests 都引用 Core。
- 至少一个 xUnit 测试通过。
- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- 周回顾记录了 SDK 版本风险和第 2 周输入。
