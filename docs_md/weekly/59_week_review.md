# 第 59 周回顾

状态：已完成；Gerber/TIFF discovery/preflight/plan 为 0.5.0 source-only Preview，真实转换仍 Deferred

## Gate 与范围结论

- Week 58 Toolchain Gate 已通过；本周复用 Gerbv 2.13.0、ImageMagick 7.1.2-27、LibTIFF 4.5.1、
  CC0 fixture 和固定 typed argv 结论，没有实现新的 Gerber/Excellon/TIFF parser。
- 本周只实现 workspace 内显式目录的 bounded discovery、静态/tool-probe preflight 和 deterministic
  conversion plan。没有 conversion、staging、job/run、queue、checkpoint、TIFF verification、preview、
  artifact lifecycle、resume 或 accept/reject。
- `packs plan` 不调用模型/MCP、外部转换工具或网络，不写 workspace，不创建 output/job/run/session/report。
  `packs doctor --probe` 仍是唯一允许启动领域外部工具的路径，并继续经过当前 approval。
- Project Pack 仍是确定性 pack-owned 领域流程，不是 skill。模型、prompt、workspace manifest、repo hook
  和环境 extra args 均不能生成或扩展真实 executable/argv/cwd/env/output。

## 冻结 input envelope

- Gerber：`.gbr .ger .gbx .gtl .gbl .gts .gbs .gto .gbo .gm1`，大小写不敏感。
- Excellon：`.drl .xln`；报告级 sidecar：`.gbrjob`。
- required group：至少一个 Gerber layer；drill、sidecar 和具体 top/bottom copper、mask、silkscreen、
  outline 角色均 optional。
- `.gbrjob` 只进入 metadata/hash/fingerprint，不解析 raw content，也不传给外部工具。unknown 文件只列出
  relative path/kind/size warning，不读取/哈希内容，不进入工具输入或 fingerprint。
- bounds：depth `8`、files `256`、directories `256`、single supported file `67108864` bytes、
  total supported bytes `536870912`、workspace-relative path `512` characters、scan+hash `10000 ms`。
- URL、UNC/network、device path、glob、parent traversal、workspace escape、reparse/symlink tree、单文件输入、
  不可读/变化文件、case/canonical collision、duplicate/ambiguous layer、missing Gerber 和所有 limit overflow
  均产生 stable diagnostic；不能静默截断后生成 runnable plan。

## Plan schema 与 fingerprint

- 通用 `schemaVersion`：`1`；领域 `planSchema`：`gerber-tiff.plan.v1`。
- JSON 顶层 `type`：`packs.plan`；状态为 `blocked`、`ready-for-staging` 或 `runnable`。
- `--input` 和 `--output-dir` 是路径输入；`--output` 仅选择 `text|json`。output 必须位于 workspace、
  不在 input tree 内且尚不存在；plan 只验证，不创建目录，也没有 overwrite flag。
- inventory 按 ordinal workspace-relative path 稳定排序；supported input 保存 kind、layer role、size、SHA256
  和 `passedToExternalTool`。哈希使用 128 KiB streaming buffer，并在读取前后复核 size/mtime。
- fingerprint canonical payload 包含 schema/pack version、relative input/output、limits、allowlist、sorted
  supported identities、static tool identities、fixed stages/timeouts/restart policy 和 expected artifact slots；
  不包含 absolute path、mtime、unknown content、raw input、raw argv、diagnostic text、secret 或 approval。
- planned external stages 保持 `requiresApproval=true`、`manual-reapproval`；render/encode/inspect timeout
  分别为 `30000/30000/15000 ms`。expected artifact kinds 为 input inventory、managed PNG intermediate、
  explicit workspace TIFF output 和 inspection report。
- `readyForStaging=true` 只表示 input/output 已安全冻结；`runnable=true` 还要求 required static tool
  identities 可用，但仍不授权执行。所有 Week 59 plan 固定 `conversionExecuted=false`、
  `executionAuthorized=false`、`approvalPersisted=false`。
- 对仓库 CC0 fixture 的 packaged read-only sample：exit `1`、status `ready-for-staging`、
  plan id `gerber-tiff-008bdf9908f9116e`、fingerprint
  `008BDF9908F9116E0B5724283E88BDA2E449E363A9F516D93FA65887815E64A5`；inventory 为 3 files、
  1 supported、2 unknown。因未配置真实 tool identity，`runnable=false`；output directory 未创建。

## CLI 与 smoke

- 新增 `packs plan gerber-tiff --input <dir> --output-dir <new-dir> [--tool-path ...] [--output text|json]`。
- plan 的 `--tool-path` 只做 static regular-file/name/reparse/size/SHA256 inspection，不提供 `--probe`、
  `--approve` 或任意 extra args。
- default packaged smoke 新增 credential-free `packs list` text/JSON、static doctor JSON 和 plan JSON。
  smoke 预期无工具 plan 为 `ready-for-staging`/not runnable，并检查 raw content、absolute path、output、
  job 和 pack-run state 均未产生。
- `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` 本周仍不执行转换；真实工具 smoke 等 Week 61 adapter 后单独接入。

## 测试与验证

- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-restore
  --filter "FullyQualifiedName~GerberTiff|FullyQualifiedName~ProjectPack|FullyQualifiedName~SmokeTestScriptTests"`
- 最终定向结果：49 passed，0 failed，0 skipped。
- 新增 11 tests；覆盖 normal/mixed-case/unknown/sidecar、duplicate/ambiguous/missing、depth/file/byte/path/
  time、locked/outside/reparse、fingerprint determinism/tool hash change、no-overwrite、text/JSON 和无持久状态。
- build 命令：`$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build
  src\CSharpAiCli.sln -c Release`
- build 结果：0 warnings，0 errors。
- full test 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- full test 结果：1191 passed，0 failed，0 skipped；比 Week 58 的 1180 增加 11。
- package 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
  -OutputRoot artifacts\week59-validation`
- validation zip：44518999 bytes，SHA256
  `E084FA9613ECCD2B910A23C1287D5452BEAE369E5C4D7007899D268CB0337F9F`。
- validation exe：49879413 bytes，SHA256
  `0B474E8958862679F54130AE8F6C0C82885B21F99A0F7BA198F82CF7C465E92B`。
- default smoke：显式移除 `CAICLI_REAL_MODEL_SMOKE`、`CAICLI_DAEMON_SMOKE` 和
  `CAICLI_GERBER_TIFF_TOOL_SMOKE` 后，对上述 packaged exe 运行
  `tools\Invoke-SmokeTests.ps1 -ExecutablePath <validation-exe>`。
- smoke 结果：`smoke tests passed`；real model、daemon/API 和 Gerber/TIFF tool 分支均明确 skipped。
- cleanup：`caicli/gerbv/magick` process count 均为 `0`；`caicli-smoke-*` 和
  `caicli-pack-probe-*` temp directory count 均为 `0`；`git diff --check` 通过，仅有既有 LF -> CRLF warning。
- validation package 是本周 source Preview smoke evidence，不是新的 0.4.0 accepted artifact，也未覆盖
  `artifacts/release` 中的既有 0.4.0 文件。

## Accepted / Preview / Deferred

- Accepted：0.4.0 release decision 不变；本周没有把 source Preview 写入 0.4.0 Accepted。
- Preview（0.5.0 source only）：冻结 input envelope、canonical bounded discovery、classifier、streaming
  inventory hash、static preflight、approval-gated probe 复用、deterministic plan、text/JSON CLI 和 packaged
  credential-free smoke。
- Gate Passed evidence：Week 58 的 Gerbv/ImageMagick/LibTIFF、CC0 fixture、固定 argv 与许可结论不变。
- Deferred：真实 `packs run`、managed staging/checkpoint、Gerbv/ImageMagick adapter execution、TIFF
  validation/preview/baseline、queue/job/artifact/report integration、resume/restart、accept/reject、prune、
  real-tool smoke、ZIP/network input、repo hooks、scheduler、parallel/remote worker、provider routing、API
  control/SSE、team platform、marketplace 和 UI。

## 风险

- extension/filename layer mapping 只是 deterministic metadata，不证明 RS-274X/Excellon 内容或 PCB
  制造语义正确；Week 61/62 必须检查 Gerbv loaded/error output 和 TIFF content/baseline。
- static identity 只证明当前路径的 filename/size/hash；任意同名文件可形成静态 identity。因此
  `runnable` 不是 trust 或 execution approval，Week 60/61 必须使用当前 policy 重新验证 known tool hash、
  probe/version、input/output 和 approval。
- plan 后仍有 TOCTOU：input、tool 或 output target 可以变化。staging/run 必须重算 fingerprint/hash，
  output 已出现时拒绝，不能复用旧 approval 或自动 replay external stages。
- 10 秒限制在本地目录枚举/stream read 的操作边界检查；单次 OS filesystem call 仍依赖本地文件系统
  返回。v1 通过拒绝 network/reparse input 降低风险，但不是 OS sandbox。
- generic `.gbr/.ger/.gbx` 可以保持 `generic-gerber`；有冲突提示时 hard fail。Week 60 不得让模型补 layer
  mapping 或扩展 argv。
- `.gbrjob` 本周只做 hash evidence，不解析 schema。unknown 文件变化按设计不改变 conversion fingerprint。

## Week 60 冻结输入

- 直接复用 `GerberTiffInputInventory`、`GerberTiffConversionPlan`、`ProjectPackPlan`、stable diagnostic code、
  `schemaVersion=1` 和 `planSchema=gerber-tiff.plan.v1`；不要建立第二套 inventory/fingerprint truth。
- managed run/staging 必须引用 plan id/fingerprint，并在复制前后重新验证每个 supported source 的
  relative path、size、SHA256、kind、layer role 和 sidecar/tool-input flag。
- source input 保持 read-only；每次 run 使用新的 managed directory；explicit workspace output 仍
  no-overwrite，不能被 prune 删除。
- checkpoint 只保存 evidence/state，不保存 approval bypass、absolute portable tool path、raw input、raw argv
  或 secret。resume/restart 必须重新验证 tool/input/output/policy 并重新申请 approval。
- Week 60 只建立 staging/checkpoint/cancel/resume 基础和 fake driver stage flow；真实 Gerbv/ImageMagick
  conversion 仍留给 Week 61，且不得把 fake、metadata、file existence、preview 或 fingerprint 单独标为
  真实业务验证通过。
