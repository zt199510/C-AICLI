# 第 62 周回顾

状态：已完成；TIFF Artifact Inspection、Preview 与 Verification 为 0.5.0 source-only Preview，human acceptance 仍 Deferred

## 范围结论

- 冻结 classic TIFF v1 hard-verification contract：little/big-endian classic TIFF、LZW、8-bit RGB、
  3 samples/pixel、no alpha、PixelsPerInch、undefined/top-left orientation、1-32 frames。BigTIFF、其他
  compression/pixel format 和 alpha-bearing output 均以稳定 unsupported diagnostic fail closed。
- 冻结 resource limits：256 MiB/file、32,768 pixels/dimension、100,000,000 pixels/frame、
  128,000,000 total pixels、32 frames、512 MiB decoded RGBA memory、15 seconds/decode、
  2,048 pixels/preview edge、4 MiB/report 和 2 MiB/baseline manifest。
- 使用 `Magick.NET-Q8-x64 14.15.0` 作为 Week 58 已验证 ImageMagick 7.1.2-27 的 managed binding。
  verifier 不手写 TIFF parser，不执行用户选择的 executable，不调用 shell/model/network。ImageMagick
  global resource policy 与 temp directory 在进程内串行锁下设置；disk pixel cache 为 0，thread 为 1。
- 新增 `packs verify <run-id> [--baseline <workspace-file>]` text/JSON。verifier 重新验证 run/plan/input
  manifest、source/staging、完整 declared output name/count、canonical/reparse boundary、file size/SHA256、
  TIFF signature、decode metadata、unexpected output 和 decode 前后 mutation。
- verification levels 固定为 `file-valid`、`metadata-valid`、`content-compared`、
  `human-review-required`。每层独立；无 baseline 时 content 是 `not-requested`，不会伪造 compared。
- baseline schema v1 严格拒绝 unknown field/version、URL/network/outside path 和 identity mismatch。
  exact SHA256 只允许 `byteDeterministic=true`；pixel compare 固定为 decoded sRGB RGBA8、visual top-left、
  straight alpha、explicit max channel delta + max different pixel count，并记录 observed delta/count 与两侧
  normalized pixel SHA256，不输出模糊 similarity score。
- stable verification JSON/markdown 写入 `reports/verification-rNNNN.{json,md}`，create-new/no-overwrite；
  hard pass 才允许 `verifying -> awaiting-acceptance`，hard failure 进入 `failed`。任何 Week 62 路径都不进入
  `accepted`。
- 新增 `packs preview <run-id>`，只允许 hard verification 后运行。single-page 生成 bounded PNG，multipage
  生成 bounded contact sheet；路径稳定、managed-only、no-overwrite，不自动打开、不上传，result 固定
  `correctnessProof=false`。
- verification/preview/report pointers 追加到同一 run/job artifact index；job `taskReport` 仍为 null，没有第二套
  report truth。run/checkpoint 每次更新仍固定 `approvalPersisted=false`。
- release build 复制精确 NuGet package 的 `Notice.txt` 为
  `THIRD-PARTY-NOTICES-MAGICK.NET.txt`。缺失 notice 时 build fail closed；manifest 显式列出 notice。

## Verification 与 preview 证据

定向 Week 62 tests 最终为 12 passed、0 failed、0 skipped，覆盖：

- actual managed codec single-page/multipage LZW RGB TIFF decode 与 normalized RGBA pixels；
- invalid signature、truncated/corrupt、sparse oversized、BigTIFF、unsupported compression/pixel format；
- output mutation、unexpected output、outside baseline、strict unknown fields、unproven exact hash；
- exact mismatch、pixel exact pass、pixel tolerance mismatch/observed values、controlled timeout diagnostic；
- deterministic/redacted JSON/markdown、independent levels、preview/contact sheet/no-overwrite；
- `packs verify/preview` JSON、run transition、job/report/preview pointers 和 null `taskReport`。

相关 build/release/smoke contract batch 为 19 passed、0 failed、0 skipped。

真实 opt-in smoke 使用 Week 58/61 相同 tool/fixture identity：

| Evidence | Size | SHA256 |
|---|---:|---|
| Gerbv archive | 17,962,489 | `BB77865DA031DF83482196A6F6E9FA98298433979D995C068C4533B9B5A9EE7D` |
| `gerbv.exe` | 10,779,843 | `8BC29F799D0FD0CE522B489040E814F11B2B491E60E1E13803CBDE8C32621E4A` |
| ImageMagick archive | 22,076,074 | `DE1B67753F86A838F41754FE3BCE168C9C7AE1BD16706777FF60A3C1915EABA9` |
| `magick.exe` | 31,147,184 | `86F7225B9A72D2FC71D284F078E392A6911E2CB1F7C106DEA8ECEE91B0608C57` |
| authorized source Gerber | 257 | `D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54` |
| real TIFF output | 1,122 | `FDDFA21D29EF870D94EC953ADF757BD61585957E1519D79BB2480AFF81C89823` |

