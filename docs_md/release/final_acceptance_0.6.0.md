# Final Acceptance 0.6.0

状态：Draft / Decision Pending

日期：待最终验收

## 当前决定

0.6.0 尚未 Accepted。本文件在 source freeze 前固定验收边界；最终决定只能引用用户确认的 clean source revision、同 revision 的全量证据、两份独立 Desktop candidate 及其各自 packaged smoke、Windows Narrator 人工结果和 CLI regression。任一安全、memory、Narrator、identity、reproducibility、payload 或 cleanup Gate 未关闭时，决定必须为 `Blocked`。

不在本任务中自动创建或推送 tag，不上传制品，不发布 GitHub Release。

## 身份（冻结后填写）

- Product version: `0.6.0`
- Source revision: Pending
- CLI release manifest/checksums: Pending
- Candidate A manifest/archive SHA-256: Pending
- Candidate B manifest/archive SHA-256: Pending
- Accepted candidate package/AppHost SHA-256: Pending

## Gate 状态

| Gate | 状态 | 证据 |
|---|---|---|
| G1 Renderer/package security | Pending clean-revision run | Exact `39 invoke + 2 event`; clean package audit 与 npm audit 待重跑。 |
| G2 Authoritative .NET write path | Pending clean-revision run | Desktop 使用 typed AppHost RPC，不解析 CLI 文本或复制安全策略。 |
| G3 Contract/bounds/corrupt/fuzz | Pending clean-revision run | `desktop-v1` surface 冻结；全量 protocol evidence 待重跑。 |
| G4 Dual packaged task loop | Pending | 两份 candidate 尚未生成和 smoke。 |
| G5 Deny/cancel/crash/restart/cleanup | Pending | 两份 candidate matrix 尚未运行。 |
| G6 Review/Preview/Narrator/accessibility | Pending manual Gate | 自动矩阵需从 clean revision 重跑；7 步 Narrator 人工结果尚未执行。 |
| G7 CLI regression/version | Pending | 源码元数据已统一到 0.6.0；Release suite/default smoke 待重跑。 |
| G8 Identity/reproducibility/payload | Pending | 双构建 manifest/inventory/notices/checksums/archive comparison 待生成。 |
| Performance hard Gate | Pre-freeze method passed; release evidence pending | 对称窗口 5 profile 首轮为 working set `6.90%–9.53%`、private bytes `4.97%–10.39%`，cleanup 0；dirty source 下状态保持 `Measured`。 |

## Accepted / Preview / Deferred / Skipped 边界

- Accepted：只有最终 Gate 全部关闭后，才包括继承的 CLI 稳定能力与本次明确通过的 0.6.0 Desktop credential-free task/review surface。
- Preview：localhost read-only API/daemon 继续 default-off；Desktop 的 Gerber/TIFF view 仅是 bounded review/Preview，不扩大 ownership 或 correctness。
- Deferred：浏览器 Web UI、远程控制、团队权限、后台 scheduler、并发 write worker、provider routing、插件市场、IDE/办公套件、通用 CAM/EDA correctness。
- Skipped/Unproven：未显式执行时，real model、real MCP、real Gerber/Gerbv/ImageMagick/LibTIFF/Magick.NET 均保持未证明，不由 fake runtime、文件存在、exit 0、metadata-valid 或 Preview 外推。

## 最终 Release Decision（待填写）

Pending。最终只允许 `Accepted` 或 `Blocked`，并必须在 `docs_md/weekly/77_week_review.md` 中记录首次失败、修复、完整命令、计数/耗时、双构建差异与候选 hash。
