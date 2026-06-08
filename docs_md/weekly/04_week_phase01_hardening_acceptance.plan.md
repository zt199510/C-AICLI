# 第 4 周基础稳定、日志诊断与阶段 01 验收实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **历史状态：** 本计划作为历史实施记录保留；checkbox 状态已在 2026-06-08 验收固化时闭合，最终证据见 `04_week_review.md`。

**Goal:** 稳定阶段 01 的 CLI 基础能力，补齐轻量命令日志、诊断可见性、`chat` Phase 02 边界提示和阶段 01 验收文档。

**Architecture:** `CSharpAiCli.Core` 继续承载可测试的报告、日志路径解析、文件日志写入和 Phase 02 边界报告。`CSharpAiCli.Cli` 只负责命令接线：`doctor`、`config get` 和 `chat` 都通过现有 `CliEnvironmentSnapshot` 获取工作区与配置上下文，并在执行时写入脱敏命令日志。第 4 周不接入模型、不实现流式输出、不改变配置 schema 的密钥语义。

**Tech Stack:** C#、`net9.0`、当前机器 .NET SDK `9.0.308`、`System.CommandLine` `2.0.8`、xUnit、Windows PowerShell。

---

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 01 计划：`docs_md/plans/01_foundation_cli_workspace.plan.md`
- 第 3 周计划：`docs_md/weekly/03_week_workspace_config.plan.md`
- 第 3 周回顾：`docs_md/weekly/03_week_review.md`

第 4 周排期目标：

```text
稳定基础能力、日志、诊断和阶段 01 文档。
周末验收：阶段 01 验收清单通过。
```

第 3 周输入：

```text
稳定基础能力、日志、诊断和 Phase 01 文档。
补齐 chat 占位命令或明确推迟到 Phase 02。
记录 Phase 01 验收清单。
```

## 本周范围

第 4 周必须完成：

- 给 `doctor` 和 `config get` 增加日志目录诊断。
- 添加轻量命令日志：记录命令名、工作区状态、模型来源、API key 存在状态和配置 warning。
- 命令日志不得打印 `SecretValue.Value`、环境变量密钥、用户配置密钥或工作区配置密钥。
- `doctor`、`config get`、`chat` 执行时通过 CLI 接线层写入命令日志；日志写入失败不得阻止诊断输出。
- 添加 `chat` Phase 02 边界命令：帮助中可见，执行时返回清晰提示和非 0 exit code，不调用模型。
- 记录阶段 01 验收清单和运行时/日志诊断说明。
- 运行阶段 01 smoke：build、test、help、doctor、config get、chat boundary、日志脱敏。
- 创建第 4 周回顾，并把第 5 周输入明确为 model client 抽象和 OpenAI Responses API。

第 4 周不做：

- OpenAI SDK 或真实模型调用。
- 真实 `chat` 交互循环、流式渲染或会话存储。
- 文件读取、文件编辑、patch、shell runner 或工具调用。
- MCP 或 Microsoft Agent Framework。
- 迁移到 .NET 10 LTS 或 .NET 8 LTS。
- 扩展配置 schema 到 provider、temperature、instructions 或工具权限。

## 文件结构

第 4 周创建或修改：

```text
src/
  CSharpAiCli.Core/
    LogPathResolver.cs              # 新增：根据工作区状态解析日志目录
    CommandLogger.cs                # 新增：脱敏命令日志写入器
    ChatUnavailableReport.cs        # 新增：chat Phase 02 边界提示
    DoctorReport.cs                 # 修改：显示日志目录
    ConfigReport.cs                 # 修改：显示日志目录
  CSharpAiCli.Cli/
    CliCommandFactory.cs            # 修改：接入命令日志和 chat 边界命令
  CSharpAiCli.Tests/
    LogPathResolverTests.cs         # 新增：日志目录解析测试
    CommandLoggerTests.cs           # 新增：日志写入和脱敏测试
    ChatUnavailableReportTests.cs   # 新增：chat 边界提示测试
    DoctorReportTests.cs            # 修改：覆盖日志目录
    ConfigReportTests.cs            # 修改：覆盖日志目录
    CliCommandFactoryTests.cs       # 修改：覆盖日志接线和 chat 边界
docs_md/
  spec/
    phase01_acceptance.md           # 新增：阶段 01 验收清单
    runtime_logging_diagnostics.md  # 新增：运行时、日志、诊断说明
  plans/
    01_foundation_cli_workspace.plan.md # 修改：验收后状态改为 Accepted
  weekly/
    04_week_review.md               # 周末收尾时创建
```

## Task 1: 添加日志目录解析和报告诊断

**Files:**

