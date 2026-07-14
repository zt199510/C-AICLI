# Gerber/TIFF Toolchain Gate

更新时间：2026-07-14

状态：Week 58 Gate Passed（技术与许可 spike）；真实运行能力仍 Deferred

## 结论

Week 58 在 Windows 上完成了以下真实、无模型转换链：

```text
RS-274X fixture
-> Gerbv 2.13.0 PNG render
-> ImageMagick 7.1.2-27 TIFF encode/inspect
-> LibTIFF 4.5.1 independent metadata inspection
```

该证据只通过真实工具、fixture 内容、输出哈希和 TIFF metadata 证明候选工具链可行，
不表示 C-AICLI 已实现 `packs run`、staging、TIFF verification 或真实业务验收。
Week 58 不将任何第三方二进制提交到仓库或打入 release。

## 候选与许可

| 组件 | 来源与版本 | 许可 | 再分发结论 | Week 58 决策 |
|---|---|---|---|---|
| Gerbv | `gerbv/gerbv` GitHub release `v2.13.0`，Windows MSYS2 UCRT64 portable ZIP | GPL-2.0-or-later；发布包包含 `COPYING` | 允许在履行 GPL 二进制/对应源代码、版权和许可证义务后再分发 | 0.5.0 默认不再分发；用户安装或解压后显式配置绝对路径 |
| ImageMagick | 官方 Windows release `7.1.2-27` portable Q8 x64 | ImageMagick License；允许商业使用和再分发，要求保留许可证与归属 | 可以合规再分发，但仍有 attribution、NOTICE 和供应链维护成本 | 0.5.0 默认不再分发；用户显式配置绝对路径 |
| LibTIFF `tiffinfo` | Anaconda `libtiff 4.5.1 hd77b12b_0`，上游 LibTIFF `4.5.1` | HPND-style LibTIFF license | 允许使用、修改和再分发，需保留版权与许可文本 | 仅作为 Week 58 独立复核工具；不成为默认运行依赖 |

许可证结论是工程 Gate，不是针对任意分发组合的法律意见。若后续决定捆绑任一工具，
Week 65 前必须重新核对精确 artifact、transitive notices、对应源代码义务和 release 内容。

## 下载与工具身份

### Gerbv

- Release URL：`https://github.com/gerbv/gerbv/releases/tag/v2.13.0`
- Portable asset：`gerbv_v2.13.0_.Windows.MSYS2.UCRT64.zip`
- Asset size：`17962489` bytes
- Asset SHA256：`BB77865DA031DF83482196A6F6E9FA98298433979D995C068C4533B9B5A9EE7D`
- `gerbv.exe` size：`10779843` bytes
- `gerbv.exe` SHA256：`8BC29F799D0FD0CE522B489040E814F11B2B491E60E1E13803CBDE8C32621E4A`
- Authenticode：not signed
- Probe argv：`["--version"]`
- Probe exit code：`0`
- Probe stdout：`gerbv version v2.13.0-dirty` 及版权行
- Probe stderr：空

上游 Windows artifact 自报 `-dirty`，因此 v1 identity 必须同时保存 canonical executable
path、file size、SHA256、probe version 和 probe raw bounded output，不能只匹配版本字符串。
建议最低版本固定为 `2.13.0`，新的版本/哈希先通过显式 probe 与重新信任。

### ImageMagick

- Release URL：`https://github.com/ImageMagick/ImageMagick/releases/tag/7.1.2-27`
- Portable asset：`ImageMagick-7.1.2-27-portable-Q8-x64.7z`
- Asset size：`22076074` bytes
- Asset SHA256：`DE1B67753F86A838F41754FE3BCE168C9C7AE1BD16706777FF60A3C1915EABA9`
- `magick.exe` size：`31147184` bytes
- `magick.exe` SHA256：`86F7225B9A72D2FC71D284F078E392A6911E2CB1F7C106DEA8ECEE91B0608C57`
- Probe argv：`["-version"]`
- Probe exit code：`0`
- Probe version：`ImageMagick 7.1.2-27 Q8 x64 b661ac9:20260705`
- Built-in delegates include `png` and `tiff`.

### LibTIFF

- `tiffinfo.exe` version：`LIBTIFF, Version 4.5.1`
- File size：`21504` bytes
- SHA256：`F276E7717CB15A811CD083A393079B7D125F6DA49C6354E3A7F2099BB1BAA321`
- Inspection uses `-M 64` to bound LibTIFF allocation.

