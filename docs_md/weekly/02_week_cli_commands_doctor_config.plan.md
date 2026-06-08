# 第 2 周 - CLI 命令、Doctor 与 Config 执行计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **历史状态：** 本计划作为历史实施记录保留；checkbox 状态已在 2026-06-08 验收固化时闭合，最终证据见 `02_week_review.md`。

**Goal:** 添加可运行的 `System.CommandLine` 根命令、`doctor`、`config get` 和帮助输出，让 CLI 在没有 API key 时也能给出清晰诊断。

**Architecture:** CLI 项目只负责命令解析和输出连接，Core 项目负责产品元数据、运行环境快照、doctor 报告和 config 报告。第 2 周不读取真实配置文件、不接入模型、不实现 workspace override；只建立命令外壳和可测试的诊断文本。

**Tech Stack:** C#、.NET SDK `9.0.308` 当前本机可用、`net9.0`、xUnit、`System.CommandLine` `2.0.8`。

---

## 来源

- 阶段计划：`docs_md/plans/01_foundation_cli_workspace.plan.md`
- 总排期：`docs_md/weekly/26_week_goal_schedule.md`
- 第 1 周计划：`docs_md/weekly/01_week_foundation_skeleton.plan.md`
- 第 1 周回顾：`docs_md/weekly/01_week_review.md`

第 2 周对应总排期目标：

```text
添加 System.CommandLine 根命令、doctor、config get 和帮助输出。
周末验收：CLI 帮助和 doctor 输出在没有 API key 时可用。
```

## 本周边界

本周必须完成：

- CLI 项目引用 `System.CommandLine` `2.0.8`
- `caicli --help` 可显示帮助、`doctor` 和 `config`
- `caicli doctor` 可运行并显示 runtime、SDK、目标框架、SDK 锁定状态、工作目录、配置路径和 API key 是否存在
- `caicli config get` 可运行并显示当前最小有效配置摘要
- `doctor` 和 `config get` 不打印密钥值
- Core 报告格式有单元测试
- CLI 命令连接层有单元测试或 smoke 验证
- `dotnet build src/CSharpAiCli.sln`
- `dotnet test src/CSharpAiCli.sln`

本周不做：

- `chat` 命令
- OpenAI SDK 或模型调用
- 真实配置文件读取与配置优先级合并
- `--workspace <path>` 覆盖
- workspace guard、patch、shell runner
- MCP 或 Microsoft Agent Framework
- 修改目标框架到 .NET 10 或 .NET 8 LTS

## Runtime 与包版本决策

当前本机只安装 .NET SDK `9.0.308`，第 1 周项目已使用 `net9.0`。第 2 周继续使用 `net9.0`，不在本周切换 SDK。`doctor` 必须把此状态诊断出来：

- `target framework: net9.0`
- `dotnet SDK: 9.0.308` 或实际 `dotnet --version` 输出
- `sdk lock: not locked`，除非工作目录已有 `global.json`

`System.CommandLine` 使用 NuGet 当前稳定版本 `2.0.8`，执行时使用精确版本号，不使用浮动版本。

## 文件结构

第 2 周计划创建或修改：

```text
src/
  CSharpAiCli.Cli/
    CSharpAiCli.Cli.csproj          # 添加 System.CommandLine 包
    CliCommandFactory.cs            # 创建根命令、doctor、config get
    Program.cs                      # 调用命令工厂并返回 exit code
  CSharpAiCli.Core/
    ProductInfo.cs                  # 添加显示名、描述、目标框架常量
    CliEnvironmentSnapshot.cs       # 捕获最小运行环境和配置路径状态
    DoctorReport.cs                 # 格式化 doctor 输出
    ConfigReport.cs                 # 格式化 config get 输出
  CSharpAiCli.Tests/
    CSharpAiCli.Tests.csproj        # 引用 CLI 项目，便于测试命令连接层
    ProductInfoTests.cs             # 替换第 1 周 UnitTest1.cs
    CliEnvironmentSnapshotTests.cs
    DoctorReportTests.cs
    ConfigReportTests.cs
    CliCommandFactoryTests.cs
docs_md/weekly/
  02_week_review.md                 # 周末完成后创建
```

