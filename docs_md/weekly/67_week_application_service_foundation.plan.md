# 第 67 周执行计划：Application Service Foundation

状态：已完成；Week 67 Gate Passed

更新时间：2026-07-16

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

基线提交：`9ba36c8a0bd5e7c02db762cebabb76568de71695`

## 本周目标

把 Week 66 的 workspace open spike 扩展为 CLI 与 Desktop 可共同依赖的只读 Application service 基础，并完成第一个真实 CLI/Application 等价迁移。

本周结束时，`CSharpAiCli.Application` 必须提供 workspace snapshot、catalog、changes、report 和 artifact metadata 的结构化查询；CLI 的 `changes` 命令必须实际调用 Application service，同时保持现有文本、JSON、exit code、安全结论和底层记录身份不变。

本周只建立共享 use case，不扩展 Desktop UI，不把新查询提前暴露为 AppHost protocol method。Week 68 继续 thread/timeline persistence，Week 69 再把稳定的 Application contract 接入 desktop-v1。

## 起点与差距

### 已有稳定输入

- Week 66 已创建 `CSharpAiCli.Application`，当前只有 `WorkspaceApplicationService.Open`。
- Application 当前只依赖 Core；AppHost 只依赖 Application；CLI 尚未引用 Application。
- Core/ProjectPacks 已有 workspace guard、Git status/diff、session、job、Skill、Automation、Project Pack 和 managed artifact 的既有实现。
- `desktop-v1` 已冻结 `Content-Length` framing、8 KiB header、1 MiB body、16 KiB diagnostics 和 contract drift check。
- .NET Release build 为 `0 warnings / 0 errors`，标准并发 full suite 基线为 `1269/1269`。
- Desktop tests 基线为 5 files / `11/11`，packaged smoke 的 AppHost orphan delta 为 0。

### 当前必须关闭的差距

- CLI 仍在 `CliCommandFactory` 内直接编排 `changes`、catalog、report 和 artifact 查询。
- Application 尚无统一错误分类、结果边界、脱敏和取消语义。
- `CSharpAiCli.Application` 尚未引用 ProjectPacks，不能复用 managed artifact store。
- catalog loader、report/store 和 artifact store 的返回模型尚未形成 UI-safe projection。
- 尚无 architecture test 阻止 Application/AppHost 反向引用 CLI 或文本 renderer。
- 尚无真实 CLI/Application parity test；Week 66 只证明了 workspace extraction path 可行。

## 范围冻结

### 本周必须交付

| 能力 | Application 输出 | 复用的既有权威 | 本周 CLI 处理 |
|---|---|---|---|
| Workspace | open + safe snapshot；workspace id、root、status、非秘密 capability 摘要 | `WorkspaceContext`、`WorkspaceGuard`、`CliEnvironmentSnapshot` | 不新增命令；为其他查询提供统一上下文 |
| Catalog | Skills、Experts、Automations、Project Packs 的稳定、排序、bounded projection | `SkillPackCatalog`、`ExpertProfileCatalog`、`AutomationCatalog`、`ProjectPackRegistry` | 保持现有 list 命令输出；本周不要求全部迁移 |
| Changes | clean/dirty、changed files、diff stat、truncated、warning、可选 task-report pointer | `GitStatusTool`、`GitDiffTool`、`IConversationStore`、`ChangesViewReport` 语义 | `changes` 必须迁移到 Application |
| Reports | 从既有 job/session 记录投影 task report 的 list/get metadata 与 structured summary | `JobRecordStore`、`IConversationStore`、既有 task report | 不创建新的 report store，不新增 CLI 命令 |
| Artifacts | managed artifact list/get metadata；owner、run、状态、size、hash、verification、retention | `ManagedArtifactStore` | 本周只读 list/get；CLI 写路径不改 |

### 本周明确不做

