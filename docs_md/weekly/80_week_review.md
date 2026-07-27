# Week 80 Review：Renderer Private-Bytes 根因诊断

状态：`Diagnosis Complete`

日期：2026-07-27

基线 HEAD：`962d5dda4ae875299a96ba2c825bd13ec683240a`

修复包 SHA-256：`E2A5BE23B0598ACA5827FC3977C48418298AE888D9293D1D51B937528BBB7A38`

## 结论

单一根因已经锁定：每个唯一的 `thread.changed` 都触发完整 authoritative projection 和 React commit。provider 每 turn 的 8 次通知因此重复创建状态控件并重投影持续增长的 timeline，形成 Blink Oilpan 页面和对象的高分配压力；自然 GC 的回收时序使 Renderer private bytes 在冻结的 15% Gate 上表现为高位且有波动的失败。

这不是 provider、bridge、observer 或普通 idle 自身造成的：当同样的 5 个 provider turns 和 40 个通知在 Main 中完成、但全部阻止进入 Renderer 时，private bytes 为 `-2.22%`、Nodes 为 `0`。零 measured-turn 的 provider idle 对照为 `+0.44%`、Nodes 为 `0`。

最小产品修复保留全部 40 个通知的可见性，只在每 turn 两个 committed-sequence 生命周期边界刷新 authoritative projection；同时稳定短生命周期控件的 DOM 身份，并把历史 timeline event cards 改为用户展开某个 turn 后才 materialize。

## 根因证据链

| 对照 | Private bytes | Nodes | 关键结果 |
| --- | ---: | ---: | --- |
| P5Q 基线，40/40 通知进入 Renderer | `+24.77%` | `+500` | 冻结 Gate 必败 |
| P5T 基线 + 非 Gate memory dump | `+21.62%` | `+502` | Blink GC pages `+8,978,432 B`；allocated objects `+3,091,680 B`；V8 仅 `+1,387,400 B`；malloc `-7,541,392 B` |
| P0T provider idle | `+0.44%` | `0` | 普通 post-warm 活动不足以致因 |
| P5DT provider/bridge 直达对照，40/40 通知丢弃于 Renderer 前 | `-2.22%` | `0` | provider、tool、bridge、trace 和 observer 均不足以致因 |

DOM mutation census 进一步定位了同一机制的分配来源：基线 5 turns 发生 `285` 个 added nodes 和 `285` 个 removed nodes；稳定结构后均降为 `0`。EventTarget census 同期没有实际 `addEventListener`/`removeEventListener` 调用，排除了重复订阅这一替代解释。

通知身份是逐条唯一的，但每 turn 的 committed sequence 呈现两个相邻重复边界。原实现每 turn 执行 8 次完整 projection；新策略仍 dispatch 8 次通知，只在两个生命周期边界执行 authoritative resync。纯函数回归将这一规则固定为 `8 → 2`。

## Deterministic regression

以下回归在修复前分别必败，修复后全部通过：

- 五 turn timeline 在用户打开某个 turn 前保持 bounded，打开后只 materialize 所选 turn。
- Composer queued-intent 节点跨状态变化保持身份。
- Composer 状态变化产生 `0` 次 child-list mutation。
- TaskControls 节点跨 active/inactive 状态保持身份。
- recovery banner 跨 recovery 状态保持身份。
- 一组完整 provider turn 生命周期通知只排队 2 次 authoritative resync，同时所有通知仍被 dispatch。

这些测试不读取 memory 数值，因此不会受 GC 或采样抖动影响；它们直接约束根因机制。

## 修复包 Gate

三次运行使用完全相同的 packaged Desktop，workers `1`、retries `0`、对称 30 秒 warm/post、末 3 样本 median；未运行 forced GC，未 reload Renderer，也未修改 15% Gate。

| Repeat | Working set | Private bytes | Nodes | 通知 | observer getThread |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | `+0.97%` | `+8.19%` | `+5` | `40/40` | `6` |
| 2 | `+0.94%` | `+8.61%` | `+5` | `40/40` | `6` |
| 3 | `+1.38%` | `+10.07%` | `+5` | `40/40` | `6` |

三次 private bytes 和 working set 均低于冻结的 15% Gate，且都完成 5/5 measured provider turns。

## 授权与安全边界

所有 provider 场景只从 `.env.local` 选择 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY`，且仅注入对应 packaged Desktop 子进程。模型工具仍只有 `workspace.read_text`；tool calls 与 provider turns 一一对应。未经授权的 tool、write、shell、Git tool、MCP、网络事件和敏感披露均为 `0`，process/temp/config cleanup delta 均为 `0`。

Evidence 只保存脱敏 numeric aggregate；配置值、prompt、模型正文、第三方正文、raw snapshot、raw trace 和 rooted path 均未持久化。

## 验证与交付

- `npm run verify`：通过；26 个测试文件、110 项测试，typecheck、lint、build 和 production security check 全部通过。
- `Test-Week80RendererMemoryEvidence.ps1`：验证 baseline 必败、独立对照、Blink allocator 差分、三次同包 Gate、授权边界、清理和脱敏。
- ignored evidence 目录生成 11 个最终 envelope，其中包含 `diagnosis.json` 和 `diagnosis-handoff.json`。
- 未 push、发布、tag 或部署。