## Task 1: 引入命令行包与 CLI 测试引用

**Files:**

- Modify: `src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj`
- Modify: `src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj`

- [x] **Step 1: 添加 System.CommandLine 精确版本**

Run:

```powershell
dotnet add src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj package System.CommandLine --version 2.0.8
```

Expected:

```text
PackageReference for package 'System.CommandLine' version '2.0.8' added
```

- [x] **Step 2: 让测试项目引用 CLI 项目**

Run:

```powershell
dotnet add src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj reference src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj
```

Expected:

```text
Reference ... CSharpAiCli.Cli.csproj added to the project.
```

- [x] **Step 3: 验证引用后 solution 仍可构建**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded.
```

## Task 2: 扩展产品元数据

**Files:**

- Rename: `src/CSharpAiCli.Tests/UnitTest1.cs` -> `src/CSharpAiCli.Tests/ProductInfoTests.cs`
- Modify: `src/CSharpAiCli.Core/ProductInfo.cs`

- [x] **Step 1: 重命名测试文件**

Run:

```powershell
Rename-Item -LiteralPath src/CSharpAiCli.Tests/UnitTest1.cs -NewName ProductInfoTests.cs
```

Expected:

```text
src/CSharpAiCli.Tests/ProductInfoTests.cs exists.
```

- [x] **Step 2: 写失败测试**

Replace `src/CSharpAiCli.Tests/ProductInfoTests.cs` with:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void Metadata_contains_cli_identity()
    {
        Assert.Equal("caicli", ProductInfo.CommandName);
        Assert.Equal("C# AI CLI", ProductInfo.DisplayName);
        Assert.Equal("Local AI engineering CLI", ProductInfo.Description);
        Assert.Equal("net9.0", ProductInfo.TargetFramework);
    }
}
```

- [x] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ProductInfoTests
```

Expected:

```text
Failed because ProductInfo.DisplayName, Description, or TargetFramework is not defined.
```

- [x] **Step 4: 实现产品元数据**

Replace `src/CSharpAiCli.Core/ProductInfo.cs` with:

```csharp
namespace CSharpAiCli.Core;

public static class ProductInfo
{
    public const string CommandName = "caicli";
    public const string DisplayName = "C# AI CLI";
    public const string Description = "Local AI engineering CLI";
    public const string TargetFramework = "net9.0";
}
```

- [x] **Step 5: 运行测试确认通过**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ProductInfoTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 1
```

## Task 3: 添加运行环境快照

**Files:**

- Create: `src/CSharpAiCli.Core/CliEnvironmentSnapshot.cs`
- Create: `src/CSharpAiCli.Tests/CliEnvironmentSnapshotTests.cs`

- [x] **Step 1: 写失败测试**

Create `src/CSharpAiCli.Tests/CliEnvironmentSnapshotTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliEnvironmentSnapshotTests
{
    [Fact]
    public void Create_builds_paths_and_status_without_reading_config_files()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "",
            hasGlobalJson: false);

        Assert.Equal("workspace-root", snapshot.CurrentDirectory);
        Assert.Equal(Path.Combine("user-home", ".caicli", "config.json"), snapshot.UserConfigPath);
        Assert.Equal(Path.Combine("workspace-root", ".caicli", "config.json"), snapshot.WorkspaceConfigPath);
        Assert.Equal("9.0.308", snapshot.DotnetSdkVersion);
        Assert.Equal(".NET 9.0.0", snapshot.DotnetRuntime);
        Assert.Equal("net9.0", snapshot.TargetFramework);
        Assert.False(snapshot.HasGlobalJson);
        Assert.False(snapshot.HasOpenAiApiKey);
    }

    [Fact]
    public void Create_marks_api_key_present_without_storing_the_value()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "sk-test-secret",
            hasGlobalJson: true);

        Assert.True(snapshot.HasOpenAiApiKey);
        Assert.True(snapshot.HasGlobalJson);
        Assert.DoesNotContain("sk-test-secret", snapshot.ToString(), StringComparison.Ordinal);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliEnvironmentSnapshotTests
```

