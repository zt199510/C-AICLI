# 第 58 周回顾

状态：已完成；Project Pack/Toolchain Gate Passed，真实执行仍 Deferred

## Gate Decision

Week 58 Gate 通过。一个真实 Windows 工具链已对仓库内合法 fixture 完成
Gerber -> PNG -> TIFF 最小转换，并由独立 TIFF 工具复核 metadata：

```text
Gerbv 2.13.0
-> ImageMagick 7.1.2-27 Q8 x64
-> LibTIFF tiffinfo 4.5.1
```

该结论只接受工具候选、fixture、许可、固定 argv、身份/probe 和 contract 可行性。
它不接受真实 `packs run`、staging、TIFF verification、artifact lifecycle 或业务转换能力。
fake driver、文件存在、metadata valid、preview 和 exit code 均未被单独写成真实验证通过。

## 0.4.0 起点

- Week 58 开始时 `git status --porcelain=v1` 为空。
- 当前起点 HEAD：`34fcc32bf90cc6204a0dbb1005dbf5b5c91b8bf3`；该提交只在 0.4.0 后增加 Week 58-65 计划/执行提示。
- 可引用的 0.4.0 release commit：`5254707`（`发布：完成 CLI 0.4.0 验收`），是当前 HEAD 的祖先。
- 起点 build：`dotnet build src\CSharpAiCli.sln -c Release`，0 warnings，0 errors。
- 起点 tests：`dotnet test src\CSharpAiCli.sln -c Release --no-build`，1147 passed，0 failed，0 skipped。
- 起点 default smoke：移除 `CAICLI_REAL_MODEL_SMOKE`、`CAICLI_DAEMON_SMOKE` 和
  `CAICLI_GERBER_TIFF_TOOL_SMOKE` 后运行 `tools\Invoke-SmokeTests.ps1`，输出
  `smoke tests passed`；daemon/API 和 real model 分支跳过，结束后无残留进程/临时目录。

0.4.0 acceptance 文档记录的已验收 zip 是 `44422661` bytes、SHA256
`FC83EF0D05347B59DE1E1454FE45625E3CD6AA789F0A8C2BB3ACA0A1473E61CA`。
Week 58 起点工作区中现存 zip 实测为 `44422606` bytes、SHA256
`0EE8D2983AEF35322080CC54D290970BF29DA9FA6109A3CAEE2AC6A88789B0EC`，与已验收记录不同；
本周未把这个现存文件误写为 0.4.0 accepted artifact，也未重做 0.4.0 release。

## 真实工具证据

### Gerbv

- 上游 release：`v2.13.0` Windows MSYS2 UCRT64 portable ZIP。
- ZIP：`17962489` bytes，SHA256
  `BB77865DA031DF83482196A6F6E9FA98298433979D995C068C4533B9B5A9EE7D`，与上游 digest 一致。
- `gerbv.exe`：`10779843` bytes，SHA256
  `8BC29F799D0FD0CE522B489040E814F11B2B491E60E1E13803CBDE8C32621E4A`，未签名。
- Probe argv：`["--version"]`；exit `0`；stdout 首行
  `gerbv version v2.13.0-dirty`；stderr 空。
- 转换 argv：`["-x","png","-D","300","-o",<managed-png>,<fixture>]`。
- 正例：exit `0`，duration `1076 ms`，stdout/stderr `0` bytes；PNG 为
  126 x 126、24-bit RGB、471 bytes，SHA256
  `850A2B4A555F2AE4060E6F541D8CE7E73D959C903000F7987C75D3DD4146D747`；重复输出 hash 相同。
- 非法 README 输入：exit 仍为 `0`，stderr 报 `Unknown file type` 与 `loaded 0`，仍生成
  87-byte PNG。这证明退出码与文件存在不能单独判定成功。
- 真实 no-export 进程分别在 750 ms timeout 和显式 cancel 后终止，observed exit `1`，
  `gerbv` process count 最终为 `0`。

