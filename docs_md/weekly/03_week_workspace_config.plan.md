# 第 3 周工作区与配置实施计划

> **给 agentic workers：** 必须使用 `superpowers:subagent-driven-development` 或 `superpowers:executing-plans` 按任务逐步执行本计划。步骤使用 checkbox（`- [ ]`）语法跟踪进度。

> **历史状态：** 本计划作为历史实施记录保留；checkbox 状态已在 2026-06-08 验收固化时闭合，最终证据见 `03_week_review.md`。

**目标：** 添加工作区检测、真实配置加载、配置优先级、密钥脱敏，以及 `--workspace <path>` 覆盖支持。

**架构：** `CSharpAiCli.Core` 负责工作区检测、配置文件解析、配置优先级、密钥处理、运行时快照和脱敏报告。`CSharpAiCli.Cli` 只负责解析命令/选项，并把用户指定的工作区路径传给 Core。报告继续保持文本优先和确定性输出，方便在接入模型之前测试。

**技术栈：** C#、`net9.0`、当前机器 .NET SDK `9.0.308`、`System.CommandLine` `2.0.8`、`System.Text.Json`、xUnit。

---

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 01 计划：`docs_md/plans/01_foundation_cli_workspace.plan.md`
- 第 2 周计划：`docs_md/weekly/02_week_cli_commands_doctor_config.plan.md`
- 第 2 周回顾：`docs_md/weekly/02_week_review.md`

第 3 周排期目标：

```text
添加工作区检测器和配置加载器，并支持密钥遮蔽。
周末验收：工作区覆盖和配置遮蔽测试通过。
```

## 本周范围

第 3 周必须完成：

- 从当前目录或 `--workspace <path>` 检测生效工作区。
- 将相对工作区覆盖路径按进程当前目录解析。
- 报告工作区状态：`ready`、`missing` 或 `not directory`。
- 从 `<user profile>/.caicli/config.json` 加载用户配置。
- 仅当工作区是可用目录时，从 `<workspace>/.caicli/config.json` 加载工作区配置。
- 按优先级合并配置：环境变量 `OPENAI_API_KEY` > 工作区配置 > 用户配置 > 默认值。
- 通过脱敏的 `SecretValue` 包装器保留 API key，供后续代码使用。
- 确保 `doctor`、`config get`、对象 `ToString()` 和报告都不打印原始 API key。
- 添加递归 `--workspace <path>` 选项，让 `doctor` 和 `config get` 都可使用。
- 保持 `dotnet build src/CSharpAiCli.sln` 和 `dotnet test src/CSharpAiCli.sln` 通过。

第 3 周不做：

- `chat`
- OpenAI SDK 或模型调用
- 文件编辑工具
- 文件读取的工作区边界保护
- patch 应用
- shell runner
- MCP
- Microsoft Agent Framework
- 目标框架迁移

## 配置 Schema

第 3 周使用刻意保持很小的 JSON schema：

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-example"
}
```

规则：

- 缺失文件是正常情况，不产生 warning。
- 空 JSON 或无效 JSON 文件会被忽略，并产生一行 warning。
- `model` 使用工作区配置覆盖用户配置。
- API key 优先级是 `OPENAI_API_KEY`，然后是工作区配置 `apiKey`，最后是用户配置 `apiKey`。
- 报告只打印 `present` 或 `missing` 以及来源，不打印原始 key。

## 文件结构

第 3 周创建或修改：

```text
src/
  CSharpAiCli.Core/
    WorkspaceStatus.cs              # 新增：工作区检测状态枚举
    WorkspaceContext.cs             # 新增：工作区检测器和配置路径解析器
    SecretValue.cs                  # 新增：脱敏密钥包装器
    CliConfigFile.cs                # 新增：JSON 配置结构
    EffectiveConfiguration.cs       # 新增：合并后的生效配置模型
    ConfigLoader.cs                 # 新增：配置文件加载器和优先级规则
    CliEnvironmentSnapshot.cs       # 修改：组合工作区和生效配置
    DoctorReport.cs                 # 修改：显示工作区/配置/key 状态
    ConfigReport.cs                 # 修改：显示生效配置来源
  CSharpAiCli.Cli/
    CliCommandFactory.cs            # 修改：添加递归 --workspace 选项
  CSharpAiCli.Tests/
    WorkspaceContextTests.cs        # 新增：工作区检测器测试
    SecretValueTests.cs             # 新增：密钥脱敏测试
    ConfigLoaderTests.cs            # 新增：配置优先级和 warning 测试
    CliEnvironmentSnapshotTests.cs  # 修改：覆盖工作区/配置快照
    DoctorReportTests.cs            # 修改：报告测试
    ConfigReportTests.cs            # 修改：报告测试
    CliCommandFactoryTests.cs       # 修改：CLI 选项测试
