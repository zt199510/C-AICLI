# 第 69 周回顾：AppHost 与 Desktop Protocol v1

状态：Passed；实现、回归、package/process 与 clean-source release acceptance Gate 全部通过

更新时间：2026-07-16

## 完成范围

- `desktop-v1` reviewed contract 冻结 16 个 method、1 个 `thread.changed` notification、4 个 capability、21 个 JSON-RPC/session error、9 个 framing error、58 个 transport type 与全部 numeric limits。
- contract DSL 支持 bounded string/array、boolean、int32/int64、UTC datetime、named reference、nullable、optional 与 enum；拒绝 unknown、duplicate、unsafe identifier、missing reference、unbounded value 与 object cycle。
- generator 对 canonical contract 计算 SHA256 `0e89e542511ee9a1531db2bda55daa75ed4593257bdf5e9b80a99b065dcd33a4`，byte-stable 生成 strict C# / TypeScript DTO、constants、method metadata 与 runtime validators。
- `contract.schema.json` 关闭 DSL shape；`examples/methods.json` 覆盖所有 method params/result、notification 与 error，并由 generator check、TS guard 和 C# validator 三条路径验证；initialize example 的 version/hash 必须匹配 canonical contract。
- framing 严格处理 8 KiB header、1 MiB body、ASCII/CRLF、唯一 Content-Length、reviewed Content-Type、empty/partial/oversize、strict UTF-8、EOF 与 cancellation。
- Application 新增 opaque `DesktopApplicationSession`；内部持有 Core snapshot，public surface 只暴露 Application projection 与 scalar use case，AppHost 不引用 Core/store/CLI/ProjectPacks。
- AppHost 完成 exact initialize/version/hash/capability handshake、workspace replacement、strict envelope/id/params、13 个 workspace/business method typed dispatch 与 domain outcome mapping。
- runtime 使用单 reader、最多 8 个 business handler、single writer、identity-aware numeric in-flight registry、duplicate/busy/rate/cancel/timeout、64-frame/4-MiB output queue 与 response priority；饱和时仍允许 `app.cancel` control request。
- negotiated `thread.changed` 只在成功 create/rename/archive/delete 后发送；eventSequence 分配与 notification 入队位于同一临界区，response 先于 event，notification 不作为 durable truth。
- shutdown 的 handler/output drain 共享单一 2 秒 deadline；Git status/diff 消费 cancellation token，取消或超时时 kill owned process tree。
- 真实无 Electron AppHost process tests 覆盖 thread lifecycle、read-only dispatch、concurrent id/notification、`app.cancel`、fatal frame、stderr redaction、stdin disconnect 与 clean shutdown。
- Desktop 仅迁移既有 initialize/workspace/shutdown skeleton 到 schema/hash/outcome；没有新增 business IPC、bridge channel 或 Renderer API。

## Source 与环境

- Branch：`week-02-cli-commands-doctor-config`，按用户要求原地开发，未创建 worktree 或新分支。
- Week 69 起点：`991e5bed5102077932f162abd034655946c8985c`。
- 起点已有排期文档修改和未跟踪 Week 69 计划；实现保留并纳入本周文档。
- .NET SDK：`9.0.308`；Node/npm：`22.13.0` / `11.7.0`。
- Week 68 full suite：`1321/1321`；Week 69 当前：`1365/1365`，新增 44 个 .NET tests。

## Protocol contract

| 项目 | 冻结值 |
|---|---:|
| Protocol / schema | `desktop-v1` / `1` |
| Contract SHA256 | `0e89e542511ee9a1531db2bda55daa75ed4593257bdf5e9b80a99b065dcd33a4` |
| Methods / notifications | 16 / 1 |
| Types / RPC errors / frame errors / capabilities | 58 / 21 / 9 / 4 |
| Header / body / JSON depth | 8,192 / 1,048,576 bytes / 64 |
| Request id | `1..9,007,199,254,740,991` |
| Timeout | initialize 5s；query 15s；mutation 30s；shutdown drain 2s |
| Admission | 8 in-flight；burst 64；32 requests/s |
| Output | 64 frames 且 4 MiB aggregate |
| Application target / diagnostic | 768 KiB / 4 KiB x 100 |

状态机为 `created -> initialized -> workspace-ready -> shutting-down -> closed`。initialize 只成功一次；workspace.open 失败保留旧 session，成功替换前 cancel/drain 旧 request；shutdown cancel active request 后在同一 2 秒 deadline 内 drain handler 与 writer；EOF/disconnect cancel session 并禁止 late response。

## 自动化测试