- 不实现 thread/turn/timeline schema、迁移或持久化；该范围属于 Week 68。
- 不新增 `catalog.get`、`changes.get`、`report.get` 或 `artifact.*` desktop-v1 method；该范围属于 Week 69。
- 不修改 Renderer、Preload allowlist 或 Desktop 页面，不用 UI 绕过 Application Gate。
- 不迁移 write-capable turn、approval、run、verify、export、prune、accept/reject、resume 或 restart。
- 不读取 artifact 二进制内容，不生成 preview，不把 preview 或文件存在声明为 correctness evidence。
- 不新建 Application 专用 job/report/artifact/config store，不复制 `.caicli` 目录布局或持久化 schema。
- 不一次性重写 `CliCommandFactory`；只迁移能以稳定 parity test 保护的 read-only slice。
- 不改变现有 CLI 参数、stdout/stderr 格式、JSON schema、exit code 或默认排序。

## Application Contract

### 1. 通用结果

建立最小公共 contract，避免各 service 自行发明错误与边界语义：

- `ApplicationResult<T>`：只包含成功数据、结构化错误、bounded diagnostics 和截断信息，不包含 CLI 文本。
- `ApplicationError`：至少包含稳定 `Code`、`Category`、`SafeMessage` 和 `Retryable`。
- 错误类别固定为 `validation`、`workspace`、`not-found`、`denied`、`conflict`、`corrupt-state`、`limit-exceeded`、`unavailable` 和 `internal`。
- 能复用 Core/ProjectPacks 稳定 error code 时必须原样保留；Application 不创建含义相同的第二套 code。
- 预期的领域失败返回结构化错误；意外编程错误交给进程顶层统一转为安全的 internal failure，禁止把 exception message、类型或 stack trace 放入 contract。
- 公共 contract 不暴露 `TextWriter`、CLI renderer、`ParseResult`、Electron 类型、store 实例或任意 JSON 字符串。

### 2. Boundaries

所有查询先定义边界再实现，默认值与最大值由一个 Application limits 类型集中维护：

| 项目 | 默认/上限 | 规则 |
|---|---:|---|
| list page size | 默认 50，最大 200 | 非法值返回 validation error，不静默放宽 |
| catalog items | 每类最大 200 | 稳定排序；读取第 `limit + 1` 项判断 truncated |
| changed files | 最大 500 | 不返回 full diff；只返回 existing bounded status/diff stat |
| report summary | 单项最大 256 KiB UTF-8 projection | 超限返回截断标记或 limit error，不读取任意外部路径 |
| artifact metadata | 每页最大 200 | 不读取 artifact content，不重新计算 hash |
| diagnostics | 最大 100 条，每条最大 4 KiB UTF-8 | 先脱敏再截断，保留稳定 error code |
| aggregate result | 目标不超过 768 KiB UTF-8 | 为 Week 69 的 1 MiB protocol body 留 envelope 余量 |

如果既有 loader 只能先无界读取再返回，本周应在原 Core/ProjectPacks 权威实现上增加可选 bounded 读取入口；不得在读完无界数据后只截断 Application DTO 并把它称为 bounded I/O。

### 3. Redaction

- Application 是进入 AppHost 前的最后一个业务边界，所有诊断、摘要、命令输出片段和 workspace-local manifest 字段在返回前复用 `DiagnosticSecretRedactor`。
- workspace root、受 guard 验证的相对路径和 managed artifact metadata 可以按产品需要返回，但不能附带文件内容、环境变量值、API key、approval grant 或未验证路径。
- catalog 只返回 allowlist 字段；不返回 automation/skill manifest 原始 JSON，也不把解析异常原文带出。
- redaction 后再计算字符串与 aggregate size，避免截断过程泄露秘密片段。
- 增加幂等性测试，保证 CLI renderer 对已脱敏 projection 再处理时不改变既有安全语义。

### 4. Cancellation

- 所有新 use case 接受 `CancellationToken`。
- 在入口、每个 store/tool I/O 前后和投影大集合时检查取消；取消后不得继续读取下一个 store 或生成部分成功结果。
- `OperationCanceledException` 只表示调用方 token 已取消，不作为普通领域错误；Week 69 由 AppHost 映射到 protocol cancellation。
- 现有同步 Git/store API 若不能中途取消，必须明确记录为“当前 I/O 单元结束后生效”，不能声称具备尚未实现的 process-level cancellation。
- CLI 本周继续以非取消 token 调用，保持既有行为。

## Use Case 设计

### Workspace snapshot

