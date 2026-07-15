# 第 63 周回顾

状态：已完成；Artifact Lifecycle、Human Acceptance 与 Safe Resume/Restart 为 0.5.0 source-only Preview；本机 real-tool smoke 未配置并明确留作 Week 64 hardening 输入

## 范围结论

- 新增 schema-v1 `artifact-manifest.json`：deterministic artifact id、pointer/kind、run/job/queue/root/parent/
  attempt owner、managed/workspace-output/source/external ownership、path、size/SHA256、verification、availability、
  retention 与 tombstone。run create/update 同步维护 manifest；missing/corrupt/unsupported/unknown/mismatched/
  reparse state fail closed，不自动修复。
- 新增只读 `artifacts list/show/verify/export` text/JSON/markdown。list/index 对 corrupt run 返回 diagnostics 并
  继续；verify 通过 ownership boundary、workspace/run guard、reparse、size/SHA256 重验，不把 external pointer
  当作 owned evidence；export 只写 stdout，不复制 artifact 内容。
- 新增 `packs accept/reject` human gate。只有当前 `awaiting-acceptance` revision 可决定；accept 必须重新读取
  唯一 indexed `tiff-verification-json` 并验证 reports-root containment、reparse、size/hash、schema/run id、
  hard pass、levels 与无 error diagnostics。fake-awaiting、failed/changed/missing report、preview-only 与 double
  decision 均拒绝。decision 记录 redacted actor/note/reason/time、based-on revision、verification artifact id/hash。
- `packs resume` 现在无需 `--dry-run` 也只渲染 revalidation plan，不执行 stage。pre-execution 要求 output
  不存在；post-execution 允许 frozen output directory 已存在，但重验 tool/input/policy、staging、workspace TIFF、
  managed evidence；awaiting-acceptance 额外重验 hard report。approval 始终 invocation-local。
- 新增 `packs restart <run-id> --from execute`。只接受 interrupted parent；当前 approved fixed probes 后原子
  reserve child run，生成 new run/job、attempt lineage 与 `<original>.attempt-NNNN` output，复用 Week 61 typed
  executable/arguments、bounded cwd/env/output、approval、timeout/cancel/process cleanup。parent partial evidence
  不覆盖；同一 reservation 不静默重复。
- 新增 `packs recover --mark-interrupted` 本地人工 stale-running 入口。它只在 operator 确认进程不再活动后
  记录 `running -> interrupted` 和 correlated running job/queue failure；不发现、终止或 replay 进程。
- 新增 `artifacts prune` age/status/min/max-size filters，默认 dry-run。apply 只选择 accepted/rejected/failed/
  canceled run 的 managed+owned+terminal-prunable artifacts；run/manifest revision、pointer、allowlist root、
  reparse 与 identity 在删除前重验。文件先原子 move 到 same-run unique quarantine，再复核 hash；race/locked/
  per-item failure 保留内容并继续 diagnostics。成功后保留 run/checkpoint/job 和 manifest/job tombstone。
- 新增 user-level `artifactRetention.defaultMinimumAgeDays`，默认 30、范围 1-3650。workspace value 被忽略，
  不能由仓库降低 user-state retention。该配置只是 prune 未传 `--older-than` 时的默认值，不是 scheduler 或授权。
- job `taskReport` 继续为 null；run/checkpoint 是 operational truth，artifact manifest 是严格 lifecycle index，
  job/queue 是 correlation，不建立第二套 task report/run truth。

## Lifecycle 安全证据

- acceptance 只有 human CLI surface；model、skill、tool、pipeline、automation、preview 和 metadata-valid 均无
  自动 accept 入口。`content-compared=not-requested` 不被改写为 passed。
- approval bypass 不写入 run/checkpoint/manifest/job。resume 是只读 plan；restart 在 reservation 前重新 probe/
  approval，child external stages 仍逐项走当前 approval。
- Project Pack 仍是 deterministic domain-tool flow，不是 skill；Week 61 fixed Gerbv/ImageMagick argument templates、
  `ProcessStartInfo.ArgumentList`、environment clear/allowlist、timeout/cancel/tree cleanup 未开放新参数来源。
- prune retention allowlist 只有 `artifacts/`、`reports/`、`logs/` 下 manifest-owned files。`run.json`、
  `checkpoint.json`、`artifact-manifest.json`、`plan.json`、`input-manifest.json`、source、baseline、explicit
  workspace TIFF、external pointer、running/interrupted/corrupt/outside/reparse path 永不成为候选。
