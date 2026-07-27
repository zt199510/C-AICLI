# Week 80 Review：Renderer Private-Bytes 诊断归因

状态：`Blocked`

日期：2026-07-25

基线 HEAD：`962d5dda4ae875299a96ba2c825bd13ec683240a`

最终受测产品 revision：`8e227a4ca050e9bdff5d25d61bf89725fed26104`

## 结论

Week 80 在独立授权的只读 provider 边界内重复了 Renderer private-bytes P1，但没有取得满足根因 Gate 的单一、安全产品修改边界。

- P1：private bytes `+8.48%`，Gate 内。
- P5：private bytes `+22.67%`，Gate 失败。
- P10：private bytes `+40.04%`，延续同方向增长。
- P5/P10 的 JS heap used 同步增长，分别为 `+43.37%`、`+50.84%`。
- 每个 provider turn 最终形成 6 个 timeline items，并触发恰好 8 个 `thread.changed`、8 个非重叠 resync runner、8 次完整 `listThreads`/`getThread(afterSequence=0)` 投影。
- 但是 credential-free C5/C7 在相同 10-turn 增量、66-item 终态和 80 次完整投影下仍在 15% Gate 内，且 JS heap used 下降。
- C6 使用与 P10 相同的 137 次 observer-style `getThread`，也没有复现 private bytes 或 live heap 增长。

因此，“减少 `queueResync`/完整投影次数”“修改 timeline DOM”“修改 timestamp 格式化”或“修改 observer/Gate”都不能作为 Week 81 的已证实修复。Week 80 不创建 `diagnosis-handoff.json`，不实施产品修复，不改变 Week 79 `Blocked`。

## 冻结身份与 Week 79 基线

- packaged Desktop：222,753,280 bytes，SHA-256 `6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29`
- packaged AppHost：79,941,168 bytes，SHA-256 `DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`
- Week 79 private bytes：
  - attempt 1：`+47.77%`
  - attempt 2：`+36.77%`
  - attempt 3：`+35.84%`
- Week 79 末次仍使用 1 秒 terminal poll、对称 30 秒 warm/post、5 秒采样、末 3 样本 median。
- Week 79 没有保存精确的 `page.evaluate` 和 observer bridge 调用次数；Week 80 将该项保留为历史 audit gap，没有回填猜测值。

## 授权边界