- Create: `src/CSharpAiCli.Core/LogPathResolver.cs`
- Create: `src/CSharpAiCli.Tests/LogPathResolverTests.cs`
- Modify: `src/CSharpAiCli.Core/DoctorReport.cs`
- Modify: `src/CSharpAiCli.Core/ConfigReport.cs`
- Modify: `src/CSharpAiCli.Tests/DoctorReportTests.cs`
- Modify: `src/CSharpAiCli.Tests/ConfigReportTests.cs`

- [x] **Step 1: 写失败的日志目录解析测试**

Create `src/CSharpAiCli.Tests/LogPathResolverTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class LogPathResolverTests
{
    [Fact]
    public void ResolveLogDirectory_uses_workspace_logs_when_workspace_is_ready()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspaceRoot: Path.Combine("root", "workspace"),
            workspaceStatus: WorkspaceStatus.Ready,
            userProfile: Path.Combine("root", "home"));

        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);

        Assert.Equal(Path.Combine("root", "workspace", ".caicli", "logs"), logDirectory);
    }

    [Fact]
    public void ResolveLogDirectory_uses_user_logs_when_workspace_is_not_usable()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspaceRoot: Path.Combine("root", "missing"),
            workspaceStatus: WorkspaceStatus.Missing,
            userProfile: Path.Combine("root", "home"));

        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);

        Assert.Equal(Path.Combine("root", "home", ".caicli", "logs"), logDirectory);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string workspaceRoot,
        WorkspaceStatus workspaceStatus,
        string userProfile)
    {
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: workspaceStatus);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            ApiKey: null,
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter LogPathResolverTests
```

Expected:

```text
Failed because LogPathResolver is not defined.
```

- [x] **Step 3: 实现日志目录解析器**

Create `src/CSharpAiCli.Core/LogPathResolver.cs`:

```csharp
namespace CSharpAiCli.Core;

public static class LogPathResolver
{
    public static string ResolveLogDirectory(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Workspace.IsUsable)
        {
            return Path.Combine(snapshot.Workspace.RootPath, ".caicli", "logs");
        }

        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        return Path.Combine(userConfigDirectory ?? snapshot.Workspace.RootPath, "logs");
    }
}
```

- [x] **Step 4: 运行日志目录解析测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter LogPathResolverTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 2
```

- [x] **Step 5: 更新 doctor 报告测试**

Replace `src/CSharpAiCli.Tests/DoctorReportTests.cs` with:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class DoctorReportTests
{
    [Fact]
    public void Create_formats_runtime_workspace_config_log_and_key_status()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: null, apiKeySource: "missing");

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI doctor", text);
        Assert.Contains("command: caicli", text);
        Assert.Contains("target framework: net9.0", text);
        Assert.Contains("dotnet SDK: 9.0.308", text);
        Assert.Contains("dotnet runtime: .NET 9.0.0", text);
        Assert.Contains("sdk lock: not locked", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspace status: ready", text);
        Assert.Contains("user config: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspace config: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("log directory: " + Path.Combine("workspace-root", ".caicli", "logs"), text);
        Assert.Contains("api key: missing", text);
    }

    [Fact]
    public void Create_never_prints_api_key_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            hasGlobalJson: true);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("api key: present (OPENAI_API_KEY)", text);
        Assert.Contains("sdk lock: global.json found", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_prints_config_warnings()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            warnings: ["ignored invalid config: " + Path.Combine("workspace-root", ".caicli", "config.json")]);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("config warning: ignored invalid config", text);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        bool hasGlobalJson = false,
        IReadOnlyList<string>? warnings = null)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: warnings ?? []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: hasGlobalJson);
    }
}
```

- [x] **Step 6: 更新 config 报告测试**

Replace `src/CSharpAiCli.Tests/ConfigReportTests.cs` with:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigReportTests
{
    [Fact]
    public void Create_formats_effective_configuration_with_sources_and_log_directory()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            model: "gpt-workspace",
            modelSource: "workspace config");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("userConfigPath: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspaceConfigPath: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("logDirectory: " + Path.Combine("workspace-root", ".caicli", "logs"), text);
        Assert.Contains("model: gpt-workspace", text);
        Assert.Contains("modelSource: workspace config", text);
        Assert.Contains("apiKey: missing", text);
        Assert.Contains("apiKeySource: missing", text);
        Assert.Contains("loadedConfigPaths: none", text);
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("apiKey: present", text);
        Assert.Contains("apiKeySource: OPENAI_API_KEY", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_prints_loaded_config_paths_and_warnings()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            loadedConfigPaths: ["user-config.json", "workspace-config.json"],
            warnings: ["ignored invalid config: broken-config.json"]);

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("loadedConfigPaths: user-config.json; workspace-config.json", text);
        Assert.Contains("configWarning: ignored invalid config: broken-config.json", text);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        string model = "not configured",
        string modelSource = "default",
        IReadOnlyList<string>? loadedConfigPaths = null,
        IReadOnlyList<string>? warnings = null)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: model,
            ModelSource: modelSource,
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: loadedConfigPaths ?? [],
            Warnings: warnings ?? []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
