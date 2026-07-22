# Final Acceptance 0.6.0

状态：Blocked

日期：2026-07-22

## 最终决定

0.6.0 本轮不接受、不推广。用户明确放弃 Windows Narrator 人工验收，因此 G6 未关闭；双候选正式比较按设计 fail closed，结果为 `Failed`，原因是 `Narrator evidence is not a Passed manual run.`。其余已执行的 clean-source 自动化、双构建、payload identity、packaged smoke 与 cleanup 结果保留为本轮证据，但不能替代 Narrator 人工结果，也不能推出 `Accepted`。

本次收尾未创建或推送 tag，未上传制品，未发布 GitHub Release。

## 冻结身份

- Product version：`0.6.0`
- Source revision：`c74f93f45b0cae4a06cd70ee6dbca1b333a5973e`
- Branch：`week-02-cli-commands-doctor-config`
- CLI win-x64 archive SHA-256：`C99DC3893B80642FCAB629115985A37716B8D093024A03E63A22974BA5FB23C6`
- Candidate A：`artifacts/desktop-rc-week77-c74-a/0.6.0-rc.1`
- Candidate B：`artifacts/desktop-rc-week77-c74-b/0.6.0-rc.1`
- Candidate A/B archive SHA-256：`1A258F9412B47D3199B2BFA2072AB37DC97412ABC38A2A6DE2AAB1E58746EF3B`
- Desktop SHA-256：`2D7B6598352665B64527F16D006B4818AFD5EC9DE43FB00693B5AE5A86F93FEA`
- AppHost SHA-256：`C69CF9DCA3BFFBFE95E38568642FDEA7C438EFD0D7D3D3F47F59FDC3434C9EC2`

Candidate A/B 的 payload inventory 均为 `78/78`，逐文件无差异；除预期的 `buildTimestampUtc` 外 manifest identity 一致。两份 archive hash、Desktop hash、AppHost hash、ASAR、notices 与 smoke 绑定值一致。

## Gate 状态

| Gate | 状态 | 证据 |
|---|---|---|
| G1 Renderer/package security | Passed | Desktop verify/security/build 通过；package audit 与 `npm audit` 为 0 vulnerability；IPC surface 保持 `39 invoke + 2 event`。 |
| G2 Authoritative .NET write path | Passed | Desktop 使用 typed AppHost RPC；未引入 CLI 文本解析或重复安全策略。 |
| G3 Contract/bounds/corrupt/fuzz | Passed | `desktop-v1` protocol evidence `3/3` 通过，AppHost identity 与冻结 revision 一致。 |
| G4 Dual packaged task loop | Passed | Candidate A/B 各自 packaged smoke `8/8`，均为 1 worker、0 retry、cleanup `0/0`。 |
| G5 Deny/cancel/crash/restart/cleanup | Passed | success/deny/cancel/crash/restart/corrupt-state/read-only/hardening `8/8`；进程和临时目录 delta 均为 0。 |
| G6 Review/Preview/Narrator/accessibility | **Blocked** | 自动 accessibility `2/2` 通过；用户明确放弃 Narrator，N1-N7 为 `Skipped/Unproven`，不能由自动 ARIA/accessibility 结果替代。 |
| G7 CLI regression/version | Passed | ReleaseAcceptance build、默认 smoke 与版本/manifest identity 通过；最终 diagnostic 全量 .NET `1400/1400`。首次全量为 `1397/1400`，其瞬时 timing/cleanup 失败已如实保留。 |
| G8 Identity/reproducibility/payload | Payload checks passed; release comparison Failed | 双候选 inventory `78/78`、archive 与核心 hash 一致；正式 comparison 在完成这些校验后因 Narrator evidence 非 Passed 而 fail closed。 |
| Performance hard Gate | Passed | package growth `+6.13%`、ASAR `-73.53%`、AppHost `+0.45%`；5/5 long-session profile 均低于 15%，所有 cleanup delta 为 0。 |

## 最终 clean-source 证据摘要

- Desktop verify：`23 files / 102 tests`。
- Desktop E2E：unpacked `9/9`，packaged `8/8`。
- Accessibility automation：`2/2`，cleanup `0/0`。
- Packaged acceptance smoke：`8/8`，cleanup `0/0`。
- Protocol：`3/3`，每轮 `48/48` response，cleanup `0/0`。
- .NET：首次全量 `1397/1400`；受影响定向 `3/3` 后，diagnostic 全量 `1400/1400`。
- Candidate A/B：各 `8/8` smoke；payload inventory `78/78`；archive SHA-256 完全相同。
- Narrator：`Skipped`；没有人工通过声明。

Performance 5 个 profile 的 idle retention（working set / private bytes）分别为：`-3.794% / -6.200%`、`2.900% / 1.653%`、`9.228% / 8.999%`、`9.158% / 8.593%`、`-3.829% / -8.782%`。

## Accepted / Preview / Deferred / Skipped 边界

- Accepted：本轮无最终 release acceptance；已通过的自动化结果仅作为冻结 revision 的局部证据保留。
- Preview：localhost read-only API/daemon 继续 default-off；Desktop 的 Gerber/TIFF view 仅是 bounded review/Preview，不扩大 ownership 或 correctness。
- Deferred：浏览器 Web UI、远程控制、团队权限、后台 scheduler、并发 write worker、provider routing、插件市场、IDE/办公套件、通用 CAM/EDA correctness。
- Skipped/Unproven：Windows Narrator N1-N7、real model、real MCP、real Gerber/Gerbv/ImageMagick/LibTIFF/Magick.NET。fake runtime、文件存在、exit 0、metadata-valid 或 Preview 均不得外推这些能力已经通过。

## Release Decision

**Blocked。** 唯一最终决策阻断项是未执行 Windows Narrator 人工验收。保留候选和证据供诊断，不生成或推广最终 release。