Phase 3 只从 `.env.local` 选择 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY`，且只注入本次 packaged Desktop 子进程及其 AppHost 后代。

全部 P1/P5/P10 合计：

- 19 个 provider turns；
- 19 个 `workspace.read_text(global.json)`；
- 0 个其他 tool；
- 0 approval、write、shell、Git、MCP、changed files、reports、artifacts；
- 0 未授权网络事件；
- 0 敏感披露；
- process/temp/config cleanup delta 全为 0；
- 配置值、prompt、模型正文、第三方正文和 rooted path 均未进入 evidence。

Playwright 的 trace/screenshot/video 在 provider project 中关闭。一次早期 harness failure 自动产生的 failure context 已在确认 owned path 后删除；最终 evidence 不包含该内容。

## Credential-free 控制矩阵

所有 profile 都使用 workers `1`、retries `0`、对称 30 秒窗口，不运行 forced GC，不以 reload 通过 Gate。

| Profile | Workload | WS | Private | JS heap used | Nodes | Resync / getThread |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| C0 | packaged zero-turn idle | -5.49% | -3.81% | -25.73% | -30 | n/a |
| C1 | 6 turns / 36 safe items，逐 turn 投影 | +4.08% | +9.93% | -27.08% | +911 | 6 / 6 |
| C2 | Week 77 frozen 240-item load | -6.75% | -8.75% | -44.24% | -3366 | 0 / 4 |
| C3 | 40 burst notifications，无 item 增长 | -4.82% | -4.35% | -23.43% | -22 | 40 / 2 |
| C4 | 一次增加 6 turns / 36 items | -51.06% | +1.23% | -33.70% | +911 | 1 / 1 |
| C5 | P10 等量：+10 turns、+60 items、80 完整投影 | +4.11% | +12.44% | -18.89% | +1533 | 80 / 80 |
| C6 | 137 次 observer-style 66-item 读取，不更新 UI | -6.92% | -2.54% | -34.08% | -24 | 0 / 137 |
| C7 | C5 等量，且每 item 使用不同 timestamp | +4.86% | +14.37% | -21.78% | +1533 | 80 / 80 |

C5 的 post-Gate disposable heap snapshot 只在 scenario-owned temp 中存在。分析后原 snapshot 已删除，evidence 仅保存脱敏聚合：

- 138,243 nodes；
- aggregate self size 9,195,318 bytes；
- String 2,778,682 bytes；
- Code 2,429,712 bytes；
- NativeOther 1,011,668 bytes；
- DOM 69,632 bytes；
- Promise 180 bytes。

该 snapshot 不参与 Gate，也未保存 raw strings。

## Provider 分级矩阵

| Profile | Warm-up + measured | Private | JS heap used | JS heap total | Nodes | Terminal polls | Measured notifications / full projections |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 1 + 1 | +8.48% | +19.14% | +24.75% | +155 | 22 | 8 / 8 |
| P5 | 1 + 5 | +22.67% | +43.37% | +59.40% | +504 | 68 | 40 / 40 |
| P10 | 1 + 10 | +40.04% | +50.84% | +103.94% | +970 | 137 | 80 / 80 |

P10 仅因 P5 private bytes 超过 15% 才运行；P10 没有替代 P5 Gate。

P1 的 numeric projection size：

- warm-up 后 6 items：4,361 UTF-8 bytes；
- measured turn 后 12 items：8,193 UTF-8 bytes；
- 12 items 的 summary 共 1,012 bytes，payload JSON 共 2,346 bytes；
- 12 个 distinct timestamps。

C5 的 66-item fixture projection 为 29,120 UTF-8 bytes。provider 每 item 较大，但总投影仍只有几十 KiB；该差异不足以单独说明 private bytes 增长。

## Source attribution

每个 provider turn 的 8 个通知可由源码路径完整解释：

1. `turn.start` 成功响应产生 1 个 mutation notification；
2. queued → running 转换产生 1 个 committed notification；
3. 5 个 runtime events 各自产生 1 个 committed notification；
4. terminal 转换产生 1 个 committed notification。

路径为：

- `src/CSharpAiCli.Application/TurnExecutionApplicationService.cs`
- `src/CSharpAiCli.AppHost/Protocol/DesktopRpcServer.cs`
- `src/CSharpAiCli.AppHost/Protocol/DesktopThreadNotificationSequencer.cs`
- `apps/desktop/src/renderer/use-desktop-controller.ts`

Renderer 的 `queueResync` 只在请求仍 in-flight 时 coalesce。provider durable events 间隔足够长，每个通知都启动自己的 runner；每个 runner 调用一次 `listThreads` 和一次选中线程的 `getThread(afterSequence=0)`。

该线性关系是已证实的放大路径，但不是已证实的 retention 根因：C5/C7 精确匹配 80 次完整投影，仍未复现 provider 的 live heap/private-bytes 方向。

### Coverage 纠偏

早期 source-map coverage 偏移落在 callback 声明和 async 恢复 range，曾把 controller render/async resume 次数误记为调用次数。该结果已作废，未进入最终归因或 Gate。

最终计数使用：

- `queueResync` 同步 callback body；
- 启动 async runner 前的同步赋值；
- 与 packaged renderer bundle SHA-256 完全一致的 product-revision source-map build。

最终每 turn 均为 8 requests、8 runners；不存在需要估算的 coalesced iteration。没有添加 retry，也没有通过重跑选择低内存结果。

## Phase 4 决策树结果

### A. JS heap used 与 private bytes 同步增长

P5/P10 满足该现象，但 credential-free C5 snapshot 没有提供同方向 heap retention。provider snapshot 会含 prompt、模型正文和路径，Week 80 没有把完整 provider snapshot 保存或带入 evidence。

### C. DOM/listener/document 增长

provider Nodes 随 turn 增长，但 C5/C7 的 Nodes 增长更大、private bytes 仍在 Gate 内。Documents 不单调增长；listener counter 有波动，但没有形成与 private bytes 一致的可重复上界。

### D. resync/full projection 线性放大

调用放大得到精确确认，但 C5/C7 是直接反例：相同投影次数和更高 DOM node 增量未复现 provider live heap retention。因此不能把 `queueResync` 直接改成 incremental fetch 并声称根因已修复。

### E. 非 Renderer 进程

Gate 使用 Tab/Renderer private bytes；Main/GPU/Utility/AppHost 的角色采样没有替代 Renderer 结果。Week 80 没有把其他进程增长误归为 Renderer。

## 未通过的根因 Gate

满足：

1. provider P5/P10 可重复；
2. 存在多个不增长的对照；
3. numeric counters 齐全；
4. 与 Week 79 private-bytes 方向一致。

未满足：

5. 无法定义由证据支持的最小产品修改边界；
6. 无法定义一个会先失败、且能证明该修改修复 retention 的 deterministic regression test。

因此 W80-G5 为 false，最终只能是 `Blocked`。

## 下一步所需证据

进入产品修复前，必须先取得以下之一：

- provider-safe 的 pre/post retained-object 差分，只输出对象类别/count/aggregate bytes，raw snapshot 仅存在于 owned temp 并在分析后删除；或
- AppHost → Main IPC → preload → renderer detail projection 边界的 bounded numeric instrumentation，能区分反序列化对象、React current tree 和已替换 projection 的存活数量/字节。

新证据必须继续使用 frozen package identity、P5 Gate、workers `1`、retries `0`、对称 30 秒窗口，并维持相同的只读授权边界。不得先实施 `queueResync`、incremental fetch、DOM 或 Gate 修正。

## 交付物

- ignored `artifacts/week80-renderer-private-bytes/`：10 个最终 envelope、8 个 credential-free raw profile、3 个 provider raw profile；
- `docs_md/weekly/80_week_renderer_private_bytes_evidence.schema.json`；
- `tools/Test-Week80RendererMemoryEvidence.ps1`；
- credential-free 与 provider Playwright diagnosis harness；
- production-default no-op 的 bounded numeric diagnostic switch。

没有 tag、上传、Release、`Preview Ready`、产品修复或 `diagnosis-handoff.json`。