docs_md/weekly/
  03_week_review.md                 # 周末收尾时创建
```

## 任务 1: 添加工作区检测

**文件：**

- 创建: `src/CSharpAiCli.Core/WorkspaceStatus.cs`
- 创建: `src/CSharpAiCli.Core/WorkspaceContext.cs`
- 创建: `src/CSharpAiCli.Tests/WorkspaceContextTests.cs`

- [x] **Step 1: 编写失败的工作区检测测试**

创建 `src/CSharpAiCli.Tests/WorkspaceContextTests.cs`：

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceContextTests
{
    [Fact]
    public void Detect_uses_current_directory_when_workspace_path_is_empty()
    {
        string root = CreateTempDirectory();

        try
        {
            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: null,
                currentDirectory: root);

            Assert.Equal(Path.GetFullPath(root), context.RootPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), ".caicli", "config.json"), context.ConfigPath);
            Assert.Equal(WorkspaceStatus.Ready, context.Status);
            Assert.True(context.IsUsable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_relative_workspace_path_against_current_directory()
    {
        string root = CreateTempDirectory();

        try
        {
            string workspace = Path.Combine(root, "project");
            Directory.CreateDirectory(workspace);

            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: "project",
                currentDirectory: root);

            Assert.Equal(Path.GetFullPath(workspace), context.RootPath);
            Assert.Equal(WorkspaceStatus.Ready, context.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_marks_missing_workspace_without_throwing()
    {
        string root = CreateTempDirectory();

        try
        {
            string missing = Path.Combine(root, "missing");

            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: missing,
                currentDirectory: root);

            Assert.Equal(Path.GetFullPath(missing), context.RootPath);
            Assert.Equal(WorkspaceStatus.Missing, context.Status);
            Assert.False(context.IsUsable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_marks_file_path_as_not_directory()
    {
        string root = CreateTempDirectory();

        try
        {
            string filePath = Path.Combine(root, "workspace.txt");
            File.WriteAllText(filePath, "not a directory");

            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: filePath,
                currentDirectory: root);

            Assert.Equal(WorkspaceStatus.NotDirectory, context.Status);
            Assert.False(context.IsUsable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [x] **Step 2: 运行测试，确认失败**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter WorkspaceContextTests
```

预期：

```text
失败原因：WorkspaceContext 和 WorkspaceStatus 尚未定义。
```

- [x] **Step 3: 添加工作区状态枚举**

创建 `src/CSharpAiCli.Core/WorkspaceStatus.cs`：

```csharp
namespace CSharpAiCli.Core;

public enum WorkspaceStatus
{
    Ready,
    Missing,
    NotDirectory
}
```

- [x] **Step 4: 添加工作区检测器**

创建 `src/CSharpAiCli.Core/WorkspaceContext.cs`：