- 保留 `WorkspaceApplicationService.Open` 的 workspace identity 算法和 guard 结论。
- 增加 snapshot 查询，只投影 Desktop/CLI 需要的安全字段；配置只暴露 capability 和来源状态，不暴露 secret value。
- `workspaceId` 必须由 canonical root identity 得到；相对路径和绝对路径打开同一目录时 identity 相同。
- missing、not-directory、reparse/outside、path-too-long 和 inaccessible path 返回稳定分类。

### Catalog query

- 请求显式选择 `skills`、`experts`、`automations` 或 `project-packs`，不接受任意 catalog 名。
- 每种 item 使用明确 DTO allowlist；按稳定名称排序，保留 source kind、read-only/write-capable、tool boundary 和有效性 diagnostics。
- local Skill/Automation 继续由 workspace guard 与现有 validator 决定，Application 不信任 manifest 自报的安全结论。
- 重名、invalid manifest、oversize、source denied 和 partial catalog 以 bounded diagnostics 表达；一个坏 manifest 不应使全部有效 catalog 消失。
- 模型选项留到 Week 72 的 composer/configuration contract，不在本周读取或暴露 credential。

### Changes query 与首个 CLI parity slice

- 把 `changes` handler 中 Git status、Git diff stat、可选 session lookup 和 report composition 移到 `ChangesApplicationService`。
- Application 返回结构化 projection；CLI handler 只保留参数解析、verbose/trace/command log、text/JSON renderer 选择和 semantic status 到原 exit code 的映射。
- 不把 `CliCommandFactory`、`ParseResult`、`TextWriter`、`ChangesTextRenderer` 或 `ChangesJsonRenderer` 移入 Application。
- 保留 clean、dirty、non-git warning、hard Git failure、missing session、corrupt session、diff truncation 和 changed-file 排序语义。
- parity test 对同一 fixture 同时调用 Application 与真实 CLI handler，校验安全结论、workspace identity、changed files、task report pointer、完整 stdout 和 exit code。

### Report query

- 建立 `list/get`，来源限定为既有 job/session truth，不扫描任意用户路径。
- report identity 由 source kind + existing record/session identity 构成；不生成新的 report 文件或复制 task report。
- list 只返回 metadata；get 返回 allowlisted structured fields、现有 artifact/report pointer 和 bounded summary。
- missing、corrupt、unsupported schema 与引用缺失分别返回稳定错误/diagnostic，不能把 corrupt 当作 empty。
- 同一记录经 Application 查询与直接 store 读取时，job/session id、timestamp、status 和 artifact pointer 必须一致。

### Artifact query

- `CSharpAiCli.Application` 增加对 `CSharpAiCli.ProjectPacks` 的单向引用，直接复用 `ManagedArtifactStore`。
- 本周只实现 list/get metadata；filter 只接受现有 run id/status 语义。
- Application projection 保留 artifact id、owner、run id、relative path、media/type、size、SHA256、availability、verification 和 retention metadata。
- get 不打开 artifact content；list/get 不调用 verify、export、preview 或 prune。
- missing manifest、corrupt record、ownership denied、outside/reparse path 和 stale metadata 继续由既有 store/policy 作最终决定。

## CLI 迁移规则

`changes` 是本周唯一强制完成的真实 vertical slice。迁移顺序固定为：

1. 先为当前 CLI text/JSON/exit code 建立 golden fixture，冻结迁移前行为。
2. 新增 CLI -> Application 项目引用和可注入的 `ChangesApplicationService` factory。
3. 把业务编排移入 Application，CLI handler 不再直接实例化 Git tools 或读取 session store。
4. 继续复用现有 CLI renderer，保证输出兼容；Application contract 本身不返回 renderer 文本。
5. 对 clean、dirty、non-git、Git failure、session found/missing/corrupt 和 JSON 模式逐项做前后 parity。
6. parity 通过后才删除 handler 中已被替代的业务编排；不顺带迁移 write path。

若时间允许，可按相同方法迁移 `skills list`、`automation list`、`packs list`、`jobs list/show` 或 `artifacts list/show`，但不能以牺牲 `changes` parity、bounds、architecture tests 或 full regression 为代价。本周 Gate 不以迁移命令数量代替共享边界质量。