```

- [x] **Step 7: 运行报告测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "DoctorReportTests|ConfigReportTests"
```

Expected:

```text
Failed because DoctorReport and ConfigReport do not print log directory yet.
```

- [x] **Step 8: 更新 doctor 报告实现**

Replace `src/CSharpAiCli.Core/DoctorReport.cs` with:

```csharp
namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = snapshot.Configuration.HasApiKey
            ? $"present ({snapshot.Configuration.ApiKeySource})"
            : "missing";

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} doctor",
            $"command: {ProductInfo.CommandName}",
            $"target framework: {snapshot.TargetFramework}",
            $"dotnet SDK: {snapshot.DotnetSdkVersion}",
            $"dotnet runtime: {snapshot.DotnetRuntime}",
            $"sdk lock: {sdkLock}",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"user config: {snapshot.UserConfigPath}",
            $"workspace config: {snapshot.WorkspaceConfigPath}",
            $"log directory: {LogPathResolver.ResolveLogDirectory(snapshot)}",
            $"api key: {apiKeyStatus}"
        ];

        foreach (string warning in snapshot.Configuration.Warnings)
        {
            lines.Add($"config warning: {warning}");
        }

        return new DoctorReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
```

- [x] **Step 9: 更新 config 报告实现**

Replace `src/CSharpAiCli.Core/ConfigReport.cs` with:

```csharp
namespace CSharpAiCli.Core;

public sealed record ConfigReport(IReadOnlyList<string> Lines)
{
    public static ConfigReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string apiKeyStatus = configuration.HasApiKey ? "present" : "missing";
        string loadedConfigPaths = configuration.LoadedConfigPaths.Count == 0
            ? "none"
            : string.Join("; ", configuration.LoadedConfigPaths);

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} effective configuration",
            $"workspace: {configuration.WorkspaceRoot}",
            $"workspaceStatus: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"userConfigPath: {configuration.UserConfigPath}",
            $"workspaceConfigPath: {configuration.WorkspaceConfigPath}",
            $"logDirectory: {LogPathResolver.ResolveLogDirectory(snapshot)}",
            $"model: {configuration.Model}",
            $"modelSource: {configuration.ModelSource}",
            $"apiKey: {apiKeyStatus}",
            $"apiKeySource: {configuration.ApiKeySource}",
            $"loadedConfigPaths: {loadedConfigPaths}"
        ];

        foreach (string warning in configuration.Warnings)
        {
            lines.Add($"configWarning: {warning}");
        }

        return new ConfigReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
```

- [x] **Step 10: 运行日志目录和报告测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "LogPathResolverTests|DoctorReportTests|ConfigReportTests"
```

Expected:

```text
Passed!  - Failed: 0, Passed: 8
```

- [x] **Step 11: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/LogPathResolver.cs src/CSharpAiCli.Core/DoctorReport.cs src/CSharpAiCli.Core/ConfigReport.cs src/CSharpAiCli.Tests/LogPathResolverTests.cs src/CSharpAiCli.Tests/DoctorReportTests.cs src/CSharpAiCli.Tests/ConfigReportTests.cs
git commit -m "feat: report diagnostic log directory"
```

Expected:

```text
Commit created with log directory diagnostics.
```

## Task 2: 添加脱敏命令日志写入器

**Files:**

- Create: `src/CSharpAiCli.Core/CommandLogger.cs`
- Create: `src/CSharpAiCli.Tests/CommandLoggerTests.cs`

- [x] **Step 1: 写失败的命令日志测试**

