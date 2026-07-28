# Week83 approval projection remediation review

## 结论

Week83 结论为 **Blocked**，不得写为 `Preview Ready`。

approval card 丢失/无限延迟的原始缺陷已经在确定性 regression 中稳定复现并完成最小 Renderer 修复；最终 exact clean product candidate、重建 package、完整 credential-free 矩阵以及 packaged provider read-only Gate 均通过。最终 packaged recovery 的功能语义也全部成立，但在相同两-turn 投影、对称两个 render-frame barrier 下，取消新 attempt 后仍观测到 `+193` 个 JS event listeners，超过冻结上限 `+40`。因此 W83-G5 失败，五轮 provider resource Gate 未启动，controlled write 的独立授权未请求且没有执行。

开放项：P0 `0`，P1 `1`。0.6.0 formal release 继续 Blocked。

## Revision 与 package 身份

- 分支：`codex/week83-approval-projection-remediation`
- Week82 review/harness closure revision：`54cb8053c3080b98b6f5cfdfdce6d16320e4d444`
- Week82 产品 baseline：`e9e062d985545377aa373767f563de6a2bb30a64`
- Week83 exact clean product candidate：`ccf9d82c9fa76c201876ee01d3849902989091e9`
- Desktop：`BFB856136D9A67E36B16B8F032B7776A4CE3D61326EDEBEC3862FBA47ECC23A5`，`222753280` bytes
- package tree：`B9C5055B08A4A2B2BAFE64914CA55DE567BF6AAB969E43298813D0859EF0C932`，`464709225` bytes，78 files
- `app.asar`：`BB87A78CEFAD90BD16A89D8BA1CB2A42CBA68DE174F8A273D5ECEB7B94176032`，`555529` bytes
- AppHost：`DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`，`79941168` bytes
- Renderer bundle：`index-Bk-7wMX0.js`，`E0905BBA05250FF2E51F00C32250992A32844B2ABAFF9C5714A7EDCCAB33BCAE`

`54cb805` 只表示 Week82 review/harness closure，不是产品候选；`e9e062d` 仍是 Week82 产品 baseline。`ccf9d82` 之后的 provider harness、evidence 与本文档提交也都不是产品候选。

## 确定性 regression 与最小修复

入口 regression 在 `e9e062d` 上构造了以下状态：ThreadStore 已提交 revision 4 的 `waiting-for-approval` 与 durable approval，Renderer 同时仍持有 revision 2 的 `running`，延迟旧响应返回后 approval card 为 `0`。baseline 按预期红灯。

真实 provider 通知顺序进一步暴露三个边界，并逐一形成先红后绿的 regression：

1. `created` 后 committed sequence 单调推进时，selected projection 必须调度 authoritative catchup；
2. catchup 必须保持有界，不能对每个通知启动 resync；
3. event sequence 前进但 revision 回退、committed sequence 不变的 duplicate 不得触发额外 resync。

最终产品差异只涉及 Renderer controller 与两份 regression test：拒绝低于最新 matching `thread.changed` revision 的 full-detail 响应；活动 runner 遇到 behind notification 时标记 dirty；selected projection 明确落后时执行有界 catchup；忽略相同 committed sequence 的 regressive revision duplicate。相对产品 baseline 的相关产品/test scope 为 3 个文件，`285 insertions / 5 deletions`。没有修改 provider、approval protocol、desktop-v1、UI 设计或 ThreadStore durable history。

最终定向结果为 2 files、`5/5`；包含 Week81 的受影响组合为 8 suites、`30/30`。

## Credential-free 全矩阵

| Gate | 结果 |
| --- | --- |
| full .NET | Passed，`1421/1421`，skipped `0` |
| Desktop verify | Passed，26 files，`116/116`；typecheck、lint、build、production security 通过 |
| dependency audit | Passed，vulnerabilities `0` |
| package audit | Passed，78 files、12 asar entries、forbidden payload `0` |
| unpacked E2E | Passed，`9/9` |
| packaged E2E / smoke | Passed，`8/8` |
| accessibility | Passed，`2/2` |
| protocol | Passed，3 轮，每轮 `48/48`，cleanup `0` |
| Week77 frozen profiles | Passed，`5/5`，workers `1`，retries `0`，全部 retention `<=15%` |
| Week80 controls | Passed，`C0-C7`，完整一次运行 |
| Week81 + Week83 regressions | Passed，8 suites，`30/30` |
| `git diff --check` | Passed |

