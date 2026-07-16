# 第 68 周回顾：Thread、Turn、Timeline 与持久化投影

状态：实现与技术验证已完成；Week 68 Gate 待提交后补跑 clean-source release acceptance

更新时间：2026-07-16

## 完成范围

- Core 新增 schema-v1 `ThreadRecord`、`TurnRecord`、`TimelineItemRecord`，冻结 typed id、状态机、UTC 时间、sequence、revision、source pointer、typed payload、limits 与 stable error code。
- 新增用户状态根下唯一 `ThreadStore`；`thread.json` 是 commit manifest，不可变 turn revision 和 timeline item 先落盘，manifest 最后原子替换。
- store 支持 create/read/list/rename/archive/delete、expected revision、单 active turn、turn transition、timeline append/dedupe/cursor page 与显式 interrupted recovery。
- delete 只接受 archived thread、expected revision 与完全匹配的 confirmation；持有路径哈希 OS mutex 完成 quarantine move，不递归进入 reparse point。
- Application 新增不暴露 store entity 的 thread create/list/get/rename/archive/delete DTO 与 service，执行 workspace binding、redaction、pagination、aggregate bound、pointer hydration 和 cancellation。
- `FileConversationStore` 新增 strict、bounded、fingerprinted authoritative read；旧 CLI session list/show/save/rename/delete 路径未修改。
- session migration 只处理显式指定的 session，提供 preview/import、fingerprint recheck、确定性 ID/排序、幂等 retry 与正常 archive + confirmed delete rollback。
- deterministic typed timeline projector 覆盖 message、plan、tool、changes、report、warning 与 turn completion；不接受任意 JSON payload，不持久化 raw arguments、approval material、artifact content 或 full output。
- AppHost、desktop-v1 contract、CLI session handler 与 Desktop Main/Preload/Renderer 均未接入 thread 能力；该范围保留给 Week 69-71。

## Source 与环境

- Branch：`week-02-cli-commands-doctor-config`。
- Week 68 起点：`051087b965cbf5420c7f75eab34faa65357a5342`。
- 起点已有 3 个用户文档修改和未跟踪 Week 68 计划；实现保留这些改动，没有回退或覆盖。
- .NET SDK：`9.0.308`；Node/npm：`22.13.0` / `11.7.0`。
- Week 67 full suite 基线：`1288/1288`；Week 68 最终：`1321/1321`。
- 未创建提交。标准 `Build-Release.ps1` 正确拒绝 dirty source；`-AllowDirtySource` 产物只用于 smoke，不是 clean-source acceptance artifact。

## Persisted contract

| 范围 | 冻结结果 |
|---|---|
| Schema | thread、turn、timeline 各自 `CurrentSchemaVersion = 1`；unknown member 拒绝，JSON depth 64 |
| Identity | `thread_` / `turn_` / `item_` + 24 位 lowercase hex；native 使用 CSPRNG，import 使用 SHA256 派生 |
| Thread | workspace binding、title、derived status、revision、turn refs/hash、committed sequence/count/bytes、origin、safe persistence policy |
| Turn | immutable revision snapshot、ordinal/status/timestamps、safe task summary、stop/error、typed pointers、timeline range |
| Timeline | stable id、thread/turn binding、continuous sequence、UTC timestamp、allowlisted type、typed payload、redaction metadata |
| Bounds | manifest 1 MiB；turn 128 KiB；item 16 KiB；1,000 turns；10,000 items；64 MiB committed child bytes |
| Page | thread list 默认 50/最大 200；timeline 默认 50/最大 100；`afterSequence` cursor |
| Application | 单次结果目标 768 KiB；diagnostic 100 x 4 KiB；title 512 UTF-8 bytes |

source pointer 只允许 `session`、`job`、`queue`、`report`、`trace`、`run`、`artifact`。persisted availability 是 cache；Application hydration 重新查询现有权威 metadata。artifact 只查询 manifest metadata，不读取内容或重新 hash。

## Atomicity、revision 与 recovery

- child JSON 使用同目录 temporary file、write-through、flush-to-disk 与 atomic move；manifest 最后替换，是唯一 committed boundary。
- 读取只承认 manifest 引用的 turn revision 与 `committedSequence` 内 item；未提交 orphan 不可见。
- missing child、turn hash mismatch、duplicate/gap/out-of-order sequence、binding mismatch、unknown field、unsupported schema、oversize 与 reparse 均 fail closed。
- mutation 在路径哈希命名的 OS mutex 下串行化；`.thread.lock` 继续作为 thread-owned marker 和非协作 handle 冲突检测。mutex 名不包含路径原文。
- append 必须携带 expected revision、expected next sequence 与 stable mutation id；同 id + 同 canonical payload 返回原结果且不增加 revision，同 id + 不同 payload 返回 conflict。
- active `running` / `waiting-for-approval` / `canceling` 读取只返回 `recovery-required`；显式 recovery 将 turn 终结为 failed/interrupted，并追加 warning/terminal projection，不调用 model、tool、worker 或 source store。
- delete 在锁内重新读取 revision、验证 archived/confirmation/ownership/reparse 后移动到 `.deleting-*`；session/job/queue/report/run/artifact/workspace 均不在删除范围内。

## Session migration