Create `src/CSharpAiCli.Tests/CommandLoggerTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CommandLoggerTests
{
    [Fact]
    public void Append_writes_command_log_without_secret_values()
    {
        string root = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                workspaceStatus: WorkspaceStatus.Ready,
                userProfile: Path.Combine(root, "home"),
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-workspace",
                modelSource: "workspace config");

            CommandLogger.Append(
                commandName: "doctor",
                snapshot: snapshot,
                timestampUtc: new DateTimeOffset(2026, 6, 24, 8, 30, 0, TimeSpan.Zero));

            string logPath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-06-24.log");
            string text = File.ReadAllText(logPath);

            Assert.Contains("timestampUtc=2026-06-24T08:30:00.0000000Z", text);
            Assert.Contains("command=doctor", text);
            Assert.Contains("workspaceStatus=ready", text);
            Assert.Contains("model=gpt-workspace", text);
            Assert.Contains("modelSource=workspace config", text);
            Assert.Contains("apiKey=present", text);
            Assert.Contains("apiKeySource=OPENAI_API_KEY", text);
            Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Append_uses_user_log_directory_when_workspace_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            Directory.CreateDirectory(Path.Combine(userProfile, ".caicli"));
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: Path.Combine(root, "missing"),
                workspaceStatus: WorkspaceStatus.Missing,
                userProfile: userProfile,
                apiKey: null,
                apiKeySource: "missing",
                model: "not configured",
                modelSource: "default");

            CommandLogger.Append(
                commandName: "config get",
                snapshot: snapshot,
                timestampUtc: new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

            string logPath = Path.Combine(userProfile, ".caicli", "logs", "2026-06-24.log");
            string text = File.ReadAllText(logPath);

            Assert.Contains("command=config get", text);
            Assert.Contains("workspaceStatus=missing", text);
            Assert.Contains("apiKey=missing", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Append_sanitizes_line_breaks_and_pipe_characters()
    {
        string root = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                workspaceStatus: WorkspaceStatus.Ready,
                userProfile: Path.Combine(root, "home"),
                apiKey: null,
                apiKeySource: "missing",
                model: "gpt" + Environment.NewLine + "workspace",
                modelSource: "workspace|config",
                warnings: ["first warning" + Environment.NewLine + "second warning"]);

            CommandLogger.Append(
                commandName: "doctor|config",
                snapshot: snapshot,
                timestampUtc: new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero));

            string logPath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-06-24.log");
            string text = File.ReadAllText(logPath);

            Assert.Contains("command=doctor/config", text);
            Assert.Contains("model=gpt workspace", text);
            Assert.Contains("modelSource=workspace/config", text);
            Assert.Contains("warnings=first warning second warning", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string workspaceRoot,
        WorkspaceStatus workspaceStatus,
        string userProfile,
        string? apiKey,
        string apiKeySource,
        string model,
        string modelSource,
        IReadOnlyList<string>? warnings = null)
    {
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: workspaceStatus);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: model,
            ModelSource: modelSource,
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: warnings ?? []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CommandLoggerTests
```

Expected:

```text
Failed because CommandLogger is not defined.
```

- [x] **Step 3: 实现命令日志写入器**

Create `src/CSharpAiCli.Core/CommandLogger.cs`:

```csharp
using System.Globalization;

namespace CSharpAiCli.Core;

public static class CommandLogger
{
    public static void Append(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(snapshot);

        DateTimeOffset timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);

        string fileName = timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
        string logPath = Path.Combine(logDirectory, fileName);
        string apiKeyStatus = snapshot.Configuration.HasApiKey ? "present" : "missing";
        string warnings = snapshot.Configuration.Warnings.Count == 0
            ? "none"
            : string.Join("; ", snapshot.Configuration.Warnings.Select(Sanitize));

        string line = string.Join(" | ",
        [
            $"timestampUtc={timestamp.UtcDateTime:O}",
            $"command={Sanitize(commandName)}",
            $"workspace={Sanitize(snapshot.CurrentDirectory)}",
            $"workspaceStatus={FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"model={Sanitize(snapshot.Configuration.Model)}",
            $"modelSource={Sanitize(snapshot.Configuration.ModelSource)}",
            $"apiKey={apiKeyStatus}",
            $"apiKeySource={Sanitize(snapshot.Configuration.ApiKeySource)}",
            $"warnings={warnings}"
        ]);

        File.AppendAllText(logPath, line + Environment.NewLine);
    }

    private static string Sanitize(string value)
    {
        string withoutPipes = value.Replace("|", "/", StringComparison.Ordinal);
        return string.Join(
            " ",
            withoutPipes.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
```

- [x] **Step 4: 运行命令日志测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CommandLoggerTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 3
```

- [x] **Step 5: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/CommandLogger.cs src/CSharpAiCli.Tests/CommandLoggerTests.cs
git commit -m "feat: add redacted command logging"
```

Expected:

```text
Commit created with command logging.
```

## Task 3: 将命令日志接入 CLI

**Files:**

- Modify: `src/CSharpAiCli.Cli/CliCommandFactory.cs`
- Modify: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [x] **Step 1: 替换 CLI 命令工厂测试**

Replace `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs` with:

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
            .Parse(["doctor"])
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
            .Parse(["config", "get"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI effective configuration", output.ToString());
        Assert.Contains("model: not configured", output.ToString());
    }

    [Fact]
    public void Workspace_option_is_passed_to_doctor_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["doctor", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Workspace_option_is_passed_to_config_get_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["config", "get", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Doctor_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["doctor"], loggedCommands);
    }

    [Fact]
    public void Config_get_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "get"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config get"], loggedCommands);
    }

    [Fact]
    public void Command_logger_failure_does_not_block_doctor_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (_, _) => throw new IOException("log directory unavailable"))
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", output.ToString());
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath)
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;

        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            ApiKey: null,
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
```

- [x] **Step 2: 运行 CLI 测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Failed because CliCommandFactory does not expose the command logger overload yet.
```

- [x] **Step 3: 更新 CLI 命令工厂实现**

Replace `src/CSharpAiCli.Cli/CliCommandFactory.cs` with:

