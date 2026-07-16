# 第 67 周回顾：Application Service Foundation

状态：已完成；Week 67 Gate Passed

更新时间：2026-07-16

## 完成范围

- 建立 `ApplicationResult<T>`、稳定 error category、bounded diagnostic、page/aggregate limits、redaction 与 cancellation contract。
- 扩展 workspace open 为安全 snapshot，返回稳定 workspace identity、capability 和不含 secret value 的配置来源投影。
- 建立 Skills、Experts、Automations、Project Packs 四类 catalog 查询，使用 allowlist DTO、稳定排序、bounded loader 与 partial diagnostics。
- 建立 `ChangesApplicationService`，把 Git status/diff、session lookup 和 report composition 从 CLI handler 移入 Application。
- CLI `changes` 已真实调用 Application；CLI 只保留参数校验、verbose/trace、text/JSON renderer 和 exit-code 映射。
- 建立 job/session-backed report list/get，以及 `ManagedArtifactStore`-backed artifact metadata list/get；没有新增 store、目录或 schema。
- Application 单向引用 Core/ProjectPacks，CLI 单向引用 Application；AppHost 仍只引用 Application，desktop-v1 contract 未修改。

## Source 与环境

- Branch：`week-02-cli-commands-doctor-config`，tracking `origin/week-02-cli-commands-doctor-config`。
- Week 67 起点：`9ba36c8a0bd5e7c02db762cebabb76568de71695`。
- 起点工作区已有排期更新和未跟踪 Week 67 计划；本周实现保留并继续这些改动。
- .NET SDK：`9.0.308`；Node/npm：`22.13.0` / `11.7.0`。
- Week 66 full suite 基线：`1269/1269`；Week 67 最终：`1288/1288`。
- 未创建提交，因此 `Build-Release.ps1 -AllowDirtySource` 产物仅用于本周验证；不是 clean-source acceptance artifact。

## Contract 与边界

| 范围 | 结果 |
|---|---|
| Result/error | 9 类稳定 category；错误只含 code/category/safe message/retryable |
| Page | 默认 50，最大 200；非法值返回 validation error |
| Catalog | 每类读取 `limit + 1` 判定 truncated；local loader 在权威 Core 实现中停止读取 |
| Changes | changed files 最大 500；Git 摘要、嵌套 task report 和 aggregate result 均有边界 |
| Reports | summary 最大 256 KiB UTF-8；list/get 只投影 job/session truth |
| Artifacts | 每页最大 200；只读 manifest metadata，不读取、verify、export 或 preview 内容 |
| Diagnostics | 最大 100 条、每条最大 4 KiB UTF-8；先脱敏后截断 |
| Cancellation | 入口、每个同步 I/O 单元前后和集合投影期间检查 caller token |

`DiagnosticSecretRedactor` 修复了已脱敏 `apiKey=[redacted]` 再次处理时多出 `]` 的非幂等行为。`changes` 对 session task report 做深度 allowlist/redaction，renderer 二次处理不再改变安全语义。

## Parity 与 store identity

- golden fixture 覆盖 `changes` text、JSON、dirty status、changed-file 顺序、warning 和 exit code。
- 真实 Git fixture 同时调用 Application 与 CLI，workspace root、status、dirty、changed files 和 exit code 一致。
- session found、missing、无 task report 与 corrupt/store failure 继续使用原有 CLI warning 语义和 session path identity。
- report identity 使用 `job:<jobId>` / `session:<sessionName>`；artifact identity、owner、run、hash、availability、verification 与 retention 直接来自既有 store record。
- artifact no-content-read 测试在内容文件被独占锁定时仍可完成 list/get metadata。

## .NET 验证

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build --filter "FullyQualifiedName~Application|FullyQualifiedName~Changes"
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