- prune apply 后 job artifact pointer 变为 `exists=false`，另增 inline tombstone；original path/size/SHA256/
  removedAt/reason/run/job/queue metadata 保留，历史仍可解释内容为何不存在。
- 新增 17 个 `ManagedArtifactLifecycleTests`，该 collection 禁止并行以免其 temp/hash workload 放大既有
  wall-clock tests。覆盖 identity/schema/store/corrupt、stable renderers、hard gate、double decision、reason
  redaction、report mutation、tool/input/policy drift、new attempt/output/partial preservation、age/status/size、
  source/workspace/outside/interrupted preservation、reparse/race/locked、CLI、job/queue/tombstone 与 retention config。

## Build、tests 与 smoke

- final Release build：
  `$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src/CSharpAiCli.sln -c Release --no-restore`
  -> 0 warnings，0 errors。
- new lifecycle tests：
  `dotnet test src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj -c Release --no-build --filter
  "FullyQualifiedName~ManagedArtifactLifecycleTests"` -> 17 passed，0 failed，0 skipped（最终新增数）。
- Project Pack/Job/Queue related regression 在新增 config case 前运行 -> 140 passed，0 failed，0 skipped；
  config/lifecycle regression 最终 -> 81 passed，0 failed，0 skipped。
- standard full 命令多次运行：
  `dotnet test src/CSharpAiCli.sln -c Release --no-build`。首轮 1250 passed/1 failed（既有 MCP doctor elapsed
  2.056s）；该项定向复跑 1 passed/505ms。第二轮 1249/2（既有 OfflineAgentRunner 1s timeout assertions），
  两项定向复跑 2 passed/190ms。后续标准 full 多轮均只有既有 MCP wall-clock assertion，elapsed 2.62-3.85s；
  priority run 还暴露 4 个既有 1-2s timing assertions。本周未修改 MCP/OfflineAgent implementation 或 assertion。
- 首次低并发 2 核 full 曾 1251/1251 通过（1m44s）；final package 后同配置的复验仍出现同一 MCP
  wall-clock failure（1250/1，elapsed 2.118s）。最终 full：
  `$env:DOTNET_PROCESSOR_COUNT='1'; dotnet test src/CSharpAiCli.sln -c Release --no-build`
  -> 1251 passed，0 failed，0 skipped，duration 3m20s。该变量只降低 .NET/xUnit host 并行度，不修改产品行为、
  test filter 或 assertion。