```csharp
using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath),
            (commandName, snapshot) => CommandLogger.Append(commandName, snapshot));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        return Create(output, snapshotProvider, (_, _) => { });
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(commandLogger);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        Option<string> workspaceOption = new("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true,
        };
        rootCommand.Options.Add(workspaceOption);

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "doctor", snapshot);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config get", snapshot);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        configCommand.Subcommands.Add(configGetCommand);
        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);

        return rootCommand;
    }

    private static void TryWriteCommandLog(
        Action<string, CliEnvironmentSnapshot> commandLogger,
        string commandName,
        CliEnvironmentSnapshot snapshot)
    {
        try
        {
            commandLogger(commandName, snapshot);
        }
        catch
        {
        }
    }
}
```

- [x] **Step 4: 运行 CLI 命令工厂测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 7
```

- [x] **Step 5: Commit**

Run:

```powershell
git add src/CSharpAiCli.Cli/CliCommandFactory.cs src/CSharpAiCli.Tests/CliCommandFactoryTests.cs
git commit -m "feat: log cli command diagnostics"
```

Expected:

```text
Commit created with CLI logging.
```

## Task 4: 添加 `chat` Phase 02 边界命令

**Files:**

- Create: `src/CSharpAiCli.Core/ChatUnavailableReport.cs`
- Create: `src/CSharpAiCli.Tests/ChatUnavailableReportTests.cs`
- Modify: `src/CSharpAiCli.Cli/CliCommandFactory.cs`
- Modify: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [x] **Step 1: 写失败的 chat 边界报告测试**

Create `src/CSharpAiCli.Tests/ChatUnavailableReportTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChatUnavailableReportTests
{
    [Fact]
    public void Create_explains_phase_02_boundary_without_printing_secret_values()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: "sk-test-secret");

        string text = ChatUnavailableReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI chat", text);
        Assert.Contains("status: unavailable in Phase 01", text);
        Assert.Contains("planned phase: Phase 02", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspace status: ready", text);
        Assert.Contains("next: run doctor and config get to verify readiness before Phase 02", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? apiKey)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKey is null ? "missing" : "OPENAI_API_KEY",
            LoadedConfigPaths: [],
            Warnings: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
```

- [x] **Step 2: 运行 chat 边界报告测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ChatUnavailableReportTests
```

Expected:

```text
Failed because ChatUnavailableReport is not defined.
```

- [x] **Step 3: 实现 chat 边界报告**

Create `src/CSharpAiCli.Core/ChatUnavailableReport.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ChatUnavailableReport(IReadOnlyList<string> Lines)
{
    public static ChatUnavailableReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ChatUnavailableReport(
        [
            $"{ProductInfo.DisplayName} chat",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            "status: unavailable in Phase 01",
            "planned phase: Phase 02",
            "reason: model client and streaming renderer are scheduled for weeks 5-6",
            "next: run doctor and config get to verify readiness before Phase 02"
        ]);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
```

- [x] **Step 4: 运行 chat 边界报告测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ChatUnavailableReportTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 1
```

- [x] **Step 5: 扩展 CLI 测试覆盖 chat 命令**

Add this test to `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs` before `CreateSnapshot`:

```csharp
    [Fact]
    public void Chat_command_returns_phase_02_boundary_message_and_logs_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(workspacePath);
                },
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["chat", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(2, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Equal(["chat"], loggedCommands);
        Assert.Contains("C# AI CLI chat", output.ToString());
        Assert.Contains("status: unavailable in Phase 01", output.ToString());
        Assert.Contains("planned phase: Phase 02", output.ToString());
        Assert.Contains("workspace: custom-root", output.ToString());
    }
```

- [x] **Step 6: 运行 CLI 测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Failed because CliCommandFactory does not add chat yet.
```

- [x] **Step 7: 更新 CLI 命令工厂实现**

In `src/CSharpAiCli.Cli/CliCommandFactory.cs`, add this command after `configCommand.Subcommands.Add(configGetCommand);` and before root subcommands are added:

```csharp
        Command chatCommand = new("chat", "Explain the Phase 02 chat boundary for this build.");
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);
            output.WriteLine(ChatUnavailableReport.Create(snapshot).ToDisplayText());
            return 2;
        });
```

Then add chat to the root commands:

```csharp
        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(chatCommand);
```

The final `CliCommandFactory.cs` should be:

```csharp
using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath),
            (commandName, snapshot) => CommandLogger.Append(commandName, snapshot));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        return Create(output, snapshotProvider, (_, _) => { });
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(commandLogger);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        Option<string> workspaceOption = new("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true,
        };
        rootCommand.Options.Add(workspaceOption);

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "doctor", snapshot);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config get", snapshot);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        configCommand.Subcommands.Add(configGetCommand);

        Command chatCommand = new("chat", "Explain the Phase 02 chat boundary for this build.");
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);
            output.WriteLine(ChatUnavailableReport.Create(snapshot).ToDisplayText());
            return 2;
        });

        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(chatCommand);

        return rootCommand;
    }

    private static void TryWriteCommandLog(
        Action<string, CliEnvironmentSnapshot> commandLogger,
        string commandName,
        CliEnvironmentSnapshot snapshot)
    {
        try
        {
            commandLogger(commandName, snapshot);
        }
        catch
        {
        }
    }
}
```

- [x] **Step 8: 运行 chat 和 CLI 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "ChatUnavailableReportTests|CliCommandFactoryTests"
```

Expected:

```text
Passed!  - Failed: 0, Passed: 9
```

- [x] **Step 9: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/ChatUnavailableReport.cs src/CSharpAiCli.Cli/CliCommandFactory.cs src/CSharpAiCli.Tests/ChatUnavailableReportTests.cs src/CSharpAiCli.Tests/CliCommandFactoryTests.cs
git commit -m "feat: add phase two chat boundary"
```

Expected:

```text
Commit created with chat boundary command.
```

## Task 5: 记录阶段 01 验收和诊断文档

**Files:**

- Create: `docs_md/spec/phase01_acceptance.md`
- Create: `docs_md/spec/runtime_logging_diagnostics.md`

- [x] **Step 1: 创建阶段 01 验收清单**

Create `docs_md/spec/phase01_acceptance.md`:

```markdown
# Phase 01 验收清单

状态：待第 4 周 smoke 验证后标记为 Accepted

## 范围

Phase 01 覆盖第 1-4 周：

- solution 和项目骨架
- CLI 根命令
- `doctor`
- `config get`
- `chat` Phase 02 边界提示
- 工作区检测
- `--workspace <path>` 覆盖
- 用户配置与工作区配置加载
- 配置优先级
- API key 脱敏
- 轻量命令日志
- 诊断报告和测试基础

## 验收命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- --help
dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .
dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace .
```

## 必须通过的行为

- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- help 输出包含 `doctor`、`config`、`chat`、`--workspace` 和 `--help`。
- `doctor --workspace .` 以 `0` 退出。
- `doctor` 输出 SDK、runtime、target framework、SDK lock、workspace、config path、log directory 和 API key 状态。
- `config get --workspace .` 以 `0` 退出。
- `config get` 输出 workspace、config path、log directory、model、model source、api key 状态和 api key source。
- `chat --workspace .` 返回 Phase 02 边界提示，不调用模型。
- `doctor`、`config get`、`chat`、命令日志和对象字符串不打印原始 API key。
- 工作区缺失或不是目录时，诊断命令不崩溃。
- 配置文件缺失时不产生 warning。
- 无效配置文件会被忽略，并显示 config warning。
- 命令日志写入失败不会阻止 `doctor` 或 `config get` 输出。

## Phase 02 输入

Phase 02 可以依赖以下基础：

- `CliEnvironmentSnapshot`
- `WorkspaceContext`
- `EffectiveConfiguration`
- `SecretValue`
- `ConfigLoader`
- `CommandLogger`
- `doctor` 和 `config get` 诊断输出
- `chat` 命令名和 Phase 02 边界位置

Phase 02 的第一个实现目标是 model client 抽象和 OpenAI SDK Responses API runner。
```

- [x] **Step 2: 创建运行时、日志、诊断说明**

Create `docs_md/spec/runtime_logging_diagnostics.md`:

```markdown
# 运行时、日志与诊断说明

## Runtime

当前项目目标框架为 `net9.0`，本机 SDK 为 `9.0.308`。当前仓库没有 `global.json` SDK 锁定。

`doctor` 负责显示：

- target framework
- dotnet SDK
- dotnet runtime
- SDK lock 状态
- workspace 路径和状态
- 用户配置路径
- 工作区配置路径
- 日志目录
- API key 是否存在

## 配置来源

第 4 周仍沿用第 3 周的最小配置 schema：

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-example"
}
```

配置优先级：

- API key：`OPENAI_API_KEY` > 工作区配置 `apiKey` > 用户配置 `apiKey` > missing
- model：工作区配置 `model` > 用户配置 `model` > `not configured`

报告和日志只打印 API key 的 `present` 或 `missing`，以及来源；不打印原始 key。

## 日志目录

当工作区状态是 `ready`：

```text
<workspace>/.caicli/logs
```

当工作区状态是 `missing` 或 `not directory`：

```text
<user profile>/.caicli/logs
```

命令日志按 UTC 日期写入：

```text
yyyy-MM-dd.log
```

每行日志记录：

- `timestampUtc`
- `command`
- `workspace`
- `workspaceStatus`
- `model`
- `modelSource`
- `apiKey`
- `apiKeySource`
- `warnings`

日志不记录 `SecretValue.Value`。

## Chat 边界

第 4 周只添加 `chat` 的 Phase 02 边界提示。该命令用于告诉用户模型客户端和流式渲染器属于第 5-6 周，不执行模型调用。

`chat` 命令可以使用 `--workspace <path>`，用于显示与记录当前工作区上下文。
```