实际 probe version：

```text
gerbv version v2.13.0-dirty
Version: ImageMagick 7.1.2-27 Q8 x64 b661ac9:20260705 https://imagemagick.org
```

真实 TIFF 在 packaged smoke 中通过 `file-valid` 与 `metadata-valid`；没有传 baseline，因此
`content-compared=not-requested`。run 从 `verifying` 进入 `awaiting-acceptance`，没有进入 Accepted。
随后生成 exactly one non-empty managed PNG preview，preview JSON 明确
`correctnessProof=false`、`automaticallyOpened=false`、`automaticallyUploaded=false`。outer smoke 在断言
run/job/report/PNG pointers 后清理隔离 profile，因此该次 preview/report 的单独 size/hash 未保留在外层日志；
不把“preview 可生成”写成真实内容/baseline verification。

Baseline exact/pixel pass/fail 由 synthetic TIFF/run fixtures 覆盖，不依赖外部工具、模型或网络。
这证明 deterministic verifier contract，不是新的真实 PCB 制造正确性证据。

## Build、tests 与 smoke

- final build：
  `$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
  -> 0 warnings，0 errors。
- full tests：`dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- 第一轮 full：1233 passed，1 failed，0 skipped。唯一失败是既有
  `McpDoctorReportTests.Create_reports_stdio_initialize_timeout_as_unavailable` wall-clock assertion，
  elapsed `3.5707908s`。
- 该 MCP test 定向重跑：1 passed，0 failed，duration 494 ms。本周未修改 MCP implementation/assertion。
- 第二轮 full：1233 passed，1 failed，0 skipped；同一既有 MCP wall-clock assertion，elapsed
  `2.6976172s`。
- 第三轮同命令 full：1234 passed，0 failed，0 skipped。两次失败没有从 review 计数中删除。
- 最终自查修正了 outside-baseline level independence、unexpected reparse-directory enumeration 与 preview
  directory reparse checks。修正后定向首轮 11 passed、1 failed；失败断言发现 content level 仍被错误提升。
  修正计算后同一批 12 passed、0 failed、0 skipped。
- 自查修正后第一轮 full：1232 passed、2 failed、0 skipped；均为既有 MCP wall-clock assertions：doctor
  `2.5865072s` 与 stdin-not-read `2.9329416s`。两项定向重跑 2 passed、0 failed，duration 604 ms。
- 随后 full：1233 passed、1 failed（doctor `2.6541293s`）；下一轮仍 1233 passed、1 failed（doctor
  `2.6554210s`）；最终同命令 full：1234 passed、0 failed、0 skipped。本周不修改 MCP timing behavior。
- 第一次重建 validation package 时 `dotnet publish` 因旧 exe file lock 返回非零，暴露
  `Build-Release.ps1` 未检查 native exit code、仍打印 artifact path 的缺口。该输出未计为成功，也未保留。
  build script 已增加 publish non-zero fail-fast，定向 release script tests 6 passed、0 failed。
