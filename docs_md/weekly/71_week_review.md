# 第 71 周执行回顾：只读 Thread、Timeline 与复核面板

状态：Passed

日期：2026-07-17

起点提交：`0f10eb9df95b89ae2f4e15bc2401a75f6fed26e7`

## 执行结论

Week 71 已在 Week 70 安全 Electron Shell 上完成可发布验证的只读复核闭环。Desktop 能从 Main/AppHost 当前 generation 的 canonical workspace snapshot 恢复 Renderer，列出并选择既有 thread，显式 create/rename/archive metadata，分页读取 frozen timeline，并按需查看 Changes、Reports、Artifacts 与 managed Gerber/TIFF/TIFF preview metadata。没有引入 turn start、composer、delete、generic IPC、文件打开、外部命令或第二套业务 truth。

`protocol/desktop-v1/contract.json`、schema、examples、generated C#/TypeScript 均未修改；协议仍为 `desktop-v1` / schema `1` / SHA256 `0e89e542511ee9a1531db2bda55daa75ed4593257bdf5e9b80a99b065dcd33a4`。

## 实际完成

- Main client 接收 strict response/notification union，initialize exact 协商第四项 `thread.changed` capability 与唯一 notification；unknown method/member、mixed id+method、missing/invalid params 均 fail closed。
- 每个 thread/review method 使用 generated params/result validator、generated timeout metadata 与 Main 注入的 schema/page size；runtime 只保存最近一次成功 workspace snapshot，并在 stop/restart/crash/protocol failure 清空。
- `thread.changed` 只作为 dirty hint。Renderer 通过 reducer、workspace/context/selection epoch、event sequence 去重、gap full resync 与 coalesced list/get query 收敛；notification 不直接 patch durable ThreadSummary。
- Bridge 冻结为 exact 14 invoke + 2 event：`runtime:get-status`、`runtime:restart`、`workspace:open`、`workspace:get-snapshot`、`thread:list`、`thread:get`、`thread:create`、`thread:rename`、`thread:archive`、`changes:get`、`report:list`、`report:get`、`artifact:list`、`artifact:get`，以及 `runtime:status`、`thread:changed`。
- ThreadSidebar 支持 bounded title、All/Active/Completed/Failed/Archived filter、选择既有 history、create、rename、archive confirmation 与 revision conflict 后 authoritative refresh；没有 delete/run/send/resume-turn surface。
- Timeline 展示 14 种 frozen item，使用 `afterSequence`/`nextSequence`、100-item page、sequence/id invariant 与 TanStack Virtual；2,000-item测试的 mounted row 不超过 80。
- Changes 只显示 status/stat/files/warnings；Reports 只显示 bounded structured fields；Artifacts/Preview 只显示 metadata/ownership/retention/availability/verification。relative path 始终为 text，Preview 明确声明 metadata/verification 不代表制造或图像 correctness。
- production scan 同时检查 bundle 的 Node/raw IPC/file URL/local persistence 与非 generated source 的 catalog/delete/cancel/turn/generic request；`e2e`、Playwright、test-results、source map 不进入 `app.asar`。

## 依赖与供应链

- 新增 runtime dependency：`@tanstack/react-virtual@3.14.6`。
- 新增 dev dependency：`@playwright/test@1.61.1`。
- lockfile 使用 exact version/integrity；`THIRD_PARTY_NOTICES.md` 已重建并通过 stale check。
- `npm audit`：0 vulnerabilities。
- Electron package 使用本机 official `electron-v41.1.0-win32-x64.zip` 离线缓存，并由 Electron Packager 校验 identity。

## 自动化证据

| Gate | 结果 |
|---|---|
| Desktop `npm run verify` | Passed；16 files / 78 tests，0 failed |
| notification/real AppHost integration | Passed；create -> renamed -> archived response-before-notification，conflict 无 notification |
| .NET Release build | Passed；SDK 9.0.308，0 warnings / 0 errors |
| .NET full suite | Passed；1365 passed / 0 failed / 0 skipped |
| CLI dirty-source release + smoke | Passed；`sourceDirty=true`，smoke passed |
| Playwright unpacked rich fixture | Passed；1/1，14-type history、Preview、reload |
| Playwright packaged real AppHost | Passed；1/1，runtime ready、reload |
| packaged startup/window-close/crash-restart | Passed；三种模式 exit 0，Desktop/AppHost orphan delta 0 |
| Week 71 combined read-only smoke | Passed；fixture inventory 0，unpacked/packaged passed，capture passed |

## Visual、package 与 process

截图：

- `artifacts/week71-read-only-review/shell-1920x1080.png`
- `artifacts/week71-read-only-review/shell-1440x900.png`
- `artifacts/week71-read-only-review/shell-1280x720.png`
- `artifacts/week71-read-only-review/shell-760x560.png`

最终 baseline：

| 指标 | Week 70 | Week 71 | 变化 |
|---|---:|---:|---:|
| package bytes | 464,731,162 | 463,767,212 | -0.21% |
| `app.asar` bytes | 1,380,905 | 416,955 | -69.81% |
| process count | 6 | 6 | 0% |
| working set bytes | 396,378,112 | 408,416,256 | +3.04% |
| private bytes | 230,088,704 | 241,643,520 | +5.02% |

最初的 `app.asar` sample 为 1,829,190 bytes，原因是新增 Renderer/source map。production package 随后明确排除 `.map`；build 目录仍保留调试 map，最终总包、asar 与内存均在 15% Gate 内。AppHost executable 为 79,563,784 bytes，SHA256 `EF3B14CC4F6E844A834887C2E464B888FAD03E68B78066561E397CDE0573E565`。

## 首次失败与修正

- npm/NuGet 初次在 restricted sandbox 因 socket `EACCES`/`NU1301` 失败；按权限流程在受控外部环境完成 exact dependency、NuGet signature/vulnerability 与 audit。
- production scan 初次仍把 Week 71 reviewed method 当 forbidden；改为 bundle capability scan + non-generated source authority scan，继续拒绝 Week 72-74/generic surface。
- unpacked Electron 初次在 restricted GPU/user-data 环境崩溃；GUI E2E 按产品权限要求在受控外部环境运行并通过。
- 首次 Renderer fixture 发现 workspace dispatch 后 list guard 读取旧 state；改为 request epoch 提交而不是依赖同步 React ref。
- short timeline 使用 virtualizer 时真实 Electron click 暴露不必要的动态 measure churn；<=100 item page 直接 bounded render，累计长 history 才挂载 virtualizer，2,000 item DOM Gate 保持通过。
- Electron package 初次未设置 `CAICLI_ELECTRON_ZIP_DIR` 而误走网络下载并被 reset；切换 official offline cache 后 29 秒完成。
- .NET Week 69 architecture scan 初次误命中测试 literal 与局部变量 `thread.archivedAtUtc`；没有放宽 Gate，改为 generated descriptor identity 与不产生 method 子串的局部命名。

## Deferred / Week 72 输入

- Deferred：turn start/resume execution、composer/draft/attachments、catalog/model/policy selector、streaming、approval resolve、delete、terminal、artifact open/export/delete、Gerber/TIFF bitmap preview 与 accept/reject。
- Week 72 只能依赖当前 exact reviewed bridge、strict validators、Main snapshot、notification resync、paginated/virtualized timeline 和 read-only panels；不得把 relative path/source pointer 转为 attachment 或 file authority。
