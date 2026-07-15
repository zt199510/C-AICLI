# Vertical Workflow Hardening Contract

状态：Week 64 已完成；本文是 0.5.0 Gerber/TIFF vertical workflow 的安全、schema、smoke 与发布收口依据。

## Week 58-63 基线

| 周 | 已形成的 0.5.0 source Preview / Gate 证据 | 当周结束时仍未完成 |
|---:|---|---|
| 58 | Project Pack v1 contract、Gerbv/ImageMagick/LibTIFF 工具链 Gate、CC0 fixture、固定 typed argv | discovery、run、verification、artifact lifecycle 与 real-tool smoke |
| 59 | bounded workspace input discovery、static/probe preflight、deterministic plan 与 fingerprint | staging、conversion、verification、human gate 与 artifact lifecycle |
| 60 | managed run layout、atomic checkpoint、bounded staging、封闭状态机、safe resume foundation | 真实 conversion、TIFF verification、human gate、restart 与 prune |
| 61 | approval-gated Gerbv/ImageMagick typed adapter、bounded process/cwd/env/output、真实 conversion evidence | TIFF engineering verification、human gate 与 artifact lifecycle |
| 62 | bounded Magick.NET TIFF inspection、strict baseline、verification report、managed preview、`awaiting-acceptance` gate | human accept/reject、restart 与 artifact lifecycle |
| 63 | strict artifact manifest/index、human accept/reject、read-only resume、new-attempt restart、managed prune/tombstone | Week 63 扩展后的 real-tool smoke 尚未实际通过；hardening/schema/release readiness 待收口 |

上述证据均不改变已发布的 0.4.0 Accepted 边界。0.5.0 的 accepted/release decision 只能在 Week 65 从干净提交执行最终验收后产生。

## Accepted / Preview / Deferred 汇总

- Accepted：0.4.0 release decision 与现有 release artifact 不变。
- 0.5.0 source Preview：Week 58-63 已实现的 pack、plan、staging、typed conversion、TIFF verification/preview、human gate、resume/restart 和 managed artifact lifecycle。
- Gate evidence：冻结的 Gerbv 2.13.0、ImageMagick 7.1.2-27、LibTIFF 4.5.1、CC0 fixture、固定参数模板和许可结论。
- Deferred：ZIP/network input、完整 CAM/EDA manufacturing correctness、model vision hard gate、accept undo、artifact restore/quarantine recovery、background retention/quota worker、scheduler、parallel writer、remote runner/provider routing、API control/SSE、team platform、marketplace 和 UI。

## Week 64 Release Blockers