Expected:

```text
Failed because CliEnvironmentSnapshot is not defined.
```

- [x] **Step 3: 实现运行环境快照**

Create `src/CSharpAiCli.Core/CliEnvironmentSnapshot.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSharpAiCli.Core;

public sealed record CliEnvironmentSnapshot(
    string CurrentDirectory,
    string UserConfigPath,
    string WorkspaceConfigPath,
    string DotnetSdkVersion,
    string DotnetRuntime,
    string TargetFramework,
    bool HasGlobalJson,
    bool HasOpenAiApiKey)
{
    public static CliEnvironmentSnapshot Create(
        string? currentDirectory = null,
        string? userProfile = null,
        string? dotnetSdkVersion = null,
        string? dotnetRuntime = null,
        string? openAiApiKey = null,
        bool? hasGlobalJson = null)
    {
        currentDirectory ??= Environment.CurrentDirectory;
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dotnetSdkVersion ??= ReadDotnetSdkVersion();
        dotnetRuntime ??= RuntimeInformation.FrameworkDescription;
        openAiApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        hasGlobalJson ??= File.Exists(Path.Combine(currentDirectory, "global.json"));

        return new CliEnvironmentSnapshot(
            CurrentDirectory: currentDirectory,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(currentDirectory, ".caicli", "config.json"),
            DotnetSdkVersion: dotnetSdkVersion,
            DotnetRuntime: dotnetRuntime,
            TargetFramework: ProductInfo.TargetFramework,
            HasGlobalJson: hasGlobalJson.Value,
            HasOpenAiApiKey: !string.IsNullOrWhiteSpace(openAiApiKey));
    }

    private static string ReadDotnetSdkVersion()
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("dotnet", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(milliseconds: 2000))
            {
                TryKill(process);
                return "unavailable";
            }

            string version = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(version) ? "unavailable" : version;
        }
        catch
        {
            return "unavailable";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliEnvironmentSnapshotTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 2
```

## Task 4: 添加 doctor 报告格式

**Files:**

- Create: `src/CSharpAiCli.Core/DoctorReport.cs`
- Create: `src/CSharpAiCli.Tests/DoctorReportTests.cs`

- [x] **Step 1: 写失败测试**

Create `src/CSharpAiCli.Tests/DoctorReportTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class DoctorReportTests
{
    [Fact]
    public void Create_formats_runtime_workspace_config_and_key_status()
    {
        CliEnvironmentSnapshot snapshot = new(
            CurrentDirectory: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false,
            HasOpenAiApiKey: false);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI doctor", text);
        Assert.Contains("command: caicli", text);
        Assert.Contains("target framework: net9.0", text);
        Assert.Contains("dotnet SDK: 9.0.308", text);
        Assert.Contains("dotnet runtime: .NET 9.0.0", text);
        Assert.Contains("sdk lock: not locked", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("user config: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspace config: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("api key: missing", text);
    }

    [Fact]
    public void Create_never_prints_api_key_value()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "sk-test-secret",
            hasGlobalJson: true);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("api key: present", text);
        Assert.Contains("sdk lock: global.json found", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter DoctorReportTests
```

Expected:

```text
Failed because DoctorReport is not defined.
```

- [x] **Step 3: 实现 doctor 报告**