- authoritative read 最大 4 MiB / 10,000 records，验证 UTF-8、schema、unknown field、path binding、reparse chain 与 changed-during-read。
- fingerprint 由 source kind、canonical session identity 与 bounded content SHA256 生成；preview 不创建 thread 目录。
- import 前重新读取 fingerprint；changed source 返回 `session-import-source-changed`，不产生 partial thread。
- message/tool/error/agent-run 以 timestamp、type priority、collection kind、original index 稳定合并；sequence 从 1 重新连续分配。
- raw tool arguments、完整输出、prompt、approval 与 secret 不进入 timeline。session bytes、path、name 与 last-write timestamp 在 preview/import/rollback 测试中保持不变。
- 相同 source identity + fingerprint retry 返回既有 deterministic thread；同 session 内容变化返回 conflict。rollback 只走 archive + confirmed delete，之后 preview fingerprint 保持一致。

## 自动化测试

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build --filter "FullyQualifiedName~Thread|FullyQualifiedName~Turn|FullyQualifiedName~Timeline|FullyQualifiedName~Migration|FullyQualifiedName~Architecture"
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

- Release build：7 projects，`0 warnings / 0 errors`。
- 精确 Week 68 contract/store/Application/migration/architecture 集合：`36/36`。
- 计划宽筛选实际命中：`211/211`，不是 0-test 假通过。
- 最终标准并发 full suite 第 1 次：`1321 passed / 0 failed / 0 skipped`，约 97 秒。
- 最终标准并发 full suite 第 2 次：`1321 passed / 0 failed / 0 skipped`，约 95 秒。
- 新增证据覆盖 id/schema/UTF-8、state transition、revision race、OS mutex、atomic child/manifest、orphan visibility、hash tamper、missing child、duplicate/gap、cursor、recovery、workspace mismatch、source hydration、strict fingerprint、idempotent import、source-changed、rollback 与 reparse-safe delete。
- architecture tests 禁止 Application public API 暴露 thread store records/filesystem/JSON document，并确认 desktop-v1、AppHost 与 CLI session 无 thread dispatch/双写。

## CLI、Desktop 与 package

- dirty-source 非验收 release publish：Passed，SDK `9.0.308`，`sourceDirty=true`。
- CLI smoke：Passed；真实模型、daemon 与真实 Gerber/TIFF 外部工具路径按脚本环境开关跳过。
- Desktop verify：contract/notice drift、typecheck、ESLint、5 files / `11/11`、Main/Renderer build 与 production security 全部通过。
- AppHost publish、Desktop package、headless smoke、window-close smoke 全部通过；两种 smoke 的 AppHost orphan delta 均为 0。

| Evidence | Week 67 | Week 68 | 变化 |
|---|---:|---:|---:|
| Package files | 77 | 77 | 0 |
| Unpacked package | 465,075,187 | 465,312,755 bytes | +0.05% |
| `app.asar` | 2,175,490 | 2,175,490 bytes | 0% |
| Desktop executable | 222,753,280 | 222,753,280 bytes | 0% |
| AppHost executable | 79,113,224 | 79,350,792 bytes | +0.30% |
| 3-second process count | 6 | 6 | 0 |
| 3-second working set | 406,507,520 | 404,094,976 bytes | -0.59% |
| 3-second private bytes | 241,971,200 | 239,906,816 bytes | -0.85% |
| Lifecycle sample | 8,097 | 7,832 ms | -3.27% |
| Orphan AppHost delta | 0 | 0 | 0 |

所有 package/process/memory 指标均低于 15% 调查阈值。

## Gate 结论

| Gate | 状态 | 证据 |
|---|---|---|
| Persisted schema/state/limits/error code | Passed | strict typed contract 与 contract tests |
| Atomicity/revision/orphan/corruption | Passed | manifest commit、mutex、expected revision、hash/missing/duplicate/reparse tests |
| Thread/turn/timeline lifecycle | Passed | CRUD、single active、terminal immutable、cursor/dedupe/recovery tests |
| Application/redaction/bounds/cancellation | Passed | workspace binding、bounded DTO、file-level cancellation、pointer diagnostics |
| Session migration/source preservation | Passed | preview/import/idempotency/source-changed/rollback/reparse tests |
| Architecture/no-second-truth | Passed | dependency/public API/protocol/CLI automated constraints |
| .NET regression | Passed | 0 warning；full suite 连续两次 `1321/1321` |
| CLI/Desktop regression | Passed | CLI smoke、Desktop verify/package、双 smoke、orphan 0 |
| Performance | Passed | package/AppHost/working set 增长均低于 15% |
| Clean-source release acceptance | Pending | 当前未提交工作树被标准 release script 正确拒绝；提交后需不带 `-AllowDirtySource` 重跑 |

因此实现、测试、package 与性能 Gate 已通过，但 Week 68 总 Gate 在 clean-source release acceptance 补齐前不标记最终 Passed。

## Week 69 输入

- schema-v1 thread/turn/timeline persisted contract、typed ids、状态机、sequence、revision、limits 与 stable error code。
- `thread.json` commit boundary、immutable child、OS mutex、expected revision、corrupt/orphan/reparse/delete quarantine 语义。
- bounded/cursor-based Application create/list/get/rename/archive/delete DTO；Week 69 只能序列化这些 DTO，不能暴露 Core records 或路径布局。
- typed timeline envelope/payload、stable pointer hydration、redaction、aggregate bound 与 recovery-required diagnostic。
- explicit session preview/import、deterministic projection 与 source fingerprint/idempotency/rollback 语义。
- desktop-v1 当前无 thread method；Week 69 应从 reviewed Application DTO 新增协议，不得让 AppHost 直接读取 `.caicli/threads`。
- clean-source release acceptance 是进入 Week 69 前唯一未关闭的验收项。