- Release build：7 projects，`0 warnings / 0 errors`。
- Application/Architecture/Changes 定向矩阵：`38/38`。
- 最终标准并发 full suite 第 1 次：`1288 passed / 0 failed / 0 skipped`，约 80 秒。
- 最终标准并发 full suite 第 2 次：`1288 passed / 0 failed / 0 skipped`，约 81 秒。
- CLI Release smoke：Passed；真实模型、daemon 与真实 Gerber/TIFF 外部工具路径按脚本环境开关跳过。

full suite 曾暴露既有 timeout observer 的 wall-clock 抖动。测试 cancellation observation 从 1 秒扩为 5 秒，已失败的 MCP wall-clock 断言容差扩为 8 秒；oversize-response 分类 fixture 的请求 timeout 从 2 秒扩为 10 秒，避免并发负载下先被错误归类为 timeout。产品默认、运行时上限和本周 Application timeout 均未修改。调整后 `OfflineAgentRunner` 定向 `32/32`，最终 full suite 连续两次通过。

## Desktop 与 package 验证

```powershell
cd apps\desktop
npm run verify
npm run package:apphost
$env:CAICLI_ELECTRON_ZIP_DIR = "$env:LOCALAPPDATA\electron\Cache"
node scripts\package-desktop.mjs
cd ..\..
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -WindowClose
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Measure-DesktopBaseline.ps1
```

- Contract/notice drift、typecheck、ESLint、production CSP 与 build 全部通过。
- Desktop tests：5 files / `11 passed`。
- Packaged headless 与 window-close 均 exit 0，AppHost orphan delta 均为 0。

| Evidence | Week 66 | Week 67 | 变化 |
|---|---:|---:|---:|
| Package files | 76 | 77 | +1 |
| Unpacked package | 437,851,431 | 465,075,187 bytes | +6.22% |
| `app.asar` | 2,175,389 | 2,175,490 bytes | +101 bytes |
| Desktop executable | 222,753,280 | 222,753,280 bytes | 0% |
| AppHost executable | 75,854,929 | 79,113,224 bytes | +4.30% |
| 3-second process count | 6 | 6 | 0 |
| 3-second working set | 406,470,656 | 406,507,520 bytes | +0.01% |
| 3-second private bytes | 242,315,264 | 241,971,200 bytes | -0.14% |
| Lifecycle sample | 8,017 | 8,097 ms | +1.00% |
| Orphan AppHost delta | 0 | 0 | 0 |

没有指标超过 15% 调查阈值。新增的 Application/ProjectPacks self-contained 依赖使 AppHost 和 unpacked package 合理增长。

## Gate 结论

| Gate | 状态 | 证据 |
|---|---|---|
| Workspace/catalog/changes/report/artifact contract | Passed | 五类结构化、bounded、可取消查询与自动化测试 |
| CLI/Application parity | Passed | 真实 CLI handler 使用 Application；text/JSON/exit/data identity 一致 |
| Store/policy ownership | Passed | 复用 Core/ProjectPacks 权威实现；无第二套 store/schema |
| Architecture | Passed | project reference、public API、handler source 自动化约束 |
| Redaction/limits/cancellation | Passed | 幂等、nested secret、missing/corrupt/truncated/cancel tests |
| .NET regression | Passed | 0 warning；最终 full suite 连续两次 `1288/1288` |
| CLI/Desktop regression | Passed | CLI smoke、Desktop verify、publish/package、双 smoke、orphan 0 |
| Performance | Passed | package/AppHost/working set 增长均低于 15% |

## 风险与 Week 68 输入

- 新 use case 仍是同步 I/O；cancellation 在当前 I/O 单元结束后生效，不声明 process-level 即时中断。
- catalog/report/artifact 只作为 Application API，未提前加入 desktop-v1 protocol 或 Desktop UI。
- clean-source Release acceptance 需要在本周改动提交后重新运行不带 `-AllowDirtySource` 的 release build；本次源码、测试和 package Gate 不受影响。
- Week 68 可稳定引用 workspace id、`job:` / `session:` report identity、managed artifact id 与 Application error/limit contract；不得复制 record 或 artifact 内容建立第二套事实源。