Create `src/CSharpAiCli.Core/DoctorReport.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = snapshot.HasOpenAiApiKey ? "present" : "missing";

        return new DoctorReport(
        [
            $"{ProductInfo.DisplayName} doctor",
            $"command: {ProductInfo.CommandName}",
            $"target framework: {snapshot.TargetFramework}",
            $"dotnet SDK: {snapshot.DotnetSdkVersion}",
            $"dotnet runtime: {snapshot.DotnetRuntime}",
            $"sdk lock: {sdkLock}",
            $"workspace: {snapshot.CurrentDirectory}",
            $"user config: {snapshot.UserConfigPath}",
            $"workspace config: {snapshot.WorkspaceConfigPath}",
            $"api key: {apiKeyStatus}"
        ]);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter DoctorReportTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 2
```

## Task 5: 添加 config get 报告格式

**Files:**

- Create: `src/CSharpAiCli.Core/ConfigReport.cs`
- Create: `src/CSharpAiCli.Tests/ConfigReportTests.cs`

- [x] **Step 1: 写失败测试**

Create `src/CSharpAiCli.Tests/ConfigReportTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigReportTests
{
    [Fact]
    public void Create_formats_minimal_effective_configuration()
    {
        CliEnvironmentSnapshot snapshot = new(
            CurrentDirectory: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false,
            HasOpenAiApiKey: false);

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("userConfigPath: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspaceConfigPath: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("model: not configured", text);
        Assert.Contains("apiKey: missing", text);
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_value()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "sk-test-secret",
            hasGlobalJson: false);

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("apiKey: present", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConfigReportTests
```

Expected:

```text
Failed because ConfigReport is not defined.
```

- [x] **Step 3: 实现 config get 报告**

Create `src/CSharpAiCli.Core/ConfigReport.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ConfigReport(IReadOnlyList<string> Lines)
{
    public static ConfigReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string apiKeyStatus = snapshot.HasOpenAiApiKey ? "present" : "missing";

        return new ConfigReport(
        [
            $"{ProductInfo.DisplayName} effective configuration",
            $"workspace: {snapshot.CurrentDirectory}",
            $"userConfigPath: {snapshot.UserConfigPath}",
            $"workspaceConfigPath: {snapshot.WorkspaceConfigPath}",
            "model: not configured",
            $"apiKey: {apiKeyStatus}"
        ]);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConfigReportTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 2
```

## Task 6: 添加 System.CommandLine 命令工厂

**Files:**

- Create: `src/CSharpAiCli.Cli/CliCommandFactory.cs`
- Create: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [x] **Step 1: 写失败测试**

Create `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`:

```csharp
using System.CommandLine;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliCommandFactoryTests
{
    [Fact]
    public void Doctor_command_writes_doctor_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(new[] { "doctor" })
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", output.ToString());
        Assert.Contains("api key: missing", output.ToString());
    }

    [Fact]
    public void Config_get_command_writes_config_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(new[] { "config", "get" })
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI effective configuration", output.ToString());
        Assert.Contains("model: not configured", output.ToString());
    }

    private static CliEnvironmentSnapshot CreateSnapshot()
    {
        return new CliEnvironmentSnapshot(
            CurrentDirectory: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false,
            HasOpenAiApiKey: false);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Failed because CliCommandFactory is not defined.
```

- [x] **Step 3: 实现命令工厂**

Create `src/CSharpAiCli.Cli/CliCommandFactory.cs`:

```csharp
using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(output, CliEnvironmentSnapshot.Create);
    }

    public static RootCommand Create(TextWriter output, Func<CliEnvironmentSnapshot> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(_ =>
        {
            CliEnvironmentSnapshot snapshot = snapshotProvider();
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(_ =>
        {
            CliEnvironmentSnapshot snapshot = snapshotProvider();
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        configCommand.Subcommands.Add(configGetCommand);
        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);

        return rootCommand;
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 2
```

## Task 7: 接线 CLI 入口

**Files:**

- Modify: `src/CSharpAiCli.Cli/Program.cs`

- [x] **Step 1: 更新 Program.cs**