## 用户配置与 smoke

Runtime 代码不得搜索或启动 workspace 内未知 executable。Week 59/61 的配置优先级草案为：

1. 显式 CLI `--tool-path`（doctor 单工具诊断）。
2. user-level config 中按 dependency id 保存的绝对路径。
3. 明确允许的系统安装定位；不得扫描 workspace 或网络下载。

通用 Project Pack DTO 只保存 dependency id 和 redacted/canonical identity metadata，
不保存用户机器绝对路径到 manifest、plan、job 或可移植报告。绝对路径只存在于本地诊断上下文，
默认 text/JSON 输出使用文件名、hash 与配置来源，不回显完整路径。

真实工具 smoke 仅在以下条件全部满足时运行：

- `CAICLI_GERBER_TIFF_TOOL_SMOKE=1`
- 调用方显式提供已安装工具路径
- fixture 是仓库内已授权样本
- smoke 重新计算工具/input hash 并经过当前 approval policy

默认 build/test/smoke 不下载、不安装、不启动真实工具，不依赖网络或模型。

## v1 typed argv

## Week 59 input envelope

Week 59 的 bounded discovery 只接受 workspace 内的显式目录，不接受单文件快捷猜测、ZIP、
其他 archive、UNC/network path 或 reparse-point tree。扩展名大小写不敏感，v1 allowlist 冻结为：

```text
Gerber:   .gbr .ger .gbx .gtl .gbl .gts .gbs .gto .gbo .gm1
Excellon: .drl .xln
```

报告级 sidecar allowlist 仅包含 `.gbrjob`。sidecar 会进入 bounded metadata/hash inventory 和 plan
fingerprint，但不解析 raw content，也不作为 argv 输入传给 Gerbv 或 ImageMagick。未知扩展名只形成
bounded warning，不计算内容 hash、不进入工具输入或 fingerprint。

required layer group 冻结为“至少一个 Gerber layer”；drill、sidecar、generic Gerber 以及
top/bottom copper、solder mask、silkscreen、board outline 具体角色均为 optional。`.gtl/.gbl/.gts/
.gbs/.gto/.gbo/.gm1` 直接映射到具体角色；`.gbr/.ger/.gbx` 只允许确定性文件名提示或
`generic-gerber`，不调用模型猜测 layer mapping。

扩展名只用于 bounded inventory，不证明内容合法；Gerbv stderr、loaded count 和后续 verification
仍须检查。Week 59 初始上限为：recursion depth `8`、file count `256`、single file
`67108864` bytes（64 MiB）、total bytes `536870912`（512 MiB）。超限、空 inventory、重复
canonical path、case collision、outside path、unreadable file 和 reparse point 必须产生稳定 diagnostic，
不能静默截断后继续转换。

为补齐枚举与路径边界，v1 同时冻结 directory count `256`、workspace-relative path length `512`
characters 和 monotonic scan/hash time `10000 ms`。这些上限是硬失败，不允许静默截断后继续计划。

这些值是 discovery/preflight 输入，不授权真实工具执行，也不进入通用 Project Pack DTO 的领域字段。

### Gerbv render stage

Executable：经 canonicalization、regular-file、reparse-point 与 hash 检查后的 `gerbv.exe`。

固定 argv 模板：

```json
["-x", "png", "-D", "300", "-o", "<managed-output.png>", "<staged-input.gbr>"]
```

v1 不接受模型生成或扩展参数，不接受 shell 字符串、project hook、用户任意 extra args、
response file、ZIP 或网络路径。输入只允许已 inventory 的 workspace 文件，输出只允许 managed
run directory 中尚不存在的文件。cwd、环境变量、stdout/stderr bytes、运行时间和输出数量均有上限。

### ImageMagick TIFF stage

固定 argv 模板：

```json
["<managed-input.png>", "-alpha", "off", "-colorspace", "sRGB",
 "-units", "PixelsPerInch", "-density", "300", "-compress", "LZW",
 "<managed-output.tiff>"]
```

`MAGICK_CONFIGURE_PATH` 指向受信任安装目录，`MAGICK_TEMPORARY_PATH` 指向本次 managed run
directory。后续 runtime 使用 allowlisted bounded environment，不继承会改变 delegate、policy、module、
registry 或临时目录行为的任意 ImageMagick 环境变量。

