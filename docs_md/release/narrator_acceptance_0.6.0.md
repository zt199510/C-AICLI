# Windows Narrator Manual Acceptance 0.6.0

状态：Pending（必须在最终 clean-source package 上由人工执行；自动 ARIA、截图或脚本结果不能代替）

## 执行前提

- 使用最终候选的原始 package，测试后不得重打包并沿用本结果。
- 记录 clean `sourceRevision`、package 根目录清单 SHA-256、Desktop executable SHA-256 与 AppHost SHA-256。
- 记录操作者、UTC 开始/结束时间、Windows 版本与 build、Narrator 文件版本、显示缩放、应用 zoom、forced colors/high contrast 与 reduced-motion 设置。
- 使用 credential-free fake runtime/workspace；不得在证据中保存 API key、原始 prompt、用户绝对路径或未脱敏诊断。
- 每一步只能记录 `Passed` 或 `Failed`。任何 `Failed` 都会阻止 0.6.0 acceptance；修复后必须在新的 clean revision 上重跑受影响自动与人工矩阵。

建议把结果复制到 `artifacts/desktop-acceptance/narrator-accessibility.json`，结构以 `evidence_templates/narrator_accessibility_0.6.0.template.json` 为准。RC validator 只接受 7 步全部通过的人工 evidence。

## 人工步骤

### N1 启动与 runtime 状态

1. 启动 packaged Desktop，再启动 Narrator。
2. 用 Narrator 导航应用 title、主 heading 与 runtime status。
3. 确认 `AppHost ready`（或安全的 error status）只在状态变化时合理朗读，没有重复噪声。
4. 确认朗读不包含本地绝对路径、profile 路径、secret sentinel 或原始诊断。

通过条件：应用身份、主要区域与 runtime 状态可辨识，状态变化不重复轰炸，敏感信息未朗读。

### N2 Workspace、thread 与焦点恢复

1. 仅使用键盘打开受控 workspace。
2. 创建 thread，重命名后归档。
3. 分别打开/关闭 thread create、rename、archive UI；用 `Escape` 关闭可关闭表面。
4. 每次关闭后确认焦点回到触发表面的按钮或合理的下一操作点。

通过条件：所有操作不依赖鼠标，名称、状态和完成反馈可朗读，`Escape` 不误关外层 surface，焦点不丢失。

### N3 Composer 与 mention listbox

1. 聚焦 composer，输入不含敏感数据的测试文本。
2. 打开 mention listbox；用方向键移动 option 并选择一项。
3. 排队 prompt，确认 pending/ready 状态可感知；随后清除 pending input。

通过条件：textbox、listbox、active option、选择结果与 queue 状态均可理解；选择与清除后焦点合理。

### N4 Approval、deny 与 restart alertdialog

1. 触发受控 fake approval，检查风险摘要、Approve 与 Deny 按钮。
2. 先走 Deny 闭环并确认终态可感知。
3. 触发 crash/recovery，再打开 restart `alertdialog`，检查标题、说明、取消和 restart action。
4. 完成 restart 后确认旧 approval 不可复用，新状态被朗读。

通过条件：dialog/alertdialog 语义、风险、动作与终态完整；焦点被约束并在关闭后恢复。

### N5 Changes、Reports、Artifacts 与 Gerber/TIFF Preview

1. 用键盘遍历 Changes、Reports、Artifacts、Gerber tabs。
2. 确认 tab/tabpanel 关系、selected 状态、empty state、warning、truncation 与 preview correctness disclaimer 可理解。
3. 打开一个受控 artifact detail，确认只朗读相对路径或安全标识。

通过条件：tabs 与 panel 对应明确；Preview、metadata-valid 或文件存在不会被表达为 real-tool/manufacturing correctness。

### N6 Error、recovery、live region 与 terminal status

1. 触发 AppHost stopped/recovery 与一个 inline error。
2. 检查 live region 不重复朗读，不泄漏绝对路径、secret 或原始诊断。
3. 打开 terminal，执行受控 fake output，检查 running、truncated、exit/cancel status；关闭 terminal。

通过条件：错误与恢复动作可理解，terminal lifecycle 可感知，敏感内容未朗读，关闭后焦点恢复。

### N7 200% zoom、forced colors 与 reduced motion

1. 在应用 200% zoom 下复核 N2–N4 的关键打开/关闭/焦点闭环。
2. 启用 Windows forced colors/high contrast，确认可见焦点、selected/disabled/warning 状态仍可区分。
3. 启用 reduced motion，确认状态信息不依赖 animation，焦点与 Narrator 顺序不受影响。

通过条件：没有阻断性裁剪或键盘陷阱；焦点始终可见；状态不只靠颜色/动画表达。

## 证据规则

- `operator` 必须是实际执行者，不能写 Codex、automation 或测试框架。
- `manualNarratorRun` 必须为 `true`；每个 step 必须含简短、已脱敏的 observation。
- 问题引用使用仓库相对链接或 issue URL；没有问题时使用空数组。
- 截图只作为补充，不能替代听觉观察；只记录仓库相对路径与 SHA-256。
- 只有 7 步全部 `Passed`、identity 与候选一致且声明为人工执行时，顶层 `status` 才能写 `Passed`。
