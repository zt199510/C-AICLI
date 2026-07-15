# 第 61 周回顾

状态：已完成；Gerber/TIFF Controlled Real Conversion 为 0.5.0 source-only Preview，TIFF engineering verification 仍 Deferred

## 范围结论

- Week 58 固定的 Gerbv/ImageMagick 参数模板已实现为 pack-owned typed adapter。Gerbv 只使用
  `-x png -D 300 -o <managed-png> <staged-input>`；ImageMagick 只使用
  `<managed-png> -alpha off -colorspace sRGB -units PixelsPerInch -density 300 -compress LZW
  <declared-tiff>`。两者均通过 `ProcessStartInfo.ArgumentList` 启动，不调用 shell，也不接受 model、skill、
  workspace manifest、plan JSON、输入文件名或环境变量追加 flags。
- 非 dry-run `packs run gerber-tiff` 复用 Week 60 的 `ManagedProjectPackRunStore`、staging manifest、
  transition table、job correlation 和 artifact pointer。执行前重新验证 plan/input/tool/output/policy 和
  staged hashes；每个 mandatory tool 先执行当前 approval-gated fixed probe，每个 external invocation 再单独
  请求当前 approval。checkpoint 始终保持 `approvalPersisted=false`。
- execution risk summary 与实际 invocation 绑定 canonical tool path、SHA256、probe version、input path/hash、
  output path、timeout、operation、fixed template id 和 no-overwrite policy。tool identity 在 approval 前、
  approval 后和 process exit 后复核；identity drift fail closed。
- process runner 使用 absolute executable、typed ArgumentList、`UseShellExecute=false`、managed cwd/temp、
  cleared/allowlisted environment、bounded stdout/stderr、timeout/cancel、tree kill、Windows structured
  `taskkill` fallback、descendant/residual check 和 bounded output drain。staged inputs 在进程期间由 Windows
  read-share-only handle 锁定。
- 首个 managed render/output root 使用原子 create-new；declared output 使用 canonical containment、reparse
  protection、no-overwrite、non-empty/size/SHA256、unexpected file/directory 和 complete inventory checks。
  exit `0` 但 missing/empty/unexpected/oversized/stderr output 仍是 partial/failed evidence。
- 真实成功只流转 `ready -> running -> verifying`。`inspect` 和 `review` 保持 pending；不会提前进入
  `awaiting-acceptance`。run summary、job warning 和 execution log 均明确只接受 “conversion executed”，
  未宣称 TIFF engineering verification 或业务正确性通过。
- run 保存 redacted `logs/conversion-execution.json` 和 managed/workspace artifact pointers；job 复用普通
  artifact index，新增 conversion output/log kinds，`taskReport` 仍为 null，没有第二套任务报告真相。
- 新增稳定错误码覆盖 tool missing/version/identity、approval、timeout/cancel/failure/partial、output
  conflict/boundary/limit、cleanup/residual process。text/JSON run renderer 继续复用 Week 60 contract。
- default smoke 继续 model-free、network-free、real-tool-free。独立 real-tool smoke 仅由
  `CAICLI_GERBER_TIFF_TOOL_SMOKE=1`、`CAICLI_GERBV_PATH` 和
  `CAICLI_IMAGEMAGICK_PATH` 显式启用；opt-in 后路径缺失会失败，不伪造成功。

## 真实工具与 fixture 证据

本机起初未发现已安装 Gerbv/ImageMagick。为完成本周授权验证，按 Week 58 冻结 URL 下载到工作区外临时目录，
先验证 archive，再解压验证 executable；第三方文件未进入 Git 或 release，最终已删除临时工具目录。

| Artifact | Size | SHA256 |
|---|---:|---|
| `gerbv_v2.13.0_.Windows.MSYS2.UCRT64.zip` | 17,962,489 | `BB77865DA031DF83482196A6F6E9FA98298433979D995C068C4533B9B5A9EE7D` |
| `gerbv.exe` | 10,779,843 | `8BC29F799D0FD0CE522B489040E814F11B2B491E60E1E13803CBDE8C32621E4A` |
| `ImageMagick-7.1.2-27-portable-Q8-x64.7z` | 22,076,074 | `DE1B67753F86A838F41754FE3BCE168C9C7AE1BD16706777FF60A3C1915EABA9` |
| `magick.exe` | 31,147,184 | `86F7225B9A72D2FC71D284F078E392A6911E2CB1F7C106DEA8ECEE91B0608C57` |