Replace `src/CSharpAiCli.Cli/Program.cs` with:

```csharp
using System.CommandLine;
using CSharpAiCli.Cli;

return CliCommandFactory
    .Create(Console.Out)
    .Parse(args)
    .Invoke();
```

- [x] **Step 2: 运行 help smoke check**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- --help
```

Expected output contains:

```text
caicli - Local AI engineering CLI
doctor
config
--help
--version
```

- [x] **Step 3: 运行 doctor smoke check**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- doctor
```

Expected output contains:

```text
C# AI CLI doctor
command: caicli
target framework: net9.0
dotnet SDK:
dotnet runtime:
sdk lock:
workspace:
user config:
workspace config:
api key:
```

- [x] **Step 4: 运行 config get smoke check**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- config get
```

Expected output contains:

```text
C# AI CLI effective configuration
workspace:
userConfigPath:
workspaceConfigPath:
model: not configured
apiKey:
```

## Task 8: 周末验收与回顾

**Files:**

- Verify: `src/CSharpAiCli.sln`
- Create: `docs_md/weekly/02_week_review.md`

- [x] **Step 1: 构建 solution**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded.
```

- [x] **Step 2: 运行全部测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Passed!  - Failed: 0
```

- [x] **Step 3: 验证 help**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- --help
```

Expected:

```text
输出包含 doctor、config、--help 和 --version。
```

- [x] **Step 4: 验证 doctor 无 key 可运行**

Run:

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = ""
dotnet run --project src/CSharpAiCli.Cli -- doctor
$env:OPENAI_API_KEY = $oldOpenAiKey
```

Expected:

```text
输出包含 api key: missing，且命令 exit code 为 0。
```

- [x] **Step 5: 验证 config get 不泄露 key**

Run:

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = "sk-week2-secret"
dotnet run --project src/CSharpAiCli.Cli -- config get
$env:OPENAI_API_KEY = $oldOpenAiKey
```

Expected:

```text
输出包含 apiKey: present。
输出不包含 sk-week2-secret。
```

- [x] **Step 6: 记录第 2 周回顾**

Create `docs_md/weekly/02_week_review.md`:

```markdown
## 第 2 周回顾

状态：Solidified

已完成：
- 添加 `System.CommandLine` 根命令
- 添加 `doctor` 命令
- 添加 `config get` 命令
- 添加运行环境快照、doctor 报告和 config 报告测试

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- --help`
- 结果：输出包含 doctor、config、--help
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- doctor`
- 结果：无 API key 时可运行，且不打印密钥
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- config get`
- 结果：输出最小配置摘要，且不打印密钥

风险：
- 当前仍使用 `net9.0` 和本机 SDK `9.0.308`，未通过 `global.json` 锁定 SDK。
- `config get` 目前只显示最小摘要，尚未读取真实配置文件。
- `workspace` 目前默认当前目录，尚未支持 `--workspace <path>` 覆盖。

下周输入：
- 添加工作区检测器。
- 添加配置加载器。
- 添加配置优先级与密钥遮蔽测试。
- 支持 `--workspace <path>` 覆盖。
```

## Acceptance

第 2 周完成时必须满足：

- CLI 项目引用 `System.CommandLine` `2.0.8`。
- `dotnet run --project src/CSharpAiCli.Cli -- --help` 输出可用帮助。
- help 输出包含 `doctor` 和 `config`。
- `dotnet run --project src/CSharpAiCli.Cli -- doctor` 在没有 API key 时 exit code 为 `0`。
- `doctor` 输出包含 SDK、runtime、target framework、SDK lock、workspace、config path 和 API key status。
- `dotnet run --project src/CSharpAiCli.Cli -- config get` 输出最小有效配置摘要。
- `doctor` 和 `config get` 不打印 API key 值。
- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- `docs_md/weekly/02_week_review.md` 记录验证输出、运行时风险和第 3 周输入。
