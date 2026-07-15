# 第 62 周 TIFF Artifact Inspection、Preview 与 Verification Implementation Plan

状态：已完成

**Goal:** 对 Week 61 生成的 TIFF 做有界、可重复、可解释的工程验证，生成 metadata、preview/contact sheet、baseline comparison 和稳定报告，避免仅凭“文件存在”判定转换成功。

## 来源

- `docs_md/weekly/58_week_project_pack_contract_toolchain_spike.plan.md`
- `docs_md/weekly/61_week_gerber_tiff_controlled_conversion.plan.md`
- Week 58 选定的 TIFF library/tool 与 Week 61 真实输出
- `src/CSharpAiCli.Core/Jobs/JobRecords.cs`
- `src/CSharpAiCli.Core/Exec/MarkdownTaskReportRenderer.cs`

## 前置条件

- Week 61 已产生至少一个真实 TIFF artifact，并记录 tool identity、input manifest 和 output hash。
- Week 58 选定的 TIFF inspection 依赖已通过 license、安全和目标格式验证。

## 本周范围

- 实现 TIFF signature/decoder preflight 和 bounded metadata reader。
- 提取 width、height、DPI、page/frame count、bits per sample、pixel format、compression、orientation 和 file size/hash 等已验证字段。
- 对 page/frame count、pixel count、decoded memory、dimension、file size 和 decode time 设置上限，防止 decompression bomb/资源耗尽。
- 根据 conversion plan 的 expected output declaration 检查 missing/unexpected/duplicate/partial output。
- 生成本地 PNG preview 或 contact sheet，写入 managed artifact directory，不覆盖、不上传、不自动打开。
- 定义 baseline manifest：预期 metadata、允许 tolerance、可选 exact hash/pixel digest；baseline 文件必须来自 workspace 内明确路径或受控 fixture。
- 定义 deterministic verification result 和 JSON/markdown report。
- 将 verifier status、warnings、preview/report pointers 写回 run/job/artifact；通过后进入 `awaiting-acceptance`，不自动 accepted。

本周明确不做：

- 不把模型视觉判断作为 hard pass/fail。
- 不做完整 CAM/EDA 电气或制造正确性分析。
- 不将 preview 视为 TIFF 原始 artifact 的替代品。
- 不自动修改、压缩或归一化原始 TIFF 来掩盖差异。
- 不接受网络 baseline、URL 或 workspace 外任意 reference。

## Verification Level

建议按以下层次输出，不混淆“格式有效”和“业务正确”：

1. `file-valid`：文件可读、signature/decoder 正常、资源边界内。
2. `metadata-valid`：尺寸、DPI、页数、格式等符合 plan/baseline。
3. `content-compared`：执行 exact hash 或明确 tolerance 的 pixel comparison。
4. `human-review-required`：生成 preview，等待 Week 63 accept/reject。

任何较低层通过都不能隐式代表较高层通过。

## Frozen v1 TIFF Contract

- Metadata fields: file size/SHA256/byte order, frame count, and per-frame width/height, DPI X/Y,
  bits/sample, samples/pixel, pixel format, compression, orientation, alpha presence, pixel count and decoded bytes.
- Supported hard-verification input: classic TIFF (`II*` or `MM*`), one to 32 frames, 8-bit RGB without alpha,
  LZW compression, PixelsPerInch density, and declared `.tiff` outputs only. BigTIFF and every undeclared
  compression/pixel format fail with a stable unsupported diagnostic rather than being partially accepted.
- Resource limits: 256 MiB/file, 32,768 pixels/dimension, 100,000,000 pixels/frame, 128,000,000 pixels total,
  32 frames, 512 MiB decoded RGBA memory, 15 seconds/decode, and 2,048 pixels/preview edge.
- Pixel comparison uses `rgba8-absolute-v1`: decoded sRGB RGBA8, visual top-left orientation, straight alpha,
  an explicit maximum per-channel delta and maximum count of pixels exceeding that delta. Reports include both
  observed values and normalized pixel SHA256; they never collapse the result to a vague similarity score.
- Exact file SHA256 comparison is permitted only when the baseline declares `byteDeterministic=true` and records
  the producing tool/input identity. Preview state is always separate from hard verification state.

## 用户入口草案

```powershell
caicli packs verify <run-id>
caicli packs verify <run-id> --baseline .\.caicli\baselines\board-a.json
caicli packs verify <run-id> --output json
caicli packs preview <run-id>
```

Preview 命令只返回 artifact path/metadata；CLI 不启动默认图片查看器，避免隐式外部进程。

## Baseline 设计

- Baseline schema/version 必须显式，未知字段/版本 fail closed。
- 记录生成 baseline 时的 pack version、tool name/version、fixture/input fingerprint。
- 精确 hash 只在工具输出已证明 byte-deterministic 时启用。
- Pixel comparison 必须记录算法、颜色空间、alpha/orientation normalization 和 tolerance；不得只输出单个模糊“相似度”。
- 更新 baseline 是显式独立动作，不由 failed verification 自动覆盖。

## 测试计划

- valid single/multipage TIFF 和每个受支持 compression/pixel format fixture。
- invalid signature、truncated file、corrupt IFD、huge dimension/page count、decode timeout。
- missing/unexpected/duplicate outputs 和 file changed after conversion。
- metadata exact/tolerance pass/fail，baseline schema/path/hash mismatch。
- preview dimensions/output naming/no-overwrite/deterministic manifest。
- pixel compare orientation/color/alpha/tolerance 边界。
- verifier read-only：不修改 TIFF、source 或 baseline。
- JSON/markdown redaction 和 schema snapshot。

## 任务清单

- [x] Step 1: 冻结 TIFF metadata、resource limits 和 verification levels。
- [x] Step 2: 实现 bounded TIFF reader/decoder wrapper 和 stable diagnostics。
- [x] Step 3: 实现 declared output inventory 与 file/hash revalidation。
- [x] Step 4: 定义 baseline manifest/schema、tolerance 和 source boundary。
- [x] Step 5: 实现 metadata/exact-hash/pixel comparison。
- [x] Step 6: 实现 managed PNG preview/contact sheet 生成。
- [x] Step 7: 实现 `packs verify/preview` text/JSON 和 markdown report。
- [x] Step 8: 将 verification/preview/report artifacts 接入 run/job。
- [x] Step 9: 增加 malicious/corrupt/large TIFF、baseline 和 renderer tests。
- [x] Step 10: 扩展 default fake fixture smoke 和 real-tool verification smoke。
- [x] Step 11: 更新 quickstart/security/known limitations/runtime diagnostics 草稿。
- [x] Step 12: 运行 build/test/default smoke/real-tool smoke。
- [x] Step 13: 创建 `62_week_review.md`，冻结 Week 63 human gate/artifact contract。

## 验收标准

- invalid、truncated、oversized 或 metadata mismatch TIFF 不能进入 awaiting-acceptance。
- 同一 artifact/baseline 产生稳定 verification result 和 report ordering。
- preview 只作为人工复核辅助，不改变 hard verification status。
- verifier 不写 source/workspace 文件；所有生成物位于 managed run artifacts/reports。
- 真实工具输出完成至少一次 metadata verification 和 preview 生成。
