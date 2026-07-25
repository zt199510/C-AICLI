# Week 80 执行计划：Renderer Private-Bytes P1 诊断归因

状态：Ready for Authorization

创建日期：2026-07-25

所属阶段：Week 79 `Blocked` 后的资源 P1 诊断

计划起点 HEAD：`962d5dda4ae875299a96ba2c825bd13ec683240a`

最终受测产品 revision：`8e227a4ca050e9bdff5d25d61bf89725fed26104`

建议分支：`codex/week80-renderer-memory-diagnosis`

## Goal

在不修改 15% Gate、不使用 forced GC/reload 掩盖 retention、不改变真实模型权限边界的前提下，定位 Week 79 provider-backed long-session Renderer private-bytes retention 的主要来源，并形成可由下一对话直接实施的单一修复假设。

必须区分：

1. live JavaScript heap 实际保留；
2. V8 heap reservation/committed capacity；
3. DOM node、document、listener 或 React tree 保留；
4. Thread detail/timeline/composer projection 保留；
5. `queueResync`、`listThreads`、`getThread` 重复全量拉取造成的 churn 或 stale request；
6. preload/IPC/provider UI transient object 生命周期；
7. Electron Renderer 之外的 Main/GPU/Utility/AppHost 增长。

Week 80 只允许以下最终结论：

- `Diagnosis Complete`：至少一个主要 retention 来源被重复、对照实验和对象/调用计数共同支持，修复边界足够明确。
- `Blocked`：证据只能说明 private bytes 超线，不能把增长归因到可安全修改的组件。

Week 80 不得写 `Preview Ready`，不得改变 Week 79 `Blocked`，不得创建 tag、上传制品或发布 Release。

## 冻结基线

- Week 79 最终资源 attempt：1 个 provider warm-up turn + 5 个测量 turns。
- warm/post-idle 两侧均为 30 秒，5 秒采样，20 秒 settle，末 3 个样本取 median。
- working set 使用 warm-window peak 对 post-idle settled median。
- private bytes 使用 warm/post 两侧 settled median。
- Week 79 最终结果：Renderer working set `+10.77%`，private bytes `+35.84%`。
- 第二轮 private bytes `+36.77%`，说明最终结果不是单次离群。
- 6 个 provider turns、6 个 read-only tool calls、36 个 timeline items、5 个进程角色、6 个 owned PID。
- approval/write/shell/changed files/reports/artifacts/未经授权网络/敏感披露均为 0。
- process/temp/config delta 均为 0。
- package SHA-256：`6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29`。
- AppHost SHA-256：`DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`。

若起点 HEAD、package 或 AppHost identity 不一致，先停止并说明差异；不得把不同产品 revision 的资源样本放进同一 retention 结论。

## 授权与权限边界

### Credential-free 阶段

以下动作无需真实模型配置：

- 阅读源码、ThreadStore/Renderer/IPC 契约和既有 evidence；
- 增加诊断 harness、测试插桩和脱敏计数；
- 运行 fake/fixture/control profile；
- 运行 .NET、Desktop、E2E、package/security 检查；
- 创建 disposable project 和临时 profile。

诊断插桩不得改变 production default 行为或 `desktop-v1` 契约。

### Provider-backed 阶段

新对话必须重新获得独立授权。以前对 Week79 Phase6–9 的授权不得自动复用。

允许的最小授权建议：

- 只读取 `.env.local` 中 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY`；
- 仅注入本次 packaged Desktop 子进程及其 AppHost 后代；
- disposable project；
- 只暴露 `workspace.read_text`；
- approval `never`；
- 禁止 write、shell、MCP、Git tool 和未经授权网络；
- 父进程、独立测试、evidence 和日志不得保存配置值。

## 明确不做

- 不修改 15% 阈值、idle 时长、workers 或 retries。
- 不以增加 warm-up 次数、延长 idle、Renderer reload、forced GC 或重跑到绿关闭 P1。
- 不先假设 `queueResync` 是根因后直接修改产品。
- 不保存 heap snapshot 中的 prompt、模型正文、本机绝对路径或配置值。
- 不在真实 provider 场景暴露 write/shell/MCP。
- 不在 Week 80 宣布修复完成或 Preview Ready。

## Evidence 目录

使用 ignored 根：

```text
artifacts/week80-renderer-private-bytes/
  manifest.json
  week79-baseline.json
  control-idle.json
  fixture-control.json
  provider-1-turn.json
  provider-5-turn.json
  provider-10-turn.json
  resync-counts.json
  heap-summary.json
  diagnosis.json
