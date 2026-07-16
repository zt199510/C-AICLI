# 第 65 周回顾

状态：已完成；CLI 0.5.0 发布决定为 **Accepted**。本次记录未创建 release tag。

## 决定

Week 58-64 implementation reviews 均已完成，且 Week 64 没有遗留实现项。Week 65 已完成版本、文档、clean-source release build、full tests、default packaged smoke、双构建复现、manifest/checksum、packaged diagnostics 和 cleanup 验证，据此接受 0.5.0。

当前环境未配置冻结 Gerbv/ImageMagick 路径，因此 current-source real-tool opt-in smoke 未运行。发布决策将该分支调整为用户具备 reviewed external tools 时的可选环境验证，不再作为 0.5.0 发布 Gate。不运行无身份工具、不放宽 hash，也不把 fake、文件存在、metadata 或 preview 写成真实 CAM/EDA 业务正确性通过。

## Source 与版本

- Accepted package source revision：`09378e66463e2830bd432793d863fcfe8f8156bd`，构建前 clean。
- Version / AssemblyVersion / FileVersion：`0.5.0` / `0.5.0.0` / `0.5.0.0`。
- SDK / configuration / runtime：`9.0.308` / `Release` / `win-x64`。
- Manifest：`sourceDirty=false`、`releaseAcceptance=true`、PDB policy `excluded`。

## Build 与测试

实际命令：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet build-server shutdown
$env:DOTNET_PROCESSOR_COUNT = "1"
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
```

- Build：0 warnings，0 errors。
- 标准并发 full suite：1259 passed / 2 failed / 0 skipped。既有 MCP wall-clock test 为 2.7416662s；另一个 MCP handshake cleanup 遇到被占用 temp directory。该轮不计通过。
- 清理 build server 后受控单处理器 full suite：1261 passed / 0 failed / 0 skipped，耗时 3m50s。
- Default packaged smoke：`smoke tests passed`；real Gerber/TIFF、daemon/API、real model 分支均明确 skipped。
- Real-tool opt-in smoke：**Not run / Optional**。四个 prerequisite 中两个 external executable path unavailable；不阻塞 0.5.0 release acceptance。

## Package evidence

两次 clean-source 构建结果一致：

| Evidence | Size | SHA256 |
|---|---:|---|
| `caicli.exe` | 61,460,601 | `3C24E020560428C69FC3D164A48F84BFED979ED98514EC70EE267BBD09787972` |
| ZIP | 55,966,314 | `CAFF04CB1CA7B028F6A4D0CB1A357BA4FCBD190B7C2D61679EB883E054C5E3AD` |
| authorized fixture | 257 | `D2FBD6E2393EFC0513915C3B5B2E7C24C80AE90A2102BB75E9CA873B31010D54` |
| strict baseline file | 919 | `970194F62A063B4F5442E726C2861B5E01086B3636F20CAB18C6B1F4EE75B9AC` |

publish inventory 两次完全一致，包含 executable、release manifest 和 Magick.NET notice；checksums JSON 另列 ZIP。manifest 与 checksums 的 source revision 均为 `09378e6...`。

## Packaged diagnostics 与 cleanup

- `caicli.exe version`：exit 0，0.5.0 / net9.0 / win-x64。
- `packs list --output json`：exit 0，列出 deterministic `gerber-tiff` contract。
- `packs doctor gerber-tiff --output json`：exit 1，Gerbv/ImageMagick 均为 `pack-tool-path-missing`，无进程启动。
- `artifacts list --output json`：exit 0，隔离 profile 下为空列表。
- `caicli` / `gerbv` / `magick` process count：0 / 0 / 0。
- `caicli-smoke-*`、pack probe、controlled process、TIFF verification、pack run、artifact lifecycle、restart temp count：全部 0。

## Accepted / Preview / Deferred

- Accepted：0.5.0 bounded Project Pack、Gerber/TIFF conversion/verification/preview/human gate、safe resume/restart、artifact lifecycle 与 clean-source package；同时继承 0.4.0 capability。
- Preview：default-off IPv4 loopback-only read-only Local API/daemon；非决定性 model visual guidance。
- Deferred：ZIP/network input、complete parser、general CAM/EDA correctness、generic C++/EDA pack、scheduler、parallel/concurrent worker、remote runner、provider routing、API control/SSE/auth/TLS、team platform、marketplace、UI、accept undo、artifact restore 和 automatic repair。

## 可选后续验证

当 reviewed Gerbv 2.13.0 与 ImageMagick 7.1.2-27 exact executables 可用时，可以对已记录 package 运行 real-tool opt-in smoke，补充 conversion、strict content comparison、preview、human decision、prune 和 cleanup 环境证据。该验证不改变 0.5.0 Accepted 状态，也不能扩展为通用 CAM/EDA 正确性声明。