- [x] **Step 3: 检查文档没有占位内容**

Run:

```powershell
$patterns = @('TB' + 'D', 'TO' + 'DO', 'fill in ' + 'details', 'implement ' + 'later')
rg -n ($patterns -join '|') docs_md/spec/phase01_acceptance.md docs_md/spec/runtime_logging_diagnostics.md
```

Expected:

```text
No matches.
```

- [x] **Step 4: Commit**

Run:

```powershell
git add docs_md/spec/phase01_acceptance.md docs_md/spec/runtime_logging_diagnostics.md
git commit -m "docs: add phase one acceptance diagnostics"
```

Expected:

```text
Commit created with Phase 01 diagnostics docs.
```

## Task 6: 阶段 01 smoke 验证与第 4 周回顾

**Files:**

- Verify: `src/CSharpAiCli.sln`
- Modify: `docs_md/plans/01_foundation_cli_workspace.plan.md`
- Modify: `docs_md/weekly/26_week_goal_schedule.md`
- Create: `docs_md/weekly/04_week_review.md`

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

- [x] **Step 3: 验证 help 包含阶段 01 命令和 workspace 选项**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- --help
```

Expected output contains:

```text
doctor
config
chat
--workspace
--help
```

- [x] **Step 4: 验证 doctor 输出**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .
```