- Release build：7 projects，`0 warnings / 0 errors`。
- Week 69 扩展定向 .NET：`59 passed / 0 failed / 0 skipped`。
- 标准并发 full suite 第 1 次：`1365 passed / 0 failed / 0 skipped`，约 88 秒。
- 标准并发 full suite 第 2 次：`1365 passed / 0 failed / 0 skipped`，约 89 秒。
- 实现提交前复验：`1365 passed / 0 failed / 0 skipped`，约 89 秒。
- Desktop verify：6 files / `24/24`，contract/examples/notice drift、typecheck、ESLint、Main/Renderer build 与 production security 全部通过；真实 C# thread payload 通过 generated TS guard。
- 无 Electron process E2E：6 tests，覆盖 happy/read-only/concurrent/cancel/fatal/disconnect/shutdown、ordered notification、stderr redaction 与 exit code。
- owned process cleanup test 使用受控 PowerShell parent/child PID，证明 cancellation 后整个 process tree 退出。
- deterministic runtime tests 使用 controlled TCS、manual monotonic clock 与 controlled deadline；没有 sleep-based cancel/backpressure test。
- dirty-source `Build-Release.ps1 -AllowDirtySource` 与 CLI smoke 通过；manifest 记录 `sourceDirty=true`，只作为中途 smoke 证据。

## Package 与 process

| Evidence | Week 68 | Week 69 | 变化 |
|---|---:|---:|---:|
| Package files | 77 | 77 | 0 |
| Unpacked package | 465,312,755 | 466,666,142 bytes | +0.29% |
| `app.asar` | 2,175,490 | 3,315,885 bytes | +52.42%（generated TS validators/metadata/source map 增长；仍不影响 native/runtime 阈值） |
| Desktop executable | 222,753,280 | 222,753,280 bytes | 0% |
| AppHost executable | 79,350,792 | 79,563,784 bytes | +0.27% |
| 3-second process count | 6 | 6 | 0 |
| 3-second working set | 404,094,976 | 406,433,792 bytes | +0.58% |
| 3-second private bytes | 239,906,816 | 241,147,904 bytes | +0.52% |
| Lifecycle sample | 7,832 | 7,244 ms | -7.51% |
| Headless/window-close orphan delta | 0 / 0 | 0 / 0 | unchanged |

`app.asar` 超过 15% 是 checked-in runtime validators 与 source map 的预期静态增长；总 package、AppHost executable、working set 与 private bytes 均远低于 15% 调查阈值。

## Architecture 结论

- AppHost csproj 只直接引用 Application；source scan 禁止 Core、ProjectPacks、CLI、ThreadStore、CliEnvironmentSnapshot 和 `.caicli/threads`。
- AppHost stdout write 只通过 `DesktopProtocolFraming.WriteFrameAsync`；Program stderr 只输出 stable code，不输出 raw payload/path/exception。
- protocol mapper 逐字段映射 Application projection；catalog source path、changes workspace/session path 与 Core record 不进入 response。
- CLI session path 无 ThreadStore/ThreadApplicationService dual-write。
- Desktop 非 generated source 不包含 thread/catalog/changes/report/artifact method 名或新增 business bridge surface。

## Clean acceptance

实现提交 `8e613a80630b1d2acb4554952da966e98d98b0a5` 后从 clean HEAD 执行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

- release manifest 与 checksums 均记录 `sourceRevision=8e613a80630b1d2acb4554952da966e98d98b0a5`、`sourceDirty=false`、`releaseAcceptance=true`、`sdkVersion=9.0.308`。
- CLI acceptance smoke 通过；真实 Gerber/TIFF、daemon/API 与 real-model smoke 仍按脚本约定由显式环境变量启用，本次未启用。
- acceptance 文档提交后，从最终 clean HEAD 再运行相同 release acceptance 与 CLI smoke，使最终 artifacts 绑定最终提交。

## Week 70 输入

- exact `desktop-v1` + contract SHA256 + generated C#/TS DTO/validators。
- 16 个 method、generated workspace/mutation/timeout metadata、typed Application outcome、stable errors/limits/capabilities 与 strict session state。
- bounded concurrent request、rate/cancel/timeout/backpressure、disconnect cleanup 与 single-writer semantics。
- ordered negotiated `thread.changed` notification 及 list/get revision resync 规则。
- AppHost process E2E、architecture、Desktop verify/package、双 smoke 与 performance baseline。
- Week 70 Main/Preload 只能消费 generated contract；不得重新定义 method/type/error/timeout/capability，不得把 raw transport 暴露给 Renderer。