实际 probe version：

```text
gerbv version v2.13.0-dirty
Version: ImageMagick 7.1.2-27 Q8 x64 b661ac9:20260705 https://imagemagick.org
```

授权输入：`src/CSharpAiCli.Tests/Fixtures/GerberTiff/real/minimal-square.gbr`，257 bytes，CC0-1.0，
SHA256 `D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54`。

手工 source-run 命令形状：

```powershell
dotnet <release-caicli.dll> packs plan gerber-tiff `
  --input input --output-dir output `
  --tool-path "gerbv=<gerbv.exe>" "imagemagick=<magick.exe>" `
  --output json --workspace <isolated-workspace>

dotnet <release-caicli.dll> packs run gerber-tiff `
  --plan plan.json `
  --tool-path "gerbv=<gerbv.exe>" "imagemagick=<magick.exe>" `
  --approval always --output json --workspace <isolated-workspace>
```

第一次真实 run 在 run/job 创建前因 mandatory probe 未满足而 fail closed：exit `1`、
`pack-execution-failed`，output files `0`、`gerbv/magick` process delta `0`。随后同参数独立
`packs doctor --probe --approval always` 两个 tool 均 `ready`，同一 plan 重跑成功。该瞬态未被删除或写成成功。

成功 run：`run_20260715T011111978Z_bdc257b9`，关联 job
`job_20260715T011111978Z_15dfd328`，terminal checkpoint 为 `verifying`：

| Evidence | Size | SHA256 |
|---|---:|---|
| staged/source Gerber | 257 | `D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54` |
| managed PNG intermediate | 471 | `850A2B4A555F2AE4060E6F541D8CE7E73D959C903000F7987C75D3DD4146D747` |
| declared workspace TIFF output | 1,122 | `FDDFA21D29EF870D94EC953ADF757BD61585957E1519D79BB2480AFF81C89823` |

成功 execution log 记录 Gerbv duration `95 ms`、ImageMagick duration `103 ms`、exit `0`、空
stdout/stderr、`processCleanedUp=true`、`residualProcessDetected=false`、`conversionExecuted=true` 和
`tiffVerificationPassed=false`。这些 size/hash 与 Week 58 frozen fixture evidence 一致，但本周不把一致性扩写为
TIFF verifier passed。

## 测试与验证

- 定向安全/runtime/CLI/smoke contract 最终相关批次：41 passed，0 failed，0 skipped；另有
  timeout/cancel/probe targeted 3 passed。覆盖 ArgumentList spaces/Unicode/metacharacters、approval denial、
  approval 后 tool swap、secret stderr redaction、missing/unexpected/oversized output、timeout、cancel、
  detached child、outside boundary、malicious success-without-output、run transition 和 smoke opt-in contract。
- 最终 build：
  `$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
  -> 0 warnings，0 errors。
- full test 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- 首轮 full：1220 passed，2 failed，0 skipped。失败为已知 MCP timeout wall-clock `3.133s`，以及本周
  750 ms PowerShell test PID 尚未创建；cleanup result 本身为 true。
- 第二轮 full：1220 passed，2 failed，0 skipped。失败为既有 MCP temp-directory cleanup race，以及本周
  secret-stderr output pipe 在全量负载下超过 3 秒 drain bound，正确返回 cleanup failure。
- 第三轮 full：1221 passed，1 failed，0 skipped。唯一失败是 Week 58 probe timeout test 的 1.5 秒
  PowerShell PID 启动窗口；加固为 timeout 有明确启动窗口、cancel 先观测 PID 再发 token。
- 第四轮 full：1221 passed，1 failed，0 skipped。唯一失败回到既有 MCP timeout wall-clock `2.778s`。
- 最终同命令 full rerun：1222 passed，0 failed，0 skipped。本周未修改 MCP implementation 或 MCP
  wall-clock assertion；上述失败没有从计数中静默删除。
- final validation package：
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
  -OutputRoot artifacts\week61-final-validation`。
- validation zip：44,601,603 bytes，SHA256
  `87F65E9093D4353678AAD459F27AB01C77D7C5992CD92C808539BCBB773A4209`。
- validation exe：49,947,722 bytes，SHA256
  `AA8493FF07EDFC94DC7555BB1011E4B2885A2A1D0987DB985E46331B8EB8645F`。