Week77 private-bytes retention 为 `8.42% / 8.15% / 7.07% / 9.36% / 7.95%`；每轮 process/temp cleanup 为 `0`。Week80 C2 的 private bytes `18.29%` 仍是历史定义的非正式 diagnostic control，不重新定义为 15% Gate。

首次并发运行曾造成 Desktop 1 个 5 秒 timeout 与 .NET 2 个 5 秒 timeout；两个完整套件串行后分别 `116/116` 与 `1421/1421`。unpacked 首轮还保留了 owned MSBuild/conhost cleanup failure；验证并清理精确 owned PID 后，完整 9-test 集按原参数重跑为 `9/9`。没有只重跑失败 case 来拼接绿灯。

## Provider read-only

本对话独立授权允许读取 `.env.local` 中必要的 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY`，并运行 read-only、recovery 与五轮 resource Gates；配置值没有输出或持久化，只注入隔离 packaged child。controlled write 明确不在该授权内。

正式 read-only 结果为 Passed：

- provider turns `1`
- `workspace.read_text` `1`
- approval/write/shell `0 / 0 / 0`
- changed files `0`
- Renderer queue/runners `2 / 2`
- working set `-6.44%`，private bytes `-1.73%`，JS heap `+0.30%`
- process/temp/config cleanup `0 / 0 / 0`

## Provider recovery

最终 recovery 功能语义通过：

- durable approval 在 crash 前可见；
- 只终止 owned AppHost，没有自动重启或 replay；
- 显式 Restart 后旧 attempt 变为 `failed/interrupted`，approval 清除；
- 新 attempt 的 turn、approval、model、tool identity 均与旧 attempt 分离；
- 新 attempt 在 approval 前取消，workspace target 保持原值；
- timeline 连续且 item IDs 唯一；
- Renderer resync queue/runners 为 `3 / 3`，在冻结 `2..4` 范围内；
- DOM nodes delta `+3`，documents delta `0`；
- process/temp/config cleanup `0 / 0 / 0`。

阻断项是 JS event listeners delta `+193`，冻结上限为 `+40`。这个结果在相同两-turn new-approval → canceled 投影、baseline/terminal 两侧各两个 `requestAnimationFrame` 的对称 render barrier 后仍稳定存在。没有通过提高阈值、延长 idle、forced GC 或 Renderer reload 消除失败。

早期 recovery harness failures 全部保留，分别暴露并修正了 explicit-restart 断言顺序、等待旧 approval 的 observer race、coverage boundary、runner sourcemap target 以及非同态 DOM baseline；这些修正没有改变产品或 Gate 参数。最终 listener failure 不是被覆盖的 harness 首败，而是正式 `provider-recovery.json` 的 Failed 结果。

## 未运行阶段

- provider resource：NotRun。授权已授予，但 W83-G5 recovery 上游失败；不可拆分的五轮未启动，未重跑任何单 profile。
- controlled write：NotRun。非写入 Gates 未全部通过，因此没有请求本对话的独立 write 授权，也没有执行写入。

## Evidence preservation 与禁止项

Week82 两份 recovery evidence 未覆盖、未删除，最终 SHA-256 仍为：

- `provider-recovery-first-failure.json`：`467C0B67BF893DD7C980532169E9B63A91196E00DAE363F5BFDEC074F5AA39C8`
- `provider-recovery.json`：`EBE8765F0EF4D136F248CC293FA515C04A6CF86C7EADE7FF067E452F9020C0D0`

本轮没有修改 15% Gate、listener Gate、workers、retries、workload 或窗口；没有 forced GC、Renderer reload、extended idle、durable history 删除/缩短、provider 重构、协议升级或 UI redesign；没有 push、tag、上传、发布或部署。

## Week84 解除条件

1. 先建立 credential-free deterministic listener ownership regression，复现同一个 two-turn new-approval → canceled 投影的 `+193` listener retention；
2. 区分 product-owned lifecycle 与 observer boundary，保留 Week83 全部 recovery failure evidence；
3. 若为产品问题，只做最小 Renderer lifecycle cleanup，不改变 UI、provider、approval/desktop-v1 protocol 或 durable history；
4. 创建新的 exact clean product candidate 并重新构建 package；
5. 重跑完整 credential-free、provider read-only、完整 recovery 与不可拆分的五轮 resource Gate；
6. 只有全部非写入 Gates 通过后，才在对应对话单独请求 controlled-write 授权；
7. 所有 Gates 全绿之前继续写 `Blocked`。

详细机器证据位于 ignored `artifacts/week83-approval-projection-remediation/`，下一阶段交接为 `week84-handoff.json`。
