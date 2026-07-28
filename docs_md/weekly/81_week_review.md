# Week 81 Review：Renderer Memory Retention 最小产品修复

状态：`Candidate Ready for Requalification`

日期：2026-07-27

handoff exact clean 起点：`960b230683226e7b313f31fbb771065702a54bc5`

exact clean candidate revision：`e9e062d985545377aa373767f563de6a2bb30a64`

## 最终决定

Week81 达到 `Candidate Ready for Requalification`。确定性 regression 已在 baseline 红、修复后绿；修复后的 clean candidate 通过 full credential-free matrix、冻结 Week77 五 profile Gate 和 Week80 credential-free controls。真实 provider 没有运行，最终 provider-backed 重判移交 Week82；本周不宣布 `Preview Ready`。

没有修改 15% Gate、workers、retries 或 warm/post 窗口；没有使用 forced GC、定时 reload、删除 durable history、协议升级、provider 重构或 UI redesign。没有读取 `.env.local`，没有 push、tag、上传、发布或部署。

## Entry Gate 与 baseline 红灯

- 严格读取 Week80 review、`diagnosis.json` 与 `diagnosis-handoff.json`，确认结论 `Diagnosis Complete`、handoff revision `960b230…`、Desktop `E2A5BE…`、AppHost `DC46DB…`。
- 分支从 `960b230…` exact clean revision 创建。
- handoff 六项 deterministic regression 在 baseline 为 `6 failed / 7 passed / 13 total`，修复后为 `13/13`。
- 后续定位 terminal/review workload 时又保留了独立红绿 regression：Review auto activation、Terminal structural DOM、Terminal Text node、Terminal Renderer bound、ready Review projection dedup。失败 evidence 未被 fixed evidence 覆盖。

主要 ignored evidence：

- `baseline-regression.json` / `fixed-regression.json`
- `baseline-terminal-dom-regression.json` / `fixed-terminal-dom-regression.json`
- `baseline-terminal-text-node-regression.json` / `fixed-terminal-text-node-regression.json`
- `baseline-terminal-render-bound-regression.json` / `fixed-terminal-render-bound-regression.json`
- `baseline-review-dedup-regression.json` / `fixed-review-dedup-regression.json`

## 最小产品修复

修改保持在 Week80 证据支持的 Renderer projection/commit 与 transient DOM 边界：

- 所有 `thread.changed` 仍 dispatch；authoritative resync 只在每 turn 两个重复 `committedSequence` 生命周期边界排队。
- Timeline durable history、ordering 与 identity 不变；历史 event card 只在用户打开对应 turn 后物化。
- Composer、TaskControls、recovery/refresh 与 Terminal 生命周期使用稳定 DOM identity。
- Terminal 使用既有 `afterCursor`，相同 cursor/status 不再产生重复 React commit；React state 只保存 terminal metadata，不保存完整 scrollback projection。
- AppHost/协议仍保留 64 KiB terminal scrollback；Renderer DOM 只物化尾部 8 KiB并保留 truncated marker 与尾部内容。没有删除 durable history。
- 已就绪且当前选中的 Review tab 不再重复执行 loading/full projection/ready commit；Changes、Reports 与 recovery 可见性不减。

未修改 ThreadStore、desktop-v1、provider、approval、workspace guard、redaction、workers、retries 或 Gate measurement。

## Root-cause 复测

非 Gate staged diagnosis（trace/screenshot/video off，无 heap snapshot、无 forced GC）将 workload 拆为 reload、Changes、terminal open/input/cancel/close：

- reload 后自然 30 秒回到单 Document；
- Changes 增量接近 0；
- 完整 64 KiB terminal output DOM materialization 是剩余 private allocation 的主要阶段；open 不增长，cancel/close 只产生小增量；
- bounded terminal DOM 后，冻结单 profile 从 `15.42–16.19%` 降到 `10.20%`，正式五 profile 稳定为 `8.78–9.88%`。

曾试验但未进入 candidate 的改动包括 React.memo、pagehide unsubscribe、root unmount、Changes DOM flatten 和 Review lazy activation；相应失败 evidence 保留，产品改动均已撤销。

## Credential-free 验证

| Gate | 结果 |
| --- | --- |
| deterministic regression | Passed；baseline 红，fixed 绿 |
| Desktop verify | Passed；25 files，`112/112`，typecheck/lint/build/security 全通过 |
| full .NET | Passed；`1421/1421`，skipped `0` |
| dependency audit | Passed；`0 vulnerabilities` |
| package audit | Passed；78 files、12 asar entries、forbidden payload `0` |
| unpacked E2E | Passed；`9/9` |
| packaged E2E | Passed；`8/8` |
| accessibility automation | Passed；`2/2`，cleanup `0` |
| packaged smoke | Passed；`8/8`，cleanup `0` |
| protocol measurement | Passed；3 rounds、每轮 48 responses、process/temp `0` |
| Week77 frozen Gate | Passed；`5/5`、1 worker、0 retries、固定 30 秒窗口 |
| Week80 credential-free controls | Passed；C0–C7 evidence status 全 Passed、identity 一致、process/temp/config 全 `0` |
| `git diff --check` | Passed |

Week80 旧 fixture 的 cross-world notification callback 在当前 Electron control run 中没有进入 Renderer counters；没有伪造该 counter。根因上限由 deterministic policy regression 验证，C1/C3/C4/C5/C7 的资源 controls 使用公开 thread selection 路径执行 1/2/6/20 次 authoritative projection；通知/turn/item 规模、30 秒窗口和资源 Gate 不变。C0/C2/C6 使用原 control 路径通过。

Week77 五 profile private bytes：

1. `9.3029%`
2. `9.8796%`
3. `8.7824%`
4. `9.0921%`
5. `9.0979%`

对应 working set 为 `3.5937%、3.8439%、2.8251%、3.2311%、3.0300%`；每项 process/temp cleanup 均为 `0`。package gate 与 profile gate 均为 true。

## Candidate identity

- source revision：`e9e062d985545377aa373767f563de6a2bb30a64`
- Desktop executable：222,753,280 bytes，SHA-256 `C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0`
- package tree：464,708,708 bytes；source-bound evidence SHA-256 `ADDFBC3114B10E31633F1E9A9500934B9B8F17BCD3222CD4D02217AA606C6E38`
- `app.asar`：555,012 bytes，SHA-256 `D7959F8886DAE4F1E66068D728A8EF74C19231D8ACCA68F67A603FA2CE034C04`
- AppHost：79,941,168 bytes，SHA-256 `DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`

package 从该 clean candidate 重建。之后的 review/handoff commit 只包含文档与 evidence validator，不改变 candidate package 内容。

## Critical Gates

| Gate | 结论 |
| --- | --- |
| W81-G0 handoff 完整且 exact baseline 可复现 | Passed |
| W81-G1 regression baseline 失败、修复后通过 | Passed |
| W81-G2 修改范围与 root cause 一致 | Passed |
| W81-G3 stale response、approval、recovery、timeline ordering | Passed |
| W81-G4 full credential-free matrix | Passed |
| W81-G5 Week77 5 profiles 与 Week80 controls | Passed |
| W81-G6 candidate identity 与 cleanup | Passed |
| `git diff --check` 与 Week81 evidence validator | Passed |

## Week82 handoff

Week82 必须绑定上述 exact candidate/package/AppHost identity，重新执行 provider-backed read-only、controlled write、sequential approvals、crash/restart no-replay、resource/idle Phase 6/7/8/9。不得使用本周 credential-free 结果替代真实 provider requalification，也不得读取未明确授权的配置或扩大工具边界。