```csharp
namespace CSharpAiCli.Core;

public sealed record WorkspaceContext(
    string RootPath,
    string ConfigPath,
    WorkspaceStatus Status)
{
    public bool IsUsable => Status == WorkspaceStatus.Ready;

    public static WorkspaceContext Detect(string? workspacePath = null, string? currentDirectory = null)
    {
        currentDirectory ??= Environment.CurrentDirectory;

        string requestedPath = string.IsNullOrWhiteSpace(workspacePath)
            ? currentDirectory
            : workspacePath;

        string rootPath = Path.GetFullPath(requestedPath, currentDirectory);
        WorkspaceStatus status = Directory.Exists(rootPath)
            ? WorkspaceStatus.Ready
            : File.Exists(rootPath)
                ? WorkspaceStatus.NotDirectory
                : WorkspaceStatus.Missing;

        return new WorkspaceContext(
            RootPath: rootPath,
            ConfigPath: Path.Combine(rootPath, ".caicli", "config.json"),
            Status: status);
    }
}
```

- [x] **Step 5: 运行工作区检测器测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter WorkspaceContextTests
```

预期：

```text
Passed!  - Failed: 0, Passed: 4
```

## 任务 2: 添加脱敏密钥包装器

**文件：**

- 创建: `src/CSharpAiCli.Core/SecretValue.cs`
- 创建: `src/CSharpAiCli.Tests/SecretValueTests.cs`

- [x] **Step 1: 编写失败的密钥测试**

创建 `src/CSharpAiCli.Tests/SecretValueTests.cs`：

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class SecretValueTests
{
    [Fact]
    public void From_returns_null_for_blank_values()
    {
        Assert.Null(SecretValue.From(null));
        Assert.Null(SecretValue.From(""));
        Assert.Null(SecretValue.From("   "));
    }

    [Fact]
    public void Value_keeps_secret_available_but_ToString_redacts_it()
    {
        SecretValue secret = SecretValue.From("sk-test-secret")!;

        Assert.Equal("sk-test-secret", secret.Value);
        Assert.Equal("[redacted]", secret.ToString());
        Assert.DoesNotContain("sk-test-secret", $"{secret}", StringComparison.Ordinal);
    }
}
```

- [x] **Step 2: 运行测试，确认失败**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter SecretValueTests
```

预期：

```text
失败原因：SecretValue 尚未定义。
```

- [x] **Step 3: 实现脱敏密钥包装器**

创建 `src/CSharpAiCli.Core/SecretValue.cs`：

```csharp
namespace CSharpAiCli.Core;

