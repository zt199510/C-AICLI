# Week 92 Review：CLI 与 Desktop Chat-first 重构

更新时间：2026-07-30

状态：`Candidate Ready / Awaiting W86, W89 and W92 visual acceptance`

产品候选：`4cba061`

## 结果

Week84–92 的产品重构已在 `main` 完成。Desktop 采用参考 OpenCowork 信息层级的
Chat-first 架构，同时保留 C-AICLI 品牌、AppHost authority、`desktop-v1` 协议和既有
安全边界。CLI 的 26 个顶层命令全部经统一 composition root 注册，公共
`CliCommandFactory.Create` façade 保留，命令行为按 feature module 拆分。

本阶段没有修改 `desktop-v1` 合同，没有调用真实 provider，没有 push、tag、部署或发布。

## 已完成产品切片

| Week | Desktop / CLI 结果 |
|---|---|
| 84–85 | 冻结精简验收规则；建立 CLI composition context、root composer、global options 与 feature modules |
| 86 | Chat-first Shell、C-AICLI design tokens、响应式左右 drawer；CLI jobs/ci/review/run/session |
| 87 | 对话 timeline projection、工具与结果层级；CLI chat/tools/queue/skills/exec |
| 88 | 底部 Composer、inline approval 与 turn controls；CLI packs/artifacts |
| 89 | Changes/Terminal/Reports/Artifacts/Preview 统一 Workspace Inspector；CLI automation/pipeline |
| 90 | CLI/Desktop 直接在 `main` 集成；递归命令复用同一 root/context |
| 91 | Terminal 叶组件改为 controller 注入；键盘导航、ARIA、生产安全与跨端边界收口 |
| 92 | 修复 Windows CRLF 下 contracts/notices check；同步重构后的 architecture assertions |

## 架构收口

- Renderer 只有 `App.tsx` composition root 读取 `window.caicli`；Workspace Inspector、
  Review 与 Terminal 叶组件均通过受限 commands/controller 注入。
- `useDesktopController` 继续作为 runtime、workspace、thread 与 subscription owner；
  本轮没有修改 subscription、AppHost recovery 或 Electron lifecycle。
- AppHost 不引用 CLI；CLI command modules 不引用 AppHost/Electron/Desktop。
- CLI 根工厂为 1,281 行，保留公共 façade 与共享执行 helper；26 个顶层命令按模块注册，
  不再在 `Create` 内集中定义。
- queue/automation/pipeline 的递归委派复用当前 root、global options、logger、trace 与安全上下文。

## Week92 自动验收

以下命令均在 5 分钟硬上限内完成：

```text
npm run verify
  Passed: contracts, notices, accessibility, typecheck, lint
  Passed: 28 test files / 126 tests
  Passed: Electron Main, Preload and Renderer production builds
  Passed: production security check

D:\AI\.dotnet\dotnet.exe test src\CSharpAiCli.sln --no-build --no-restore
  Passed: 1423 / 1423 tests
```

补充兼容 smoke：

- CLI help 列出原顺序的 26 个顶层命令。
- `skills list --output json` 返回可解析 JSON，退出码为 `0`。
- `exec --max-steps 0` 在 provider 前稳定失败关闭，退出码为 `1`。
- automation/pipeline/queue、job、pack、artifact 与 composition 定向回归 50/50 通过。

## 明确未执行

按精简验收规则，本阶段没有执行或声称通过以下可选重型项目：

- 原 104 Gate schema/hash/lineage/evidence/anchor 流水线。
- 真实 provider 与 `.env.local` 读取；provider turns consumed 为 `0`。
- packaged E2E、重复 Electron 场景、长会话/forced-GC/private-bytes 资源矩阵。
- Windows Narrator 人工观察与历史 Gate handoff 重建。

## 非阻塞技术债

Week83 `provider-recovery-listener-retention` P1 继续保留为非阻塞技术债，未表述为已关闭。
本轮没有修改 Renderer subscription、AppHost recovery 或 Electron lifecycle，因此没有运行
listener 诊断。后续只有相关生命周期被修改且一次不超过 5 分钟的同场景比较显示继续恶化时，
才进入最小修复。

## 待用户验收

启动 Renderer 预览：

```powershell
cd D:\AI\C-AICLI\apps\desktop
npm run dev:renderer
```

检查以下地址：

- W86 Shell：`http://127.0.0.1:5173/?preview=week86`
- W89 Workspace Inspector：`http://127.0.0.1:5173/?preview=week89`
- W92 最终候选：使用 W89 fixture 完整检查当前最终布局、交互层级与 C-AICLI 品牌

用户回复 `W86 Passed/Failed；W89 Passed/Failed；W92 Passed/Failed` 后，才能把总 Goal
从 `Active` 标记为 `Complete`。