## Architecture Tests

新增自动化约束：

- Application 的项目引用只允许 Core、ProjectPacks 和明确审核的 framework/package；禁止 Cli、AppHost 或 Desktop。
- AppHost 禁止引用 Cli，且不得加载或调用 `CliCommandFactory`。
- CLI 允许单向引用 Application；Application public API 不得暴露 CLI、System.CommandLine、renderer 或 `TextWriter` 类型。
- Application 不得包含 Console 输出、CLI text/json renderer 或协议 framing。
- Application 不得新建 job/report/artifact store 文件或硬编码第二套持久化目录/schema。
- Desktop Main/Preload/Renderer 不直接读取 Core/ProjectPacks store；现有 secure bridge 约束继续通过。
- contract generation/drift test 保持通过；本周没有必要修改 `protocol/desktop-v1/contract.json`。

## 执行任务清单

- [x] Step 1：记录 Week 67 起点 commit、branch、clean state、SDK、Node/npm 和 Week 66 测试基线。
- [x] Step 2：冻结 `changes` 迁移前的 text/JSON/exit-code golden fixtures。
- [x] Step 3：定义 Application result、error category、limits、diagnostics、redaction 和 cancellation contract。
- [x] Step 4：完成 workspace open/snapshot contract 与路径、identity、安全配置投影测试。
- [x] Step 5：完成 Skills/Experts/Automations/Project Packs catalog query 与 bounded diagnostics。
- [x] Step 6：实现 Changes Application service，并迁移真实 CLI `changes` handler。
- [x] Step 7：建立 CLI/Application parity matrix，修复所有输出、exit code 或安全语义差异。
- [x] Step 8：完成 job/session-backed report list/get，不创建第二套 report truth。
- [x] Step 9：增加 Application -> ProjectPacks 引用，完成 managed artifact list/get metadata query。
- [x] Step 10：覆盖 missing、corrupt、oversize、denied、redaction、cancellation 和 truncated 结果。
- [x] Step 11：加入项目引用、public contract、renderer/store 边界 architecture tests。
- [x] Step 12：运行定向测试、Release build、标准并发 full suite 两次和 CLI smoke。
- [x] Step 13：运行 Desktop verify、AppHost publish、packaged headless/window-close smoke 与 orphan 检查。
- [x] Step 14：按 Week 66 同口径重采 package/process/memory，调查超过 15% 的增长。
- [x] Step 15：执行 `git diff --check`、文档链接检查，并创建 `67_week_review.md`。

## 建议日程

| 工作日 | 重点 | 当日退出条件 |
|---|---|---|
| Day 1 | baseline、golden fixtures、Application 公共 contract、architecture tests 骨架 | contract 边界评审完成；禁止依赖可自动失败 |
| Day 2 | workspace snapshot、catalog query | 四类 catalog 的成功/invalid/bounded/cancel tests 通过 |
| Day 3 | changes service、CLI handler 迁移 | text/JSON/exit-code parity 全部通过 |
| Day 4 | report 与 artifact list/get | store identity、corrupt-state、ownership tests 通过 |
| Day 5 | redaction/limits hardening、全量回归、package/process smoke、review | 所有 Week 67 Gate 形成可重复证据 |

任何 Day 3 parity 未通过时，Day 4 不扩大 CLI 迁移范围；优先修复共享 contract 或回退尚未完成的 handler 切换，不能保留双路径作为长期状态。

## 测试矩阵

| 范围 | 必测路径 |
|---|---|
| Workspace | absolute/relative identity、missing、not-directory、reparse/outside、inaccessible、cancel |
| Catalog | built-in/local、deterministic order、duplicate、invalid JSON、oversize、source denied、limit/truncated、secret redaction |
| Changes | clean、dirty、rename/untracked、non-git、Git failure、diff truncated、session found/missing/corrupt、cancel |
| Reports | empty、list/get、job/session identity、missing pointer、corrupt/unsupported schema、bounded summary、redaction |
| Artifacts | empty、filter、get、missing/corrupt manifest、ownership/outside/reparse、stale metadata、no content read |
| CLI parity | `changes` text、`--json`、`--output json`、`--session`、verbose interaction、exit 0/1、stderr policy |
| Architecture | allowed project references、public API dependency、no AppHost -> CLI、no Application renderer/store duplication |
| Regression | .NET full suite、CLI smoke、Desktop verify、AppHost publish、packaged close/cleanup |