Expected output contains:

```text
C# AI CLI doctor
workspace status: ready
user config:
workspace config:
log directory:
api key:
```

- [x] **Step 5: 验证 config get 输出和脱敏**

Run:

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = "sk-week4-secret"
dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .
$env:OPENAI_API_KEY = $oldOpenAiKey
```

Expected:

```text
输出包含 apiKey: present。
输出包含 apiKeySource: OPENAI_API_KEY。
输出包含 logDirectory:。
输出不包含 sk-week4-secret。
命令以 0 退出。
```

- [x] **Step 6: 验证 chat Phase 02 边界**

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace .
```

Expected output contains:

```text
C# AI CLI chat
status: unavailable in Phase 01
planned phase: Phase 02
```

Expected exit code:

```text
2
```

- [x] **Step 7: 验证命令日志写入且不泄露密钥**

Run:

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = "sk-week4-log-secret"
dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .
dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .
$env:OPENAI_API_KEY = $oldOpenAiKey
$today = [System.DateTime]::UtcNow.ToString("yyyy-MM-dd")
$logPath = Join-Path (Resolve-Path ".") ".caicli/logs/$today.log"
Get-Content -LiteralPath $logPath
```

Expected:

```text
日志包含 command=doctor。
日志包含 command=config get。
日志包含 apiKey=present。
日志包含 apiKeySource=OPENAI_API_KEY。
日志不包含 sk-week4-log-secret。
```

- [x] **Step 8: 更新阶段 01 计划状态**

In `docs_md/plans/01_foundation_cli_workspace.plan.md`, replace:

```markdown
## 状态

`Planned`
```

with:

```markdown
## 状态

`Accepted`
```

Then append this section before `## 下一阶段输入`:

```markdown
## 阶段 01 验收记录

阶段 01 在第 4 周完成验收。验收记录见：

- `docs_md/spec/phase01_acceptance.md`
- `docs_md/spec/runtime_logging_diagnostics.md`
- `docs_md/weekly/04_week_review.md`
```

- [x] **Step 9: 更新总周计划当前进度**

In `docs_md/weekly/26_week_goal_schedule.md`, replace the current progress list with:

```markdown
- 第 1 周已稳固：solution 骨架、仓库布局、文档目录和测试项目已建立。详见 `01_week_review.md`。
- 第 2 周已稳固：`System.CommandLine` 根命令、`doctor`、`config get`、运行环境快照和配置报告测试已完成。详见 `02_week_review.md`。
- 第 3 周已稳固：工作区检测、配置加载、配置优先级、密钥脱敏和 `--workspace <path>` 覆盖支持已完成。详见 `03_week_review.md`。
- 第 4 周已验收：日志、诊断、`chat` Phase 02 边界提示和阶段 01 文档已完成。详见 `04_week_review.md`。
- 当前下一步是第 5 周：添加 model client 抽象和 OpenAI SDK Responses API 实现。
- 运行时仍为 `net9.0`，本地 .NET SDK 为 `9.0.308`，当前没有 `global.json` SDK 锁定。
```

Then update the week 3 and week 4 rows:

