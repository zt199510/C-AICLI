# 第 23 周回顾

状态：已稳固

已完成：
- 添加统一版本元数据：`Directory.Build.props` 现在定义 `0.1.0` 版本、assembly/file/informational version 和产品信息。
- CLI 输出名固定为 `caicli`，并新增 `caicli version`，用于发布包和用户环境自检。
- 添加 `tools/Build-Release.ps1`，支持 `Release`、`win-x64`、self-contained、single-file publish，并生成 `release-manifest.json`。
- 添加发布脚本测试，覆盖 publish 参数、manifest、版本属性和不嵌入密钥。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，237 tests passed。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -NoZip`
- 结果：通过，生成 `artifacts/release/caicli-0.1.0-win-x64/caicli.exe` 和 `release-manifest.json`。
- 命令：`artifacts\release\caicli-0.1.0-win-x64\caicli.exe version`
- 结果：输出 `caicli 0.1.0`、`target framework: net9.0`、`release runtime: win-x64`。

运行时说明：
- 当前发布包为 Windows `win-x64` self-contained single-file direct 后端候选包。
- 发布脚本不会读取或嵌入 OpenAI API key；用户凭据仍来自环境变量或用户配置。

风险：
- 当前只实现 Windows self-contained zip/publish 路径；dotnet tool 包仍未作为首版硬门槛启用。
- Microsoft Agent Framework、真实 MCP 协议握手和 Gerber/TIFF 真实执行仍是增强目标，不纳入 Week 23 发布产物硬依赖。

第 24 周输入：
- 编写 installation、configuration、security model、quickstart 和能力状态文档。
- 用干净用户路径验证 `doctor`、`chat` 缺 key错误和发布包基础运行路径。