```

Evidence 只允许保存：

- source/package/AppHost identity；
- elapsed time、turn/tool/event/request 数量；
-按角色和阶段聚合的 working set/private bytes；
- JS heap used/total、DOM/document/listener 等数值计数；
- projection item/byte 上限与 resync 调用数；
- 脱敏的对象类别或 safe failure category；
- process/temp/config cleanup delta。

禁止保存 raw prompt/response、request header、配置值、完整 heap snapshot、绝对路径、完整 command environment 或第三方响应正文。

## Phase 0：基线与 harness 审计

1. 验证 HEAD、branch、dirty 和 package/AppHost identity。
2. 运行 Week79 evidence validator，确认 8 个 envelope 仍通过。
3. 阅读 Week79 三轮资源结果和 `79_week_review.md`，冻结失败数值。
4. 审计 Week79 harness 的 observer 行为：
   - terminal poll interval；
   - `page.evaluate` 次数；
   - Renderer bridge 调用次数；
   - resource sample 次数；
   - 是否在测量期间创建额外 DOM 或持久引用。
5. 建立新的 schema validator，禁止敏感字段和 rooted path。

Phase 0 Gate：基线身份一致；Week79 首败没有被覆盖；新 harness 的测量扰动可计数。

## Phase 1：增加只读诊断指标

优先使用 Chromium DevTools Protocol 或 Electron 官方 process metrics：

- `Performance.getMetrics`：
  - `JSHeapUsedSize`
  - `JSHeapTotalSize`
  - `Nodes`
  - `Documents`
  - `JSEventListeners`
- `Memory.getDOMCounters`，若当前 Electron 支持；
- `app.getAppMetrics()`：
  - Browser/Tab/GPU/Utility PID
  - working set/private bytes
- owned AppHost：
  - PID
  - working set/private bytes
- Renderer 应用级计数：
  - visible timeline cards
  - authoritative timeline items
  - turns
  - pending detail requests
  - resync requested/coalesced/completed
  - `listThreads`/`getThread` 调用次数
  - append/replace projection 次数
  - ignored stale response 次数。

若需要增加产品内计数，只能：

- 在 test/diagnostic build 中启用；
- 使用 bounded numeric counters；
- 不进入 production timeline、ThreadStore 或 report；
- 不暴露 prompt、payload、workspace root 或 provider data；
- 默认 production build 行为不变。

Phase 1 Gate：指标可以在 fake/control 场景稳定采集，关闭后 owned PID 与临时目录 delta 为 0。

## Phase 2：Credential-free 控制矩阵

至少运行以下独立新 profile：

| Profile | Workload | 用途 |
| --- | --- | --- |
| C0 | packaged Desktop ready，零 turn，warm/post 对称 idle | 机器与 Electron idle control |
| C1 | deterministic fixture，6 turns/36 timeline items | 排除 provider 网络和 AppHost agent |
| C2 | fixture 240 timeline items，使用 Week77 frozen workload | 校准既有 performance Gate |
| C3 | 仅重复 Thread projection/resync，不增加 turn | 检查 IPC/反序列化 churn |
| C4 | 仅增加等量安全 timeline projection，不调用 provider | 检查 React/reducer/DOM retention |

每个 profile：

- 新 profile、新 disposable workspace；
- warm/post 使用完全相同的 30 秒窗口；
- workers `1`、retries `0`；
- 不运行 forced GC；
- 记录 live heap、total heap、DOM/listener、resync 和 private bytes；
- cleanup delta 必须为 0。

Phase 2 Gate：至少能说明超线是否依赖 provider、turn growth、projection resync 或普通 Electron idle。

## Phase 3：Provider-backed 分级矩阵

仅在获得独立授权后执行：

1. P1：1 个 warm-up turn + 1 个测量 turn。
2. P5：1 个 warm-up turn + 5 个测量 turns，复现 Week79。
3. P10：1 个 warm-up turn + 10 个测量 turns；只有 P5 仍超线时才运行。

每个 turn：

- exactly one `workspace.read_text(global.json)`；
- 不调用其他 tool；
- terminal 必须 completed；
- approval/write/shell/changed files 必须为 0；
- timeline、Renderer、Changes、Reports 与磁盘一致。

每个 profile 记录：

- 首模型、首 tool、terminal 耗时；
- 每个 turn 后的 Renderer private bytes、JS heap used/total、DOM/listener；
- 每个 turn 的 runtime event、resync、full projection 调用数；
- post-idle settled median；
- cleanup delta。

P10 是诊断 profile，不得替代 P5 Gate，也不得用 10-turn 高基线计算一个更容易通过的 retention。

Phase 3 Gate：P5 能在新 harness 中复现，且敏感值/rooted path/未经授权网络计数为 0。

## Phase 4：归因决策树

### A. JS heap used 与 private bytes 同步增长

- 运行脱敏 disposable fixture heap snapshot；
- 只输出 top retained type、count 和 aggregate bytes；
- 检查 Thread detail、timeline item、composer draft、IPC response、React fiber、listener 和 closure dominator；
- 完整 snapshot 仅保存在 scenario-owned temp，分析结束后回收，不进入 evidence。

### B. JS heap used 稳定但 total heap/private bytes 增长

- 比较 provider P5、fixture C1 和 resync C3；
- 记录第二个等量 workload batch 是否继续增长；
- forced GC 只可作为诊断对照，结果不得用于 Gate；
- 只有多个正常运行 profile 证明 stable plateau，才可提出 measurement-methodology 修正；不得直接删掉 private-bytes Gate。

### C. DOM/listener/document 单调增长

- 按 turn 和 event 定位未释放节点或订阅；
- 验证组件 unmount、effect cleanup、terminal/review panel 生命周期；
- 给出可由 deterministic test 证明的计数上限。

### D. resync/full projection 与 event 数近似线性放大

- 计算每个 durable event 对 `listThreads`/`getThread` 的放大系数；
- 验证 `queueResync` 是否正确 coalesce；
- 检查完整 projection replace 是否可以改为 bounded incremental fetch；
- 保留 terminal/restart 时的 authoritative full resync 语义。

### E. 增长主要在 AppHost/Main/GPU/Utility

- 不把该结果错误归为 Renderer；
- 新建独立缺陷，保持 Renderer P1 是否关闭的证据分离；
- 若 Renderer private bytes 仍超 15%，Week80 仍不得判定 Diagnosis Complete。

## Phase 5：根因确认

根因必须同时满足：

1. 至少两个独立 profile 可重复；
2. 一个对照 profile 不出现同样增长，形成可证伪差异；
3. numeric counters 或 retained-type aggregate 支持；
4. 与 Week79 `~36%` private-bytes retention 方向一致；
5. 能定义最小产品修改边界；
6. 能定义不依赖内存抖动的 deterministic regression test。

不得仅凭：

- 单个 heap snapshot；
- 一次低内存重跑；
- “Electron/V8 正常”解释；
- forced GC 后下降；
- 延长 idle 后下降；
- 工作负载或阈值变更。

## Phase 6：诊断交付与交接

创建：

- `docs_md/weekly/80_week_review.md`
- ignored `artifacts/week80-renderer-private-bytes/diagnosis.json`
- 若 Diagnosis Complete，创建 `diagnosis-handoff.json`，至少包含：
  - tested source/package/AppHost identity
  - root cause category
  - affected component/files
  - reproduction profile
  - control profile
  - required regression tests
  - prohibited shortcuts
  - recommended minimal fix
  - required rerun matrix。

最终：

- `Diagnosis Complete`：允许进入 Week81。
- `Blocked`：停止，不得由 Week81 猜测修复。

## Critical Gates

- [ ] W80-G0 Week79 evidence、source/package/AppHost identity 一致。
- [ ] W80-G1 harness observer 扰动已量化且 bounded。
- [ ] W80-G2 JS heap、DOM/listener、projection/resync、process role 指标齐全。
- [ ] W80-G3 credential-free control matrix cleanup 全为 0。
- [ ] W80-G4 provider P5 在独立授权与只读边界内复现。
- [ ] W80-G5 根因由重复 profile、对照和 numeric evidence 共同支持。
- [ ] W80-G6 evidence 脱敏，无 rooted path、raw provider data 或完整 heap snapshot。
- [ ] `git diff --check` 和 evidence validator 通过。

## 新对话启动指令

复制以下内容到新的 Codex 对话：

```text
开始执行 docs_md/weekly/80_week_renderer_private_bytes_diagnosis.plan.md。
基线 HEAD=962d5dda4ae875299a96ba2c825bd13ec683240a；
最终受测产品 revision=8e227a4ca050e9bdff5d25d61bf89725fed26104。
先执行 credential-free Phase 0–2；不得修改 15% Gate、增加 retry、forced GC/reload 过 Gate，或直接实施未经证据支持的产品修复。
需要运行 provider-backed Phase 3 时先停下并请求本对话独立授权。
```

Provider-backed 阶段建议追加授权：

```text
授权 Week80 Phase 3；仅使用 .env.local 内 OPENAI_MODEL、OPENAI_BASE_URL、OPENAI_API_KEY，
仅注入本次 packaged Desktop 子进程；允许 disposable project；仅允许 workspace.read_text；
禁止 write、shell、MCP、Git tool 和其他网络行为。
```