## Fixture Gate

- 文件：`src/CSharpAiCli.Tests/Fixtures/GerberTiff/real/minimal-square.gbr`
- 来源：C-AICLI 项目原创，不包含第三方 PCB 或制造数据
- 许可：fixture 级 CC0-1.0，文件内有 SPDX identifier，目录 README 记录来源
- Size：`257` bytes
- SHA256：`D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54`
- 内容：毫米、2.4 坐标、10 mm x 10 mm 闭合方框、0.2 mm 圆形绘制 aperture、中心 1 mm flash

机器可读预期值在同目录 `expected-metadata.json`。它只固定本次候选版本的可复核
render/encode metadata；preview image 本身不作为真实业务正确性证明。

## 真实最小转换证据

### Gerbv 正例

```text
gerbv.exe -x png -D 300 -o <run>/minimal-square.png <fixture>/minimal-square.gbr
```

- exit code：`0`
- duration：`1076 ms`
- stdout/stderr：均为 `0` bytes
- PNG：`126 x 126`、24-bit RGB、`471` bytes
- PNG SHA256：`850A2B4A555F2AE4060E6F541D8CE7E73D959C903000F7987C75D3DD4146D747`
- 相同版本/input/argv 连续两次输出 SHA256 相同

### ImageMagick TIFF 正例

```text
magick.exe <run>/minimal-square.png -alpha off -colorspace sRGB \
  -units PixelsPerInch -density 300 -compress LZW <run>/minimal-square.tiff
```

- exit code：`0`
- duration：`1082 ms`
- stdout/stderr：均为 `0` bytes
- TIFF：single-page、`126 x 126`、8-bit/sample、RGB、3 samples/pixel、LZW、300 x 300 DPI
- TIFF size：`1122` bytes
- TIFF SHA256：`FDDFA21D29EF870D94EC953ADF757BD61585957E1519D79BB2480AFF81C89823`
- 间隔超过一秒的两次输出 size/hash 相同

## TIFF inspection Gate

ImageMagick `identify` 与 LibTIFF `tiffinfo -M 64` 均成功读取 single-page TIFF，metadata
一致。另用两个相同 PNG 创建了一个真实 two-page TIFF：

- file size：`2260` bytes
- SHA256：`69CA9004C2652AEB9F77D0C3FC149026FDCF829778A2AFC47E30590472226910`
- pages：`2`
- 每页：`126 x 126`、8-bit RGB、LZW、300 DPI
- ImageMagick 分别报告 scene `0`、`1` 和 pages `2`
- LibTIFF 分别报告 TIFF directory `0`、`1`

因此 ImageMagick 7.1.2-27 满足 Week 58 对 multipage、compression、DPI、pixel format
和 preview/encode 的候选要求；LibTIFF 提供独立 metadata 复核。Week 62 仍须实现 bounded
inspection、corrupt/bomb 防护与 stable DTO，不能把 `identify` 文本直接当产品 schema。

## 失败、部分输出与 cleanup

用 README 文本替代 Gerber 输入时，Gerbv 的实际行为是：

- exit code 仍为 `0`
- stderr 报 `Unknown file type` 与 `loaded 0`
- 仍生成 `87`-byte PNG

因此 `exitCode == 0`、文件存在、metadata 可解析或 preview 可打开，任一条件单独都不能判定
转换成功。adapter 必须拒绝非空 error-level stderr、确认输入加载与输出声明，并在 Week 62 做
TIFF 内容/baseline verification。

真实 Gerbv GUI/no-export 进程分别经过 `750 ms` timeout 和显式 cancel：两次均终止进程树，
observed exit code 为 `1`，最终 `gerbv` process count 为 `0`。后续 adapter 必须在 timeout、
cancellation 和 parent failure 时执行 bounded tree cleanup；partial output 保留为失败证据，不能发布为
succeeded artifact。

## Deferred

- C-AICLI 内的真实 `packs run` 和工具 adapter
- Gerber/Excellon discovery、ZIP/网络输入和任意 archive 解压
- managed staging/checkpoint/resume/restart
- TIFF verification/baseline compare/preview report
- artifact lifecycle/prune 与 human accept/reject
- 任意 repo hook、用户自定义 args 或模型生成真实工具参数