## 验证命令

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build --filter "FullyQualifiedName~Application|FullyQualifiedName~Architecture|FullyQualifiedName~Changes"
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet test src\CSharpAiCli.sln -c Release --no-build

npm run verify
npm run package:apphost
$env:CAICLI_ELECTRON_ZIP_DIR = "$env:LOCALAPPDATA\electron\Cache"
node apps\desktop\scripts\package-desktop.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Measure-DesktopBaseline.ps1

git diff --check
git status --short --branch
```

定向 filter 名称以最终测试类为准；若 filter 未匹配任何测试，不能作为通过证据，必须记录实际测试数。

## 周末 Gate

Week 67 只有同时满足以下条件才能标记 Passed：

1. Application 提供 workspace、catalog、changes、report 和 artifact metadata 的结构化、bounded、可取消 contract。
2. CLI `changes` 已真实调用 Application service；handler 不再直接编排 Git tools 和 session store。
3. 同一 fixture 经 CLI 与 Application 得到相同 workspace/record identity、安全结论、changed files、task report pointer、stdout 和 exit code。
4. Application 只单向依赖 Core/ProjectPacks；AppHost/Desktop 没有反向引用 CLI 或解析 CLI 文本。
5. 没有复制 job/report/artifact store、workspace guard、redaction、ownership 或 policy。
6. corrupt、denied、oversize、truncated、secret 和 cancellation 路径均有自动化证据，且意外 exception 不进入 contract。
7. Release build 为 0 warning / 0 error；标准并发 full suite 连续两次通过；CLI 和 Desktop foundation 回归通过。
8. packaged headless/window-close smoke 均无 AppHost orphan；package、AppHost 或 working set 增长超过 Week 66 基线 15% 时已有原因和处置结论。
9. `67_week_review.md` 记录 source、命令、计数、耗时、parity、store identity、bounds、cleanup、风险和 Week 68 输入。

若第 2、3、4、5 或 6 项失败，Week 68 不得通过新增 thread store、Renderer mock data 或 AppHost 私有查询绕过 Gate。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| `CliCommandFactory` 体量大，迁移易改变输出 | 先冻结 golden fixtures；保持 renderer 不动；按单 handler 切换 |
| Core records 含 CLI 字段或暴露过多内部信息 | Application 使用 allowlisted projection，不把 store entity 直接作为 public contract |
| catalog loader 先无界扫描再截断 | 在原权威 loader 增加 bounded 入口；不复制 catalog |
| report 没有统一 store | 用 existing job/session pointer 做聚合查询，不创建新 report truth |
| artifact 查询误读内容或扩大 ownership | 本周只 list/get metadata，所有路径结论由 `ManagedArtifactStore` 决定 |
| 同步 Git/store 调用不能中途取消 | 明确 I/O 单元边界，先覆盖 pre/between-call cancel；不伪造即时取消声明 |
| CLI parity 与理想 DTO 冲突 | 以现有 0.5.0 CLI 兼容为 Gate，先调整 adapter，不修改用户可见契约 |

回退单位是单个 use case/CLI handler。任何迁移未通过 parity 时，允许在同一工作分支恢复该 handler 使用原路径并保留已验证的 Application contract/tests；禁止保留由 feature flag 随机选择的双业务实现。

## Week 68 输入

通过 Gate 后，Week 68 只接收以下稳定输入：

- 不依赖 CLI/Electron 的 Application result/error/limit/redaction/cancellation contract。
- 已被真实 CLI parity 证明的 workspace 与 changes use case。
- Skills/Experts/Automations/Project Packs catalog 的 UI-safe projection。
- existing job/session/report/artifact truth 的稳定 pointer 与只读查询。
- architecture tests、标准并发 full suite 和 Desktop process/package 回归基线。

Week 68 的 thread/turn/timeline 只能引用这些既有 record/artifact identity，不得复制其内容形成第二套任务事实源。
