## 第 1 周回顾

状态：Solidified

已完成：
- 创建 `src/CSharpAiCli.sln`
- 创建 CLI、Core、Tests 三个项目
- 创建 `docs_md/spec/`
- Core 第一个单元测试通过

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded，0 warnings，0 errors
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0，Passed: 1

风险：
- 当前本机只有 .NET SDK 9.0.308，且未检测到 `global.json`；阶段 01 后续需要决定是否升级到 .NET 10 LTS 或锁定其他 LTS SDK。

下周输入：
- 添加 System.CommandLine 根命令、`doctor`、`config get` 和帮助输出。