- final default smoke：显式移除 real model、daemon、Gerber/TIFF opt-in 与 tool path variables 后运行
  `tools\Invoke-SmokeTests.ps1 -ExecutablePath <final-validation-exe>` -> `smoke tests passed`；real tool 分支明确
  `skipped`。
- final real-tool smoke：设置三个 Gerber/TIFF opt-in/path variables 后运行同一 packaged smoke ->
  `smoke tests passed`，并打印上述 tool versions/SHA256、input hash、1,122-byte output hash，以及
  `TIFF engineering verification remains pending`。
- 最终 cleanup：`caicli/gerbv/magick` process count 均为 0；`caicli-smoke-*`、
  `caicli-pack-probe-*`、`caicli-controlled-process-*`、`caicli-pack-run-tests-*` temp count 均为 0；
  手工 real-run 与下载工具临时目录已在校验 absolute temp-root/name 后删除。
- `git diff --check` 通过，仅报告仓库既有 LF -> CRLF warning。

## Accepted / Preview / Deferred

- Accepted：0.4.0 release decision、版本号和 `artifacts/release` 均未改变；final validation package 不是新的
  0.4.0 Accepted artifact。
- Preview（0.5.0 source only）：fixed Gerbv/ImageMagick adapter、exact execution approval summary、bounded
  process/env/cwd/output、timeout/cancel/tree cleanup/residual check、real `packs run`、`verifying` checkpoint、
  redacted execution log、job/run/output pointers、stable errors 和 independent real-tool smoke。
- Gate Passed evidence：Week 58 license/tool/fixture decision 不变；Week 61 已用相同 exact tool/executable
  identities 对授权 CC0 fixture 形成真实 run/job/artifact evidence。
- Deferred：TIFF signature/metadata/content/baseline verification、preview/contact sheet、human accept/reject、
  execute restart decision、artifact verify/export/prune、ZIP/network input、repo hooks、scheduler、parallel/
  concurrent worker、lease/heartbeat、remote runner、provider routing、API control/SSE、team platform、
  marketplace 和 UI。

## 风险

- C-AICLI 仍不是 OS sandbox。安全边界依赖 explicit trusted tool path/hash/version、fixed argv、cleared env、
  managed cwd/temp、read-locked staging 和 declared output inventory；同一 OS user 的恶意进程仍可竞争本地文件。
- 首个 output root 在 Windows 原子 create-new；后续 declared file 在启动前执行 no-overwrite check。外部工具本身
  没有通用 create-new flag，因此同一用户在 check 与 tool create 之间仍存在有限 TOCTOU 风险。
- ImageMagick policy/delegate/module 行为仍受精确 portable artifact 影响；adapter 不继承相关环境，并固定
  `MAGICK_CONFIGURE_PATH`/`MAGICK_TEMPORARY_PATH`，但后续版本/hash 必须重新 plan/probe/review。
- 真实 smoke 只覆盖一个 single-layer CC0 fixture 和一条 single-output path；多 Gerber/drill input 的完整业务
  组合仍需后续 fixture 与 verifier evidence。
- workspace TIFF 是 explicit external output pointer，不属于 managed prune。Week 62 verifier 必须重新做
  canonical/reparse/size/hash checks，不能只信任 Week 61 pointer。
- run/checkpoint 双原子文件而非 filesystem transaction、MCP wall-clock flake 等 Week 60 风险继续存在。

## Week 62 Verifier 输入

- 成功 conversion run state 固定为 `verifying`；`inspect`/`review` stage 仍 pending，Week 62 verifier 负责在
  verification 通过后进入 `awaiting-acceptance`，不能由 conversion adapter直接跳转。
- TIFF pointer：kind `tiff-output`、scope `workspace-output`、workspace-relative `.tiff` path、size、SHA256。
  managed PNG intermediate：kind `render-intermediate`、scope `managed-run`、nested artifact path、size、SHA256。
- managed execution log schema `gerber-tiff.conversion-execution` v1 包含 tool filename/version/SHA256、input/
  output hashes、fixed template id、approval status、exit/duration、bounded/redacted stdout/stderr 和 cleanup flags；
  不含 absolute tool path、raw argv、raw input 或 approval grant。
- Week 62 必须从 run/store 读取并重新验证 TIFF pointer 与 file identity，使用 Week 58 成熟 TIFF library/tool
  做 bounded signature/metadata/content/baseline verification；`conversionExecuted=true`、TIFF existence、
  exact Week 58 hash、metadata valid 或 preview 任一项均不能单独建立更高 verification level。