### ImageMagick / TIFF

- 上游 release：`7.1.2-27` portable Q8 x64。
- 7z：`22076074` bytes，SHA256
  `DE1B67753F86A838F41754FE3BCE168C9C7AE1BD16706777FF60A3C1915EABA9`，与上游 digest 一致。
- `magick.exe`：`31147184` bytes，SHA256
  `86F7225B9A72D2FC71D284F078E392A6911E2CB1F7C106DEA8ECEE91B0608C57`。
- TIFF argv：PNG 输入、`-alpha off -colorspace sRGB -units PixelsPerInch -density 300
  -compress LZW`、managed TIFF 输出。
- 正例：exit `0`，duration `1082 ms`，stdout/stderr `0` bytes；TIFF 为 single-page、
  126 x 126、8-bit/sample RGB、3 samples/pixel、LZW、300 x 300 DPI、1122 bytes，SHA256
  `FDDFA21D29EF870D94EC953ADF757BD61585957E1519D79BB2480AFF81C89823`；间隔超过一秒的两次输出 hash 相同。
- two-page TIFF：2260 bytes，SHA256
  `69CA9004C2652AEB9F77D0C3FC149026FDCF829778A2AFC47E30590472226910`；
  ImageMagick 报 pages `2`，LibTIFF 报 directory `0/1`，每页 metadata 一致。
- `tiffinfo.exe` 4.5.1：SHA256
  `F276E7717CB15A811CD083A393079B7D125F6DA49C6354E3A7F2099BB1BAA321`；
  使用 `-M 64` 做 bounded 独立复核。

## 许可与 fixture

- Gerbv：GPL-2.0-or-later。允许按 GPL 条件再分发，但需履行二进制/对应源代码、版权和许可义务；
  0.5.0 默认不捆绑，由用户显式配置路径。
- ImageMagick：ImageMagick License，允许商业使用和再分发但要求保留 license/attribution；
  0.5.0 默认仍不捆绑。
- LibTIFF：HPND-style license，本周仅作为独立 metadata 复核工具，不作为默认依赖。
- Fixture：`src/CSharpAiCli.Tests/Fixtures/GerberTiff/real/minimal-square.gbr`，项目原创、
  fixture 级 CC0-1.0、257 bytes，SHA256
  `D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54`。
- 预期工具版本、PNG/TIFF hash 与 metadata 在同目录 `expected-metadata.json`；生成图像未提交。

## 已完成实现

- 冻结 Project Pack v1 通用 manifest/capability/dependency/tool identity/plan/stage/artifact/
  diagnostic contract；通用 DTO 无 Gerber/TIFF 专属字段和用户机器 absolute path。
- Gerber/TIFF manifest 声明 Gerbv/ImageMagick dependency、ordered stages 和 logical artifacts；
  所有 external stage 强制 approval + `manual-reapproval`。
- registry 拒绝 unsupported schema、duplicate id、unknown capability/dependency/artifact 和不安全 stage。
- 新增 `packs list`、`packs list --output json`、静态 `packs doctor` 和显式 `--probe`。
- 静态 doctor 不启动进程；检查 filename allowlist、regular file、reparse chain、size/hash/trust。
- probe 使用 typed `ProcessStartInfo.ArgumentList`、当前 shell-risk approval、前后双 hash、minimum
  version、isolated temp cwd、allowlisted env、bounded output/time/cancel/tree cleanup 和可选 trace。
- approval/trust 不持久化；无 `run/resume/accept/reject` 实现。
- 定义 protocol-v1 fake driver 与 success/failure/partial-output/timeout fixture；fake 不进入 release。

## 验证

- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-build
  --filter "FullyQualifiedName~ProjectPack|FullyQualifiedName~GerberTiff"`
- 定向结果：37 passed，0 failed，0 skipped。
- 最终命令：`$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build
  src\CSharpAiCli.sln -c Release`
- 最终结果：0 warnings，0 errors。
- 最终命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 第一次最终全量：1179 passed，1 failed，0 skipped；唯一失败是既有
  `McpDoctorReportTests.Create_reports_stdio_initialize_timeout_as_unavailable` wall-clock 断言在并行负载下
  测得 `2.2837820 s`，超过 `< 2 s` 上限，MCP timeout 内容断言本身通过。
- 该既有测试定向重跑：1 passed，0 failed，duration 506 ms；未修改 Week 58 范围外的 MCP 行为。
- 第二次最终全量：1180 passed，0 failed，0 skipped；比起点新增 33 tests。
- 最终 default smoke：移除 real model、daemon 和 Gerber/TIFF tool opt-in 后运行
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`。
- Smoke 结果：`smoke tests passed`；daemon/API 与 real model 跳过。Week 58 未实现或运行
  real-tool smoke branch；真实转换证据来自上面的独立 spike。
- 最终 cleanup：`caicli/gerbv/magick/curl` process count 均为 `0`；
  `caicli-smoke-*` 与 `caicli-pack-probe-*` temp directory count 均为 `0`。
- `git diff --check` 通过，仅报告仓库既有 LF -> CRLF warning。

## Accepted / Preview / Deferred

- Accepted：0.4.0 release decision 不变；本周没有把新能力写入 0.4.0 Accepted。
- Preview（0.5.0 source only）：Project Pack v1 contract/registry、`packs list`、静态 doctor、
  approval-gated fixed version probe、fake-driver test protocol。
- Gate Passed evidence：Gerbv/ImageMagick/LibTIFF 候选、CC0 fixture、固定 argv、真实输出与许可结论。
- Deferred：real `packs run`、bounded input discovery implementation、queue/job integration、staging、
  checkpoint、TIFF verification/preview report、resume/restart、artifact verify/export/prune、human
  accept/reject、real-tool smoke、repo hooks、scheduler、parallel/remote worker、provider routing、
  API control/SSE、team platform、marketplace 和 UI。

## 风险

- Gerbv Windows artifact 未签名且自报 `v2.13.0-dirty`；必须同时使用 path metadata、hash 和 probe，
  版本字符串不能单独建立 trust。
- 工具许可允许有条件再分发不等于应默认捆绑；Week 65 若改变决定，必须重新完成 artifact-specific
  notices/source/security review。
- 当前 trust 只在 invocation 内观察，不持久化。后续 execute/resume/restart 必须重新验证并审批。
- ImageMagick delegate/policy/environment 会影响行为；Week 61 adapter 必须继续使用 allowlisted env 和
  managed temp/output，不能继承任意 delegate/module 配置。
- Preview/metadata 不能证明 PCB 制造语义正确；Week 62 仍需 corrupt/bomb、content/baseline verification。
- 起点工作区现存 0.4.0 zip 与 acceptance 文档 hash 不一致，后续 release 工作不得把它当已验收包。

## Week 59 输入

- 冻结 schema version `1`、built-in pack id `gerber-tiff`、dependency ids `gerbv`/`imagemagick`、
  stable diagnostic codes、tool identity 与 text/JSON renderer contract。
- 输入只接受 workspace 内显式目录；ZIP/archive、UNC/network、reparse tree Deferred。
- v1 allowlist：Gerber `.gbr .ger .gbx .gtl .gbl .gts .gbs .gto .gbo .gm1`；
  Excellon `.drl .xln`，大小写不敏感。
- 初始 bounds：depth 8、256 files、64 MiB/file、512 MiB total；空/超限/collision/unreadable
  必须稳定失败，不能静默截断后计划转换。
- 合法 fixture 与 expected metadata 已冻结；Week 59 只能实现 discovery/preflight/plan，仍不转换。
- 静态 doctor 可复用；Week 59 不应增加 run、staging、TIFF verification、prune 或 approval persistence。