- final validation package：
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
  -OutputRoot artifacts\week62-final-validation-v2`。
- validation zip：56,098,761 bytes，SHA256
  `2B277ACE2F2D43E10BBBCC6EDC988EA9A3AAC97DA6E1F8AE92CB2B84DB523BC5`。
- validation exe：61,414,394 bytes，SHA256
  `8E5563B33F10E6FDA5CBC16366B36DC75E47144EB536209634EFAFB8051994C3`。
- Magick.NET notice：426,055 bytes，SHA256
  `453A9AF66E4458AE4C27BDA2BF62D0338AEBA5ABAF08CAB2FCEDC923E083C65A`。
- default packaged smoke：显式移除 real model、daemon、Gerber/TIFF opt-in/path variables 后运行
  `tools\Invoke-SmokeTests.ps1 -ExecutablePath <validation-exe>` -> `smoke tests passed`；real-tool branch
  明确 skipped。default fake/dry-run `ready` checkpoint 的 `packs verify` 被稳定拒绝且未生成 report/preview。
- real-tool packaged smoke：设置 `CAICLI_GERBER_TIFF_TOOL_SMOKE=1`、`CAICLI_GERBV_PATH`、
  `CAICLI_IMAGEMAGICK_PATH` 后运行同一脚本 -> `smoke tests passed`；真实 conversion、TIFF metadata
  verification、managed preview 与 awaiting-acceptance assertions 全部通过。
- 首次 `Invoke-WebRequest` 下载在 246.7s 因连接关闭失败且未留下 archive。随后 bounded curl retry
  最终成功；过程中出现 DNS retry diagnostics，但 exact archive/executable size/hash Gate 均通过后才执行。
  v2 real-smoke 重验时 GitHub ImageMagick 下载先后因 timeout/reset 失败，官方 archive URL 对历史文件返回
  404；最后只把 `ghfast.top` 用作 GitHub transport proxy，下载内容仍通过冻结 size/SHA256 与 executable
  SHA256 后才执行，没有信任 proxy metadata 或改用最新版本。
- cleanup：`caicli/gerbv/magick` process count 为 0；`caicli-smoke-*`、`caicli-pack-probe-*`、
  `caicli-controlled-process-*`、`caicli-tiff-verification-tests-*` temp count 均为 0；工作区外临时工具目录
  在校验 absolute temp root/name 后删除。作废的 `artifacts/week62-final-validation` 已删除，只保留 v2。
- `git diff --check` 通过，仅报告仓库既有 LF -> CRLF warning。

## Accepted / Preview / Deferred

- Accepted：0.4.0 version、release decision 和 `artifacts/release` 均未改变；Week 62 validation package
  不是新的 0.4.0 Accepted artifact。
- Preview（0.5.0 source only）：bounded Magick.NET TIFF decoder、declared output/source/staging revalidation、
  strict baseline、metadata/exact/pixel comparison、stable verification JSON/markdown、managed PNG/contact
  sheet、`packs verify/preview`、`verifying -> awaiting-acceptance` hard gate、run/job/report/preview pointers。
- Gate Passed evidence：Week 58/61 external tool/fixture identity 不变；真实 controlled TIFF 已通过 Week 62
  file/metadata verification 并生成 managed preview，human acceptance 仍 pending。
- Deferred：human accept/reject、baseline create/update command、complete CAM/EDA manufacturing correctness、
  model vision hard decision、execute restart、artifact list/show/verify/export/prune、ZIP/network input、repo hooks、
  scheduler、parallel/concurrent worker、lease/heartbeat、remote runner、provider routing、API control/SSE、team
  platform、marketplace 和 UI。

## 风险

- C-AICLI 仍不是 OS sandbox。source/TIFF/baseline 只读 handles、workspace/reparse checks 和 hashes 降低
  风险，但同一 OS user 的恶意进程仍可竞争本地路径。
- Magick.NET/ImageMagick 是 native third-party attack surface。固定 format/resource limits 与 managed temp
  不证明 codec 无漏洞；package/version 变化必须重新做 notice、vulnerability、format 和 limit review。
- ImageMagick `ResourceLimits.Time` 是 in-process native codec timeout，不是可 kill 的独立 process。
  已知 resource-limit error 可稳定失败，但底层 native deadlock 仍不能由 managed caller做 process-tree cleanup。
- ImageMagick resource limits 与 temp path 是 process-global，当前用串行锁保护。0.5.0 不支持并行 TIFF
  verifier；未来并发前必须重新设计 isolation，而不是移除锁。
- 无 baseline 的 metadata hard pass 可以进入 `awaiting-acceptance`，但 content level 明确是
  `not-requested`。Week 63 human gate 不得把它重写成 content-compared 或制造正确性通过。
- verification report 先 create-new、后原子更新 run/checkpoint。进程在两者之间崩溃可能留下未索引的
  managed report，no-overwrite 会阻止静默重写；自动 recovery/repair 仍 Deferred。
- Magick.NET 增加 validation package size 与 notice 内容；Week 65 package/checksum/reproducibility 必须以新
  dependency 为基线，不得沿用 0.4.0 artifact size/hash。

## Week 63 输入

- `verifying -> awaiting-acceptance` 只能由 `hardVerificationPassed=true` 建立；`failed` run 不可 accept。
- verification JSON/markdown artifact kinds：`tiff-verification-json`、
  `tiff-verification-markdown`；preview kinds：`tiff-preview`/`tiff-contact-sheet` 与
  `tiff-preview-report`。全部是 managed-run scope、size/SHA256、no-overwrite pointer。
- workspace TIFF 仍是 external workspace-output pointer，不属于 managed prune。Week 63 prune 只能删除
  managed reports/previews/intermediates，不能删除 source、baseline 或 explicit workspace output。
- accept/reject 必须引用当前 run revision、verification report identity、review note/actor/time，并保持
  `taskReport=null`；preview availability 不能成为 hard acceptance proof。
- resume/restart 仍不得复用 approval。任何重新执行 external conversion 必须重新验证 tool/input/output/
  policy 并重新 approval；`running`/`interrupted` 不自动 replay。
- artifact list/show/verify/export/prune 应复用现有 run/job artifact pointers、workspace guard、reparse
  protection、redaction、dry-run/no-overwrite，不新增 scheduler、worker、remote/API/UI surface。
