# 第 72 周评审：Composer、Catalog 与受控上下文

状态：Passed

评审日期：2026-07-17

起点提交：`16ba2be32dc7b6c0a13674000cc32c1779d5fc5a`

执行分支：`week-02-cli-commands-doctor-config`

## 结论

Week 72 的 Critical Gate 已通过。Desktop 现在提供线程级多行 Composer、Skills/Experts/Automations Catalog、受控文件/目录上下文、effective model/approval 摘要，以及独立于 Turn 的 one-per-thread pending intent。发送只写入可恢复队列，不创建 Turn、timeline item，也不启动 agent/model/tool/shell；active turn 存在时明确排为 `next-turn`。

Renderer 仅持有 opaque selection id 和 workspace-relative display path。native picker 的绝对路径停留在 Main 局部变量中，并通过 Main-only `context.resolve` 交给 AppHost/Application 重新验证。Preload/Renderer 没有获得文件 bytes、绝对路径、通用 IPC、terminal、approval resolve 或 turn execution 能力。

## 已交付边界

- Core 新增 versioned composer intent record/store，具备 atomic enqueue/get/clear、queue revision、client mutation idempotency、one-pending 限制、corrupt-state fail closed 与重启恢复。
- Application 新增 controlled-context search/resolve/revalidation 和 Composer query/enqueue/clear；Catalog 返回 deterministic workspace-bound revision；archive 在 pending/corrupt queue 存在时稳定拒绝。
- `desktop-v1` 新增 `context.search`、Main-only `context.resolve`、`composer.get/enqueue/clear`，扩展 `catalog.list`，并协商 `composer.controlled-context` capability。
- Main/Preload bridge 冻结为 exact 21 invoke + 2 event；新增 7 个 reviewed channel，无 raw `context.resolve` 或 generic invoke。
- Renderer 新增多行 Composer、thread-scoped memory-only draft、context/catalog chips、`@` mention、file/folder picker、Enter/Shift+Enter/IME 行为、queued/next-turn 状态、authoritative `composer.get` 确认与 epoch guard。
- packaged fixture 的 queued intent 位于 fixture Main，Renderer reload 后可恢复；另有真实 packaged AppHost smoke 验证实际协议与持久化边界。

## 冻结的协议与限制

- 协商 contract SHA256：`1a66bec61e044dd7e684f32361053fc534ff78d9eb27b8bbfc66675ef8f7aa3a`
- schema/protocol version：`1` / `desktop-v1`
- prompt：64 KiB UTF-8；context selections：32；catalog selections：16。
- 单文件：10 MiB；全部 file context：32 MiB；单 folder：500 files / 64 MiB。
- context search：100 results / 5,000 scanned entries；query：256 UTF-8 bytes。
- relative display path：4,096 UTF-8 bytes；queue mutation id：128 UTF-8 bytes。
- pending intent：每 thread 1 个；enqueue/clear 均要求 expected revision，并使用 strict JSON contract。

## 自动化证据

| Gate | 结果 |
|---|---|
| .NET SDK | `9.0.308` |
| Release build | 0 warnings，0 errors |
| .NET full suite | 1,372 passed，0 failed，0 skipped |
| Composer/Context targeted suite | 6 passed，0 failed |
| Desktop contract/notices/typecheck/lint/build/security | passed |
| Vitest | 18 files，84 passed，0 failed |
| Playwright unpacked | 1 passed，0 skipped |
| Playwright packaged | 1 passed，0 skipped |
| npm audit | 0 vulnerabilities |
| CLI dirty-source release/smoke | passed；可选真实模型/Gerber smoke 按既有条件跳过 |
| packaged real AppHost composer smoke | passed；created Turns `0`，timeline items `0`，clear 后 queue revision `2` |
| startup/window-close/crash/capture smoke | 全部 exit 0；test-owned Desktop/AppHost orphan delta `0` |
| production package scan | fixture/E2E/test-results/playwright material matches `0` |

Core/Application 的 negative assertions 覆盖 workspace containment、private roots、binary、oversize、stale identity、catalog revision、queue/thread revision、duplicate mutation、one-pending、corrupt record 与 active-turn byte-stability。Main/Preload/Renderer tests 覆盖 exact bridge、sender guard、deep freeze、strict validation、memory-only keyed draft、错误保留、authoritative clear、keyboard、IME、accessible picker 与 next-turn 状态。