```markdown
| 3 | 2024-06-17 至 2024-06-23 | 阶段 01 | 已稳固 | 添加工作区检测器和配置加载器，并支持密钥遮蔽。详见 `03_week_workspace_config.plan.md` 和 `03_week_review.md`。 | 已验证：工作区覆盖和配置遮蔽测试通过。 |
| 4 | 2024-06-24 至 2024-06-30 | 阶段 01 | 已验收 | 稳定基础能力、日志、诊断、`chat` Phase 02 边界提示和阶段 01 文档。详见 `04_week_phase01_hardening_acceptance.plan.md` 和 `04_week_review.md`。 | 已验证：阶段 01 验收清单通过。 |
```

- [x] **Step 10: 创建第 4 周回顾**

Create `docs_md/weekly/04_week_review.md`:

```markdown
# 第 04 周回顾
状态：已验收

已完成：
- 添加日志目录解析，`doctor` 和 `config get` 会显示生效日志目录。
- 添加脱敏命令日志，记录命令、工作区状态、模型来源、API key 状态和配置 warning。
- 将 `doctor`、`config get` 和 `chat` 接入命令日志，日志写入失败不会阻止诊断输出。
- 添加 `chat` Phase 02 边界提示命令，明确真实 chat 归属 Phase 02。
- 创建 Phase 01 验收清单和运行时、日志、诊断说明。
- 将阶段 01 状态更新为 `Accepted`。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded，0 warnings，0 errors
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- --help`
- 结果：输出包含 `doctor`、`config`、`chat`、`--workspace` 和 `--help`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .`
- 结果：输出包含 `C# AI CLI doctor`、工作区状态、配置路径、日志目录和 API key 状态
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .`
- 结果：输出包含有效配置、配置来源、日志目录和密钥来源，并且不打印密钥值
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace .`
- 结果：输出 Phase 02 边界提示，返回 exit code 2，不调用模型
- 命令：命令日志脱敏 smoke
- 结果：日志包含 `command=doctor`、`command=config get` 和 `apiKey=present`，不包含测试密钥值

运行时说明：
- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 仍为 `9.0.308`。
- 当前没有 `global.json` SDK 锁定。
- 真实模型调用尚未接入，`chat` 当前只提供 Phase 02 边界提示。

风险：
- 配置 schema 仍是最小 schema，只覆盖 `model` 和 `apiKey`。
- 工作区检测只负责诊断和路径解析，尚未实现文件访问越界保护。
- 命令日志是本地文本日志，尚未实现轮转、大小限制或结构化 JSON 输出。

第 05 周输入：
- 添加 model client 抽象。
- 添加 OpenAI SDK Responses API 实现。
- 添加 smoke 命令或等价最小模型调用验证。
- 缺少 API key 时给出清晰错误，且不打印密钥值。
```

- [x] **Step 11: 运行文档占位扫描**

Run:

```powershell
$patterns = @('TB' + 'D', 'TO' + 'DO', 'fill in ' + 'details', 'implement ' + 'later')
rg -n ($patterns -join '|') docs_md/weekly/04_week_review.md docs_md/spec/phase01_acceptance.md docs_md/spec/runtime_logging_diagnostics.md docs_md/plans/01_foundation_cli_workspace.plan.md docs_md/weekly/26_week_goal_schedule.md
```

Expected:

```text
No matches.
```

- [x] **Step 12: Commit**

Run:

```powershell
git add docs_md/plans/01_foundation_cli_workspace.plan.md docs_md/weekly/26_week_goal_schedule.md docs_md/weekly/04_week_review.md
git commit -m "docs: accept phase one foundation"
```

Expected:

```text
Commit created with Phase 01 acceptance record.
```

## 验收标准

第 4 周完成时必须满足：

- `LogPathResolverTests` 通过。
- `CommandLoggerTests` 通过。
- `ChatUnavailableReportTests` 通过。
- 已更新的 `DoctorReportTests`、`ConfigReportTests` 和 `CliCommandFactoryTests` 通过。
- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- `dotnet run --project src/CSharpAiCli.Cli -- --help` 包含 `doctor`、`config`、`chat` 和 `--workspace`。
- `dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .` 以 `0` 退出，并显示日志目录。
- `dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .` 以 `0` 退出，并显示日志目录。
- `dotnet run --project src/CSharpAiCli.Cli -- chat --workspace .` 输出 Phase 02 边界提示，并以 `2` 退出。
- 命令日志会写入 `<workspace>/.caicli/logs/yyyy-MM-dd.log`。
- 工作区不可用时，命令日志会写入 `<user profile>/.caicli/logs/yyyy-MM-dd.log`。
- 报告、日志和对象字符串不打印来自 env、用户配置或工作区配置的原始 API key。
- `docs_md/spec/phase01_acceptance.md` 记录阶段 01 验收清单。
- `docs_md/spec/runtime_logging_diagnostics.md` 记录运行时、日志和诊断策略。
- `docs_md/weekly/04_week_review.md` 记录验证输出和第 5 周输入。
- `docs_md/plans/01_foundation_cli_workspace.plan.md` 状态更新为 `Accepted`。