1. 必须实际通过一次 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1`，覆盖真实 conversion、hard verification、preview、accept/reject、artifact verify/export、prune、source/workspace output preservation 和 process/temp cleanup。
2. default packaged smoke 必须继续 model-free、network-free、real-tool-free，并补齐 vertical approval denied、missing tool、partial output、corrupt state 和安全 cleanup 场景。
3. input/tool/output/TIFF/state/prune 的 adversarial 与故障回归必须覆盖 outside/reparse/TOCTOU/tool swap/argument injection/disk full/process leak/decoder bomb/prune race，以及 corrupt schema/stale running/invalid transition/secret diagnostics。
4. pack/plan/run/artifact/verification 的 schema、stable error code 和 exit policy 必须形成发布契约，并由 JSON contract tests 固定。
5. release build 必须在 release acceptance 模式拒绝 dirty tracked source，记录完整 `sourceRevision`、SDK、configuration、PDB policy 和逐文件 SHA256 inventory，并产生独立 package checksum evidence。
6. release/runtime 文档必须准确区分 Accepted、Preview、Deferred；不得把 fake driver、文件存在、metadata valid 或 preview 单独描述为真实业务验证通过。

任一 blocker 未关闭时，Week 64 不得声称已验收，Week 65 也不得只凭版本号或制品存在作出 Accepted 决定。

## Threat Model

C-AICLI 是本地确定性 orchestration/runtime，不是 OS sandbox。攻击者可控制 workspace 内容、文件名、plan/baseline JSON、显式工具路径指向的文件，以及同一 OS user 下竞争文件系统的其他进程；攻击者不能因此获得扩展 pack-owned executable/argv/env/cwd/output 的权力。

| 资产/入口 | 主要威胁 | 强制控制 | 失败语义与残余风险 |
|---|---|---|---|
| input discovery | outside/UNC/device/glob/traversal、reparse、case/canonical collision、huge tree、读时变化 | canonical workspace guard、network/device/reparse 拒绝、depth/count/size/time limits、stream hash 前后 size/mtime 复核、稳定排序 | fail closed 且不产生 runnable fingerprint；单次本地 filesystem call 仍依赖 OS 返回 |
| staging copy | source mutation、target escape/overwrite、reparse swap、部分复制、磁盘/权限失败 | 只复制 inventory supported entries；复制前后重验 source；安全扁平命名、create-new、bounded stream、destination hash；run-root containment/reparse guard | staging 不完整不得进入 ready；源只读且显式 workspace output 不创建 |
| external executable | path/tool swap、hash/version drift、argument injection、untrusted env、unexpected child、stdout/stderr flood | explicit dependency binding、filename/regular/reparse/hash/version probe、approval 前后及 exit 后 identity check、pack-owned `ArgumentList`、无 shell、cleared allowlist env、managed cwd/temp、bounded output | mismatch/approval denial/cleanup failure 为 terminal evidence；同一 OS user 仍可竞争路径，C-AICLI 不是 sandbox |
| conversion output | outside run/workspace boundary、overwrite、unexpected/partial/empty/oversized file、post-run mutation | output root/file no-overwrite、canonical/reparse guard、declared filename/count inventory、size/SHA256、进程期间 staged read lock、后续 verify 再重验 | exit 0 或文件存在不能单独成功；partial output 保留为失败证据 |
| TIFF decoder/baseline/preview | malformed/truncated/BigTIFF/decompression bomb、unsupported compression/page、baseline escape/mutation、preview escape | mature Magick.NET codec、classic TIFF allowlist、dimension/frame/pixel/memory/time/report limits、process-global serial lock、workspace baseline guard、read-only identity、managed create-new report/preview | native codec 是第三方 in-process attack surface；preview 与 metadata 不是 correctness proof |
| run/checkpoint/manifest | corrupt/unsupported/unknown schema、split write、invalid transition、stale running、approval bypass 注入 | strict JSON unknown-field rejection、size/schema/id/revision/state/fingerprint cross-check、atomic same-dir replace、revision lock、compiled transition table、`approvalPersisted=false` | mismatch fail closed 且不自动修复；三个 atomic files 不是单一文件系统事务 |
| resume/restart | 自动重放 execute、复用 approval、tool/input/output/policy drift、reserved child 冲突 | resume 只渲染 revalidation plan；running/interrupted 要求显式 restart；重新 probe/approval；new child/job/output attempt；parent evidence immutable | crash 可留下 reserved-but-missing child，需重验后继续或人工诊断 |
| human gate | fake/preview/metadata-only accept、report mutation、double decision、secret actor/reason | only CLI human command；current revision；唯一 hard verification report path/schema/hash/levels 重验；redaction；terminal decision conflict | actor 是本地 metadata，不是 authenticated identity |
| artifact prune | outside/source/workspace-output delete、reparse/race/locked、manifest/tombstone mutation、partial delete | terminal owned managed allowlist、default dry-run、age/status/size filters、revision/pointer/hash recheck、same-run quarantine、post-move hash、per-item diagnostics、retained tombstone | crash 可留下 quarantine/tombstone split state；restore/recovery 与 background pruning Deferred |
| diagnostics | secret-like path/filename/actor/reason/stdout/stderr/raw exception 泄漏 | bounded summaries、central secret redaction、JSON DTO projection、exception 类型映射到 stable error；不持久化 raw argv/input/approval | 本地路径仍是敏感 metadata，导出前必须再次 redaction |

## Command Boundary Table

| 命令 | workspace/source 读取 | user-state / workspace 写入 | external process | approval | 网络 | 成功/失败边界 |
|---|---|---|---|---|---|---|
| `packs list` | built-in catalog only | 无 | 无 | 无 | 无 | schema-valid catalog 为 0 |
| `packs doctor` | explicit tool path static metadata | 仅显式 trace | 默认无；只有 `--probe` 执行 fixed version argv | 每个 probe 当前 shell-risk approval | 无 | required tool ready 为 0，否则 1；参数错误 2 |
| `packs plan gerber-tiff` | bounded input inventory、静态 tool identity | 仅调用者自行保存 stdout；可选 trace | 无 | 无 | 无 | runnable 为 0；safe but missing tool/blocked 为 1；usage/config 为 2 |
| `packs run ... --dry-run` | strict plan、current input/tool/output/policy | 新 run/checkpoint/manifest/staging/job；不写 explicit output | 无 | 不申请/不保存 | 无 | ready 为 0；revalidation/store failure 为 1；usage 为 2 |
| `packs run` | dry-run 边界加 staged/tool revalidation | run/checkpoint/manifest/log/job、managed PNG、explicit no-overwrite TIFF | Gerbv + ImageMagick fixed typed invocations | probe 与每个 external stage 逐次申请 | 无 | conversion 到 verifying 为 0；denied/timeout/partial/cleanup failure 为 1；usage 为 2 |
| `packs verify` | run/state/source/staging/TIFF、可选 baseline | managed JSON/markdown report；run/checkpoint/manifest/job | 无；使用 bounded in-process codec | 无 | 无 | hard checks pass 到 awaiting-acceptance 为 0，否则 1；usage 为 2 |
| `packs preview` | hard verification identity 与 TIFF | managed PNG/contact sheet/report、manifest/job | 无；使用 bounded in-process codec | 无 | 无 | preview 生成只证明 inspection UX，为 0；boundary/decode/write failure 为 1 |
| `packs resume` | current tool/input/output/policy/evidence | 无 operational mutation；仅显式 trace | 无 | 只报告未来 stage 需要重新 approval | 无 | eligible plan 为 0；terminal/drift/corrupt 为 1 |
| `packs recover --mark-interrupted` | running record 与 operator confirmation | running -> interrupted、correlated job/queue failure | 不发现、不终止进程 | 无 | 无 | transition 成功为 0；非 running/corrupt 为 1 |
| `packs restart --from execute` | interrupted parent、current tool/input/output/policy | reservation、新 child run/job/staging/attempt output | 与 run 相同的 fixed typed invocations | probe 与每个 stage 重新申请 | 无 | child conversion 到 verifying 为 0；conflict/denied/failure 为 1 |
| `packs accept/reject` | current run + strict verification report | decision、run/checkpoint/manifest/job | 无 | 无 | 无 | 单次 eligible human decision 为 0；fake/changed/double decision 为 1 |
| `artifacts list/show/export` | strict manifest/index；export 仅 metadata | 无（stdout only） | 无 | 无 | 无 | valid records 可与 corrupt diagnostics 并列；指定项 invalid/not found 为 1 |
| `artifacts verify` | owned pointer canonical path/size/hash | 无 | 无 | 无 | 无 | identity match 为 0；external/corrupt/changed 为 1 |
| `artifacts prune` | run/manifest/job 与候选 identity | dry-run 无；apply 仅 quarantine/delete managed files 并写 tombstone/job | 无 | `--apply` 是显式 destructive intent，不持久化 bypass | 无 | 全部候选安全处理为 0；per-item failure 保留内容并返回 1；usage 为 2 |

所有命令的 JSON stdout 必须保持单一合法 JSON 值；trace、run、job、report 和 manifest 只是相互关联的 evidence，不取代各自已有的 operational truth，也不创建第二套 task report truth。

## Frozen JSON Schemas

0.5.0 vertical workflow 的当前 schema version 全部为 `1`。新增/删除/改名 required 字段、改变字段类型或放宽 persisted JSON unknown-field 策略都属于 breaking change，必须提升对应 schema，而不能在 hardening/release 周静默漂移。

| Surface / file | `type` / schema marker | Required top-level contract | Unknown fields |
|---|---|---|---|
| `packs list` | `type=packs.list`, `schemaVersion=1` | `type,schemaVersion,packs,diagnostics` | output consumer 可忽略；producer shape 由 contract tests 固定 |
| `packs doctor` | `type=packs.doctor`, `schemaVersion=1` | `type,schemaVersion,packId,status,probeRequested,tools,diagnostics` | output consumer 可忽略；不得回显 raw probe/path |
| `packs plan` / saved plan | `type=packs.plan`, `schemaVersion=1`, `planSchema=gerber-tiff.plan.v1` | status/pack/plan id+fingerprint/workspace-relative input+output/inventory/tools/stages/artifacts/diagnostics/redaction flags | saved plan loader 只投影 allowlisted execution fields；任意 argv/approval/raw content 不进入 managed copy |
| `run.json` | `ProjectPackRunRecord.CurrentSchemaVersion=1` | run/revision/pack/plan+policy fingerprint/state/timestamps/correlation/artifacts/redaction；可选 error/acceptance/restart | strict reject |
| `checkpoint.json` | `ProjectPackRunCheckpoint.CurrentSchemaVersion=1` | run/revision/state/fingerprints/time/stages/`approvalPersisted=false` | strict reject；`approvalPersisted=true` invalid |
| `input-manifest.json` | schema `1` | run/plan/fingerprint/sorted staged inputs/source-read-only flags | strict reject |
| `artifact-manifest.json` | `type=managed-artifact-manifest`, `schemaVersion=1` | owner/run revision+state/times/artifacts；可选 acceptance/tombstone | strict reject and no auto-repair |
| `packs run/show/cancel/accept/reject` | `type=packs.run`, `schemaVersion=1` | `type,schemaVersion,status,run,checkpoint,redaction` | output consumer 可忽略；stored run/checkpoint 仍 strict |
| `packs resume` | `type=packs.resume`, `schemaVersion=1` | state/eligibility/next action/approval+restart flags/error/summary/`approvalPersisted=false` | output only |
| `packs restart` | `type=packs.restart`, `schemaVersion=1` | parent/new run/attempt/output/from/approval+partial evidence/diagnostic | output only；diagnostic 逐字段 redaction |
| `packs verify` / verification report | `type=packs.verify`, `schemaVersion=1` | hard/human/correctness flags/baseline/limits/levels/artifacts/pixel comparisons/diagnostics/summary | report consumer strict at human gate；raw exception 不序列化 |
| verification baseline | `type=gerber-tiff.verification-baseline`, `schemaVersion=1` | pack/tool/input identity and outputs with exact/pixel policy | strict reject |
| `packs preview` / preview report | `type=packs.preview`, `schemaVersion=1` | correctness/open/upload flags/previews/diagnostics/summary | report projection redacted；no-overwrite |
| `artifacts list/show/verify/prune` | `type=artifacts.*`, `schemaVersion=1` | command-specific artifact/diagnostic/result fields | output only；path/reason/tombstone/summary 逐字段 redaction |

## Stable Error Codes

权威集合是编译期常量，不从 exception message、外部工具 stderr 或模型文本生成。Week 64 contract test 冻结集合大小和 kebab-case 唯一性：

| Family | Source | Frozen count | Semantics |
|---|---|---:|---|
| discovery/plan | `GerberTiffDiagnosticCode` | 由 input/output/tool plan tests 固定 | `input-*`、`output-*` 和 tool preflight；unsafe/limit/changed 均阻止 runnable plan |
| run/tool/process/state | `ProjectPackRunErrorCode` | 46 | `pack-run-*` 加 `pack-tool-*`、`pack-execution-*`、`pack-output-*`、approval/process/human gate codes |
| TIFF/baseline/preview | `TiffVerificationErrorCode` | 27 | 全部 `pack-tiff-*`；signature/decode/resource/format/baseline/compare/preview/report 分离 |
| artifact lifecycle | `ManagedArtifactErrorCode` | 14 | 全部 `artifact-*`；manifest/path/identity/prune failure 分离 |

同一失败不得根据 renderer 或 text/JSON 模式改变 error code。原始 exception 只映射到上述 stable code 和固定脱敏 summary；不得直接进入 JSON、trace、run、job、manifest 或 report。

## Exit Policy

- `0`：请求的 deterministic operation 完成，或只读 list/index 在保留逐项 diagnostics 的同时成功返回其余合法记录。`packs plan` 只有 `runnable` 才为 0；`packs verify` 只有所有 requested hard checks 通过才为 0。
- `1`：runtime/domain/policy failure，包括 unavailable/missing tool、approval denied、unsafe/drift/corrupt state、verification mismatch、ineligible human transition、partial output、timeout/cancel/cleanup 或 prune item failure。JSON 仍必须是该命令的合法 failure object。
- `2`：CLI usage/configuration error，包括未知 command/option/dependency、缺少 required argument、无效 enum/age/filter/format 或互斥参数。

fake evidence、文件存在、exit 0、metadata-valid、preview-generated 和 dry-run ready 均不能把本应为 1 的真实 conversion/verification/acceptance 结果提升为 0。