## 真实 AppHost Composer smoke

packaged AppHost 完成以下真实链路：initialize -> workspace.open -> thread.create -> context.resolve -> catalog.list -> composer.enqueue -> thread.get -> composer.get -> composer.clear -> shutdown。

- AppHost SHA256：`79403D7B7F7A40DBB3CE507691081FE9F290FF21D1F8176C9CFFAF2AAE6AAC98`
- enqueue created Turns：`0`
- enqueue created timeline items：`0`
- queue revision after clear：`2`
- orphan AppHost delta：`0`
- absolute workspace/native path 未出现在 Renderer-facing response。

## 视觉与可访问性证据

以下 packaged captures 已人工检查，Composer 可达，中央 timeline、sidebar、Inspector 与 Composer 无重叠、横向溢出或空白主视图：

- `artifacts/week72-composer/shell-1920x1080.png`
- `artifacts/week72-composer/shell-1440x900.png`
- `artifacts/week72-composer/shell-1280x720.png`
- `artifacts/week72-composer/shell-760x560.png`

自动化同时覆盖 textarea、attach buttons、mention listbox/options、chip remove、Send、Clear queue 的 stable accessible names，以及 IME composition、Enter、Shift+Enter 和 live queued status。

## Package 与 process baseline

| 指标 | Week 71 | Week 72 | 变化 | Gate |
|---|---:|---:|---:|---|
| package bytes | 463,767,212 | 463,940,984 | +0.04% | passed |
| `app.asar` bytes | 416,955 | 476,039 | +14.17% | passed |
| packaged process count | 6 | 6 | 0% | passed |
| working set bytes | 408,416,256 | 425,906,176 | +4.28% | passed |
| private bytes | 241,643,520 | 250,990,592 | +3.87% | passed |

所有同口径增长均低于 15%。package 共 77 files；测试后 Desktop/AppHost orphan delta 均为 0。

## 首次失败与恢复记录

1. 首次 full .NET run 在既有 Gerber/TIFF process-cleanup 竞态中出现 1 项失败（1,371 passed / 1 failed）；隔离复跑 4/4 通过，随后两次完整套件均为 1,372/1,372，判定为瞬时清理竞态而非 Week 72 回归。
2. 首次 `npm ci` 在 Electron binary 下载时遇到 `ECONNRESET`。随后以 `ELECTRON_SKIP_BINARY_DOWNLOAD=1` 完成 lockfile clean install（296 packages，0 vulnerabilities），再从既有校验缓存恢复 Electron `41.1.0` binary。
3. packager 首次尝试网络获取 Electron 时等待超时；终止该次 test-owned 进程后，使用仓库既有 Electron zip cache 完成 deterministic package。最终 packaged/unpacked E2E、全部 smoke 与 orphan check 均通过。

这些恢复没有放宽 validator、CSP、security scan、packaged E2E 或 clean-source Gate。

## Week 73 稳定输入

- reviewed `desktop-v1` composer/context/catalog contract、generated validators、exact capability/hash 与 21+2 bridge。
- Core/AppHost one-per-thread pending intent、workspace/root identity、queue revision、mutation idempotency 与 restart recovery。
- enqueue-time canonical prompt、context/catalog references 和 `ready`/`next-turn` delivery 语义。
- memory-only draft、`@` mention/native picker、错误保留、epoch race guard 与 authoritative `composer.get` resync。
- active Turn 与 pending intent 的严格分离；Week 73 必须原子消费 intent 后才创建 Turn/user message。
- Week 73 消费时仍须重新验证 workspace、context、catalog 与 approval policy，不得把 Week 72 的 selection token、旧 identity 或 revision 当作执行授权。

## 最终 clean-source acceptance

结果：Passed。

- release manifest：`sourceDirty=false`、`releaseAcceptance=true`、SDK `9.0.308`。
- CLI clean-source release 与 smoke：passed。
- Desktop verify、同一 clean HEAD 的 AppHost publish 与 cached Electron package：passed。
- unpacked/packaged E2E、四视口 capture、production inventory scan 与真实 AppHost Composer smoke：passed。
- test-owned Desktop/AppHost orphan delta：`0`。

最终 source revision 由生成的 `artifacts/release/caicli-0.5.0-win-x64/release-manifest.json` 绑定；不把提交 hash 写回源码文档，以避免提交身份自引用。文档结果写入后对最终提交重新执行同一 clean-source acceptance。