- validation package：
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -OutputRoot artifacts/week63-final-validation`
  -> success。版本仍为 0.4.0，因为 0.5.0 metadata 只能在 Week 65 release acceptance 更新；此包不是新的
  Accepted artifact，也未覆盖 `artifacts/release`。
- validation zip：56,150,685 bytes，SHA256
  `219D4A8F9BD98B1170359834CE6CE7C41E308279B7E5B9025F997C1319B48AC6`。
- validation exe：61,457,836 bytes，SHA256
  `229C582B783B4C73CF017E3DD23A34FC09BA26B9D26E21F5F3311F24F6D960AF`。
- Magick.NET notice：426,055 bytes，SHA256
  `453A9AF66E4458AE4C27BDA2BF62D0338AEBA5ABAF08CAB2FCEDC923E083C65A`。
- default packaged smoke：显式移除 real model、daemon、Gerber/TIFF tool opt-in/path variables 后运行
  `tools/Invoke-SmokeTests.ps1 -ExecutablePath artifacts/week63-final-validation/caicli-0.4.0-win-x64/caicli.exe`
  -> `smoke tests passed`。新增 default assertions 覆盖 artifact list/show/verify/export、ready accept/reject
  hard-gate、resume plan、prune dry-run 与 zero-candidate controlled apply；未启动 model/Gerbv/ImageMagick/network。
- real-tool smoke：**Skipped / Not Passed**。`CAICLI_GERBER_TIFF_TOOL_SMOKE`、`CAICLI_GERBV_PATH`、
  `CAICLI_IMAGEMAGICK_PATH` 均 unset；`gerbv.exe`/`magick.exe` 不在 PATH，也不在 retained validation artifacts。
  按 hard boundary 未自行下载、猜测路径或设置 bypass。real branch 已扩展为真实 hard verify -> accept、第二 run
  reject、double decision、artifact verify/export、terminal prune apply 与 source/workspace TIFF preservation，必须在
  Week 64 用冻结工具实际执行至少一次。
- cleanup：`caicli/gerbv/magick` process count 均为 0；`caicli-smoke-*`、`caicli-pack-probe-*`、
  `caicli-controlled-process-*`、`caicli-tiff-verification-tests-*`、`caicli-artifact-lifecycle-tests-*`、
  `caicli-restart-tests-*` temp directory count 均为 0。
- `git diff --check` 通过，仅报告仓库既有 LF -> CRLF warning。

## Accepted / Preview / Deferred

- Accepted：0.4.0 version、`artifacts/release` 与 final acceptance 均未改变。Week 63 validation package 不是
  Accepted release artifact。
- Preview（0.5.0 source only）：strict artifact manifest/index、list/show/verify/export、human accept/reject、
  stage-aware resume plan、manual stale recovery、new-run/new-output restart、retention config、dry-run/apply prune、
  quarantine/race/reparse protections、run/job/queue/attempt/tombstone correlation 和扩展 smoke contract。
- Gate Passed evidence：Week 58 tool/license/fixture 和 Week 61 typed execution、Week 62 real conversion/hard TIFF
  verification evidence不变；本周 default/local deterministic gate 通过，但未生成新的 real-tool evidence。
- Deferred：accept exception/undo、artifact restore/quarantine recovery、background retention/quota worker、shared/
  remote store、cloud upload、多用户 policy、scheduler、parallel writer、remote runner、provider routing、API
  control/SSE、team platform、marketplace、UI、ZIP/network input、complete CAM/EDA correctness 和 model vision gate。

## 风险

- Week 63 real-tool smoke 未执行；新增 real accept/reject/prune branch 只有脚本契约测试，Week 64 必须用 Week 58
  冻结身份实际运行，不能把 Week 62 结果或 default smoke当成本周真实验证。
- `run.json`、`checkpoint.json`、`artifact-manifest.json` 分别 atomic，但不是文件系统事务。进程在 rename 之间
  崩溃会 mismatch 并 fail closed，需要人工诊断。
- restart reservation 写入 parent 后、child create/job update 前崩溃可能留下 reserved-but-missing child；同一
  reservation 可在重验/重新审批后继续创建，但 corrupt/conflicting child 必须人工检查。
- prune 在 quarantine identity 验证、tombstone write、physical delete 之间崩溃可能留下 tombstone 加 quarantine
  file；v1 没有 restore/recovery command。Week 64 应增加 crash/disk-full/quarantine diagnostics。
- human actor 是 redacted local metadata，不是 authenticated identity 或多用户 authorization。同一 OS user 的
  恶意进程仍可竞争 state；C-AICLI 不是 sandbox。
- run decision/prune truth 可能已持久化而 correlated job update 失败；CLI 返回 failure，manifest/run evidence
  仍保留。Week 64 应加强 partial-write/corrupt job diagnostics，不反向回滚 human truth 或已安全删除内容。
- full-suite 多个既有 wall-clock tests 对高并发主机有明显 flake；最终低并发 full 通过，但 Week 64/65 应继续
  记录标准 full 与 controlled-concurrency full，不静默删除失败计数。

## Week 64 输入

- 用冻结 Gerbv/ImageMagick paths 设置 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1`，实际执行已扩展 real branch，记录
  两个 tool version/SHA256、fixture/output hashes、accept/reject、prune candidates/deleted bytes、source/
  explicit TIFF preservation、process/temp cleanup。未实际通过前不得进入 Week 65 Accepted decision。
- 固化 artifact manifest/acceptance/restart/prune text/JSON schema、error codes 和 exit policy；补 unknown field、
  truncated manifest、run/checkpoint/manifest split-write 与 correlation corruption tests。
- 补 disk-full/permission/quarantine-delete failure、restart reserved child crash、job update failure、symlink swap、
  manifest/tombstone mutation与 prune 多项 partial success adversarial tests。
- 对 accept actor/reason、restart output、artifact path、tombstone/job warning 做 secret-like filename/token/path
  redaction审计；JSON 不得直接序列化 raw exception。
- 复核 default smoke 文案：negative hard-gate 不写成 human accept/reject success，fake/file existence/metadata/
  preview 不写成 real business validation；real smoke与 real model smoke继续独立。
- 更新 quickstart、CHANGELOG candidate、runtime diagnostics、capability status 和 release process；Week 64 不新增
  第二 pack、scheduler/worker/remote/API/UI，不改变冻结 tool argv/verification semantics。