public sealed class SecretValue
{
    private SecretValue(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SecretValue? From(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new SecretValue(value);
    }

    public override string ToString()
    {
        return "[redacted]";
    }
}
```

- [x] **Step 4: 运行密钥测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter SecretValueTests
```

预期：

```text
Passed!  - Failed: 0, Passed: 2
```

## 任务 3: 添加配置加载器

**文件：**

- 创建: `src/CSharpAiCli.Core/CliConfigFile.cs`
- 创建: `src/CSharpAiCli.Core/EffectiveConfiguration.cs`
- 创建: `src/CSharpAiCli.Core/ConfigLoader.cs`
- 创建: `src/CSharpAiCli.Tests/ConfigLoaderTests.cs`

- [x] **Step 1: 编写失败的配置加载器测试**

创建 `src/CSharpAiCli.Tests/ConfigLoaderTests.cs`：

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_merges_user_workspace_and_environment_with_expected_priority()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");
            WriteConfig(Path.Combine(workspaceRoot, ".caicli", "config.json"), "gpt-workspace", "sk-workspace-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "sk-env-secret");

            Assert.Equal("gpt-workspace", configuration.Model);
            Assert.Equal("workspace config", configuration.ModelSource);
            Assert.True(configuration.HasApiKey);
            Assert.Equal("OPENAI_API_KEY", configuration.ApiKeySource);
            Assert.Equal("sk-env-secret", configuration.ApiKey!.Value);
            Assert.Contains(Path.Combine(userProfile, ".caicli", "config.json"), configuration.LoadedConfigPaths);
            Assert.Contains(Path.Combine(workspaceRoot, ".caicli", "config.json"), configuration.LoadedConfigPaths);
            Assert.DoesNotContain("sk-env-secret", configuration.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-workspace-secret", configuration.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-user-secret", configuration.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_workspace_api_key_when_environment_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");
            WriteConfig(Path.Combine(workspaceRoot, ".caicli", "config.json"), "gpt-workspace", "sk-workspace-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("sk-workspace-secret", configuration.ApiKey!.Value);
            Assert.Equal("workspace config", configuration.ApiKeySource);
            Assert.DoesNotContain("sk-workspace-secret", configuration.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_user_config_when_workspace_config_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("gpt-user", configuration.Model);
            Assert.Equal("user config", configuration.ModelSource);
            Assert.Equal("sk-user-secret", configuration.ApiKey!.Value);
            Assert.Equal("user config", configuration.ApiKeySource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_invalid_json_and_records_warning()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string workspaceConfigPath = Path.Combine(workspaceRoot, ".caicli", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(workspaceConfigPath)!);
            File.WriteAllText(workspaceConfigPath, "{ invalid json");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("not configured", configuration.Model);
            Assert.Equal("default", configuration.ModelSource);
            Assert.False(configuration.HasApiKey);
            Assert.Equal("missing", configuration.ApiKeySource);
            Assert.Contains(configuration.Warnings, warning => warning.Contains("ignored invalid config", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteConfig(string path, string model, string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $@"{{
  ""model"": ""{model}"",
  ""apiKey"": ""{apiKey}""
}}");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [x] **Step 2: 运行测试，确认失败**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter ConfigLoaderTests
```

预期：

```text
失败原因：ConfigLoader、EffectiveConfiguration 和 CliConfigFile 尚未定义。
```

- [x] **Step 3: 添加配置文件模型**

创建 `src/CSharpAiCli.Core/CliConfigFile.cs`：

```csharp
namespace CSharpAiCli.Core;

public sealed class CliConfigFile
{
    public string? Model { get; init; }

    public string? ApiKey { get; init; }
}
```

- [x] **Step 4: 添加生效配置模型**

创建 `src/CSharpAiCli.Core/EffectiveConfiguration.cs`：

```csharp
namespace CSharpAiCli.Core;

public sealed record EffectiveConfiguration(
    string WorkspaceRoot,
    string UserConfigPath,
    string WorkspaceConfigPath,
    string Model,
    string ModelSource,
    SecretValue? ApiKey,
    string ApiKeySource,
    IReadOnlyList<string> LoadedConfigPaths,
    IReadOnlyList<string> Warnings)
{
    public bool HasApiKey => ApiKey is not null;
}
```

- [x] **Step 5: 添加配置加载器**

创建 `src/CSharpAiCli.Core/ConfigLoader.cs`：

```csharp
using System.Text.Json;

namespace CSharpAiCli.Core;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static EffectiveConfiguration Load(
        WorkspaceContext workspace,
        string? userProfile = null,
        string? openAiApiKey = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        openAiApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        string userConfigPath = Path.Combine(userProfile, ".caicli", "config.json");
        List<string> loadedConfigPaths = [];
        List<string> warnings = [];

        CliConfigFile userConfig = ReadConfig(userConfigPath, loadedConfigPaths, warnings);
        CliConfigFile workspaceConfig = workspace.IsUsable
            ? ReadConfig(workspace.ConfigPath, loadedConfigPaths, warnings)
            : new CliConfigFile();

        string model = FirstNonWhiteSpace(workspaceConfig.Model, userConfig.Model) ?? "not configured";
        string modelSource = SourceFor(
            workspaceConfig.Model,
            "workspace config",
            userConfig.Model,
            "user config",
            fallback: "default");

        string? apiKey = FirstNonWhiteSpace(openAiApiKey, workspaceConfig.ApiKey, userConfig.ApiKey);
        string apiKeySource = SourceFor(
            openAiApiKey,
            "OPENAI_API_KEY",
            workspaceConfig.ApiKey,
            "workspace config",
            userConfig.ApiKey,
            "user config",
            fallback: "missing");

        return new EffectiveConfiguration(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: workspace.ConfigPath,
            Model: model,
            ModelSource: modelSource,
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: loadedConfigPaths,
            Warnings: warnings);
    }

    private static CliConfigFile ReadConfig(string path, List<string> loadedConfigPaths, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            return new CliConfigFile();
        }

        try
        {
            string json = File.ReadAllText(path);
            CliConfigFile? config = JsonSerializer.Deserialize<CliConfigFile>(json, JsonOptions);

            if (config is null)
            {
                warnings.Add($"ignored empty config: {path}");
                return new CliConfigFile();
            }

            loadedConfigPaths.Add(path);
            return config;
        }
        catch (JsonException)
        {
            warnings.Add($"ignored invalid config: {path}");
            return new CliConfigFile();
        }
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string SourceFor(
        string? firstValue,
        string firstSource,
        string? secondValue,
        string secondSource,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(firstValue))
        {
            return firstSource;
        }

        if (!string.IsNullOrWhiteSpace(secondValue))
        {
            return secondSource;
        }

        return fallback;
    }

    private static string SourceFor(
        string? firstValue,
        string firstSource,
        string? secondValue,
        string secondSource,
        string? thirdValue,
        string thirdSource,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(firstValue))
        {
            return firstSource;
        }

        if (!string.IsNullOrWhiteSpace(secondValue))
        {
            return secondSource;
        }

        if (!string.IsNullOrWhiteSpace(thirdValue))
        {
            return thirdSource;
        }

        return fallback;
    }
}
```

- [x] **Step 6: 运行配置加载器测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter ConfigLoaderTests
```

预期：

```text
Passed!  - Failed: 0, Passed: 4
```

## 任务 4: 用工作区和配置组合运行时快照

**文件：**

- 修改: `src/CSharpAiCli.Core/CliEnvironmentSnapshot.cs`
- 修改: `src/CSharpAiCli.Tests/CliEnvironmentSnapshotTests.cs`

- [x] **Step 1: 替换快照测试**

替换 `src/CSharpAiCli.Tests/CliEnvironmentSnapshotTests.cs`：

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliEnvironmentSnapshotTests
{
    [Fact]
    public void Create_builds_workspace_paths_config_paths_and_runtime_status()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.GetFullPath(workspaceRoot), snapshot.CurrentDirectory);
            Assert.Equal(Path.Combine(userProfile, ".caicli", "config.json"), snapshot.UserConfigPath);
            Assert.Equal(Path.Combine(workspaceRoot, ".caicli", "config.json"), snapshot.WorkspaceConfigPath);
            Assert.Equal("9.0.308", snapshot.DotnetSdkVersion);
            Assert.Equal(".NET 9.0.0", snapshot.DotnetRuntime);
            Assert.Equal("net9.0", snapshot.TargetFramework);
            Assert.False(snapshot.HasGlobalJson);
            Assert.False(snapshot.HasOpenAiApiKey);
            Assert.Equal(WorkspaceStatus.Ready, snapshot.WorkspaceStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_the_value_in_ToString()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "sk-test-secret",
                hasGlobalJson: true);

            Assert.True(snapshot.HasOpenAiApiKey);
            Assert.True(snapshot.HasGlobalJson);
            Assert.Equal("OPENAI_API_KEY", snapshot.Configuration.ApiKeySource);
            Assert.DoesNotContain("sk-test-secret", snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_honors_relative_workspace_override()
    {
        string root = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(root, "workspace");
            string userProfile = Path.Combine(root, "home");
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(userProfile);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: "workspace",
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.GetFullPath(workspaceRoot), snapshot.CurrentDirectory);
            Assert.Equal(WorkspaceStatus.Ready, snapshot.WorkspaceStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [x] **Step 2: 运行测试，确认失败**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter CliEnvironmentSnapshotTests
```

预期：

```text
失败原因：CliEnvironmentSnapshot 尚未暴露 Workspace、Configuration 或 workspacePath 支持。
```

- [x] **Step 3: 替换运行时快照实现**

替换 `src/CSharpAiCli.Core/CliEnvironmentSnapshot.cs`：

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSharpAiCli.Core;

public sealed record CliEnvironmentSnapshot(
    WorkspaceContext Workspace,
    EffectiveConfiguration Configuration,
    string DotnetSdkVersion,
    string DotnetRuntime,
    string TargetFramework,
    bool HasGlobalJson)
{
    public string CurrentDirectory => Workspace.RootPath;

    public string UserConfigPath => Configuration.UserConfigPath;

    public string WorkspaceConfigPath => Configuration.WorkspaceConfigPath;

    public bool HasOpenAiApiKey => Configuration.HasApiKey;

    public WorkspaceStatus WorkspaceStatus => Workspace.Status;

    public static CliEnvironmentSnapshot Create(
        string? workspacePath = null,
        string? currentDirectory = null,
        string? userProfile = null,
        string? dotnetSdkVersion = null,
        string? dotnetRuntime = null,
        string? openAiApiKey = null,
        bool? hasGlobalJson = null)
    {
        currentDirectory ??= Environment.CurrentDirectory;

        WorkspaceContext workspace = WorkspaceContext.Detect(
            workspacePath: workspacePath,
            currentDirectory: currentDirectory);

        EffectiveConfiguration configuration = ConfigLoader.Load(
            workspace,
            userProfile: userProfile,
            openAiApiKey: openAiApiKey);

        dotnetSdkVersion ??= ReadDotnetSdkVersion();
        dotnetRuntime ??= RuntimeInformation.FrameworkDescription;
        hasGlobalJson ??= File.Exists(Path.Combine(workspace.RootPath, "global.json"));

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: dotnetSdkVersion,
            DotnetRuntime: dotnetRuntime,
            TargetFramework: ProductInfo.TargetFramework,
            HasGlobalJson: hasGlobalJson.Value);
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

- [x] **Step 4: 运行快照测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter CliEnvironmentSnapshotTests
```

预期：

```text
Passed!  - Failed: 0, Passed: 3
```

## 任务 5: 更新 Doctor 和 Config 报告

**文件：**

- 修改: `src/CSharpAiCli.Core/DoctorReport.cs`
- 修改: `src/CSharpAiCli.Core/ConfigReport.cs`
- 修改: `src/CSharpAiCli.Tests/DoctorReportTests.cs`
- 修改: `src/CSharpAiCli.Tests/ConfigReportTests.cs`

- [x] **Step 1: 替换 doctor 报告测试**

替换 `src/CSharpAiCli.Tests/DoctorReportTests.cs`：

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class DoctorReportTests
{
    [Fact]
    public void Create_formats_runtime_workspace_config_and_key_status()
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
        Assert.Contains("api key: missing", text);
    }

    [Fact]
    public void Create_never_prints_api_key_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: "sk-test-secret", apiKeySource: "OPENAI_API_KEY");

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("api key: present (OPENAI_API_KEY)", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_prints_config_warnings()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            warnings: ["ignored invalid config: workspace-root/.caicli/config.json"]);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("config warning: ignored invalid config", text);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
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
            HasGlobalJson: false);
    }
}
```

- [x] **Step 2: 替换 config 报告测试**

替换 `src/CSharpAiCli.Tests/ConfigReportTests.cs`：

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigReportTests
{
    [Fact]
    public void Create_formats_effective_configuration_with_sources()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: null, apiKeySource: "missing");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("userConfigPath: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspaceConfigPath: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("model: gpt-workspace", text);
        Assert.Contains("modelSource: workspace config", text);
        Assert.Contains("apiKey: missing", text);
        Assert.Contains("apiKeySource: missing", text);
        Assert.Contains("loadedConfigPaths: none", text);
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: "sk-test-secret", apiKeySource: "OPENAI_API_KEY");

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
            Model: "gpt-workspace",
            ModelSource: "workspace config",
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

- [x] **Step 3: 运行报告测试，确认失败**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter "DoctorReportTests|ConfigReportTests"
```

预期：

```text
失败原因：报告尚未使用 WorkspaceContext 或 EffectiveConfiguration。
```

- [x] **Step 4: 替换 doctor 报告实现**

替换 `src/CSharpAiCli.Core/DoctorReport.cs`：

```csharp
namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = snapshot.HasOpenAiApiKey
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

- [x] **Step 5: 替换 config 报告实现**

替换 `src/CSharpAiCli.Core/ConfigReport.cs`：

```csharp
namespace CSharpAiCli.Core;

public sealed record ConfigReport(IReadOnlyList<string> Lines)
{
    public static ConfigReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string apiKeyStatus = configuration.HasApiKey ? "present" : "missing";

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} effective configuration",
            $"workspace: {configuration.WorkspaceRoot}",
            $"workspaceStatus: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"userConfigPath: {configuration.UserConfigPath}",
            $"workspaceConfigPath: {configuration.WorkspaceConfigPath}",
            $"model: {configuration.Model}",
            $"modelSource: {configuration.ModelSource}",
            $"apiKey: {apiKeyStatus}",
            $"apiKeySource: {configuration.ApiKeySource}",
            $"loadedConfigPaths: {FormatPaths(configuration.LoadedConfigPaths)}"
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

    private static string FormatPaths(IReadOnlyList<string> paths)
    {
        return paths.Count == 0 ? "none" : string.Join("; ", paths);
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

- [x] **Step 6: 运行报告测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter "DoctorReportTests|ConfigReportTests"
```

预期：

```text
Passed!  - Failed: 0, Passed: 6
```

## 任务 6: 添加递归 `--workspace` CLI 选项

**文件：**

- 修改: `src/CSharpAiCli.Cli/CliCommandFactory.cs`
- 修改: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [x] **Step 1: 替换命令工厂测试**

替换 `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`：

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

- [x] **Step 2: 运行命令工厂测试，确认失败**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

预期：

```text
失败原因：CliCommandFactory 仍接收 Func<CliEnvironmentSnapshot>，而不是 Func<string?, CliEnvironmentSnapshot>。
```

- [x] **Step 3: 替换命令工厂实现**

替换 `src/CSharpAiCli.Cli/CliCommandFactory.cs`：

```csharp
using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(output, workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        Option<string> workspaceOption = new("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true
        };

        rootCommand.Options.Add(workspaceOption);

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
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

- [x] **Step 4: 运行命令工厂测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

预期：

```text
Passed!  - Failed: 0, Passed: 4
```

## 任务 7: Smoke 验证与周回顾

**文件：**

- 验证: `src/CSharpAiCli.sln`
- 创建: `docs_md/weekly/03_week_review.md`

- [x] **Step 1: 构建 solution**

运行：

```powershell
dotnet build src/CSharpAiCli.sln
```

预期：

```text
Build succeeded.
```

- [x] **Step 2: 运行全部测试**

运行：

```powershell
dotnet test src/CSharpAiCli.sln
```

预期：

```text
Passed!  - Failed: 0
```

- [x] **Step 3: 验证 help 包含 workspace 选项**

运行：

```powershell
dotnet run --project src/CSharpAiCli.Cli -- --help
```

预期输出包含：

```text
doctor
config
--workspace
--help
```

- [x] **Step 4: 验证带工作区覆盖的 doctor**

运行：

```powershell
dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .
```

预期输出包含：

```text
C# AI CLI doctor
workspace:
workspace status: ready
user config:
workspace config:
api key:
```

- [x] **Step 5: 验证带工作区覆盖的 config get，且不泄露 API key**

运行：

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = "sk-week3-secret"
dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .
$env:OPENAI_API_KEY = $oldOpenAiKey
```

预期：

```text
输出包含 apiKey: present。
输出包含 apiKeySource: OPENAI_API_KEY。
输出不包含 sk-week3-secret。
命令以 0 退出。
```

- [x] **Step 6: 手动验证工作区配置优先级**

运行：

```powershell
$tempWorkspace = Join-Path ([System.IO.Path]::GetTempPath()) ("caicli-week3-" + [System.Guid]::NewGuid().ToString("N"))
$workspaceConfig = Join-Path $tempWorkspace ".caicli/config.json"
New-Item -ItemType Directory -Force -Path $tempWorkspace
New-Item -ItemType Directory -Force -Path (Split-Path $workspaceConfig)
Set-Content -LiteralPath $workspaceConfig -Value '{ "model": "gpt-week3-workspace", "apiKey": "sk-week3-file-secret" }'
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = ""
dotnet run --project src/CSharpAiCli.Cli -- config get --workspace $tempWorkspace
$env:OPENAI_API_KEY = $oldOpenAiKey
Remove-Item -LiteralPath $tempWorkspace -Recurse -Force
```

预期：

```text
输出包含 model: gpt-week3-workspace。
输出包含 modelSource: workspace config。
输出包含 apiKey: present。
输出包含 apiKeySource: workspace config。
输出不包含 sk-week3-file-secret。
命令以 0 退出。
```

- [x] **Step 7: 记录第 3 周回顾**

创建 `docs_md/weekly/03_week_review.md`：

```markdown
# 第 03 周回顾
状态：已稳固

已完成：
- 添加 `WorkspaceContext` 工作区检测器。
- 添加 `--workspace <path>` 覆盖支持。
- 添加用户配置和工作区配置加载。
- 添加配置优先级：`OPENAI_API_KEY` > 工作区配置 > 用户配置 > 默认值。
- 添加 `SecretValue` 脱敏包装，报告和对象字符串不打印原始密钥。
- 更新 `doctor` 和 `config get` 报告，显示工作区状态、配置来源和密钥来源。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- --help`
- 结果：输出包含 `doctor`、`config`、`--workspace` 和 `--help`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .`
- 结果：输出包含工作区路径、工作区状态、配置路径和 API key 状态
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .`
- 结果：输出有效配置、配置来源和密钥来源，并且不打印密钥值

运行时说明：
- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 仍为 `9.0.308`。
- 当前没有 `global.json` SDK 锁定。

风险：
- 配置 schema 仍是最小 schema，只覆盖 `model` 和 `apiKey`。
- API key 已可由配置加载器读取，但本阶段仍没有模型调用。
- 工作区检测只负责诊断和路径解析，尚未实现文件访问越界保护。

第 04 周输入：
- 稳定基础能力、日志、诊断和 Phase 01 文档。
- 补齐 `chat` 占位命令或明确推迟到 Phase 02。
- 记录 Phase 01 验收清单。
```

## 验收标准

第 3 周完成时必须满足：

- `WorkspaceContextTests` 通过。
- `SecretValueTests` 通过。
- `ConfigLoaderTests` 通过。
- 已更新的报告测试和命令测试通过。
- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- `dotnet run --project src/CSharpAiCli.Cli -- --help` 包含 `--workspace`。
- `dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .` 以 `0` 退出。
- `dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .` 以 `0` 退出。
- `doctor` 和 `config get` 永远不会打印来自 env、用户配置或工作区配置的原始 API key。
- `docs_md/weekly/03_week_review.md` 记录验证输出和第 4 周输入。
