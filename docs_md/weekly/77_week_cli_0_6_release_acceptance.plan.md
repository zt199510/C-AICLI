# Week 77 执行任务：CLI/Desktop 0.6.0 Release Acceptance

状态：Ready（先关闭 Week 76 G6 blockers；未关闭则最终决定必须为 Blocked）

**Goal:** 在固定、可引用的 clean source revision 上关闭 Week 76 遗留的 renderer memory stability、Windows Narrator 与 Release Candidate 证据缺口，统一 CLI/Desktop 版本与发布文档，完成两次相同源码的可复现 Desktop 构建、默认 packaged acceptance smoke、CLI regression 和 0.6.0 最终发布决定。Week 77 不以重跑掩盖失败，不降低 8 项关键验收 Gate。

## 创建基线

- 任务创建日期：2026-07-21。
- 创建任务时 HEAD：`ed5822e6858023fabc5376b48f573dd684ee777f`，工作树 clean。
- Week 76 review：`docs_md/weekly/76_week_review.md`，状态 `Blocked`。
- 自动化基线：`.NET 1398/1398`、Desktop `21 files / 97 tests`、unpacked E2E `9/9`、packaged E2E `8/8`、package audit 0 forbidden payload、`npm audit` 0 vulnerability。
- Reviewed Desktop surface 仍为 exact `39 invoke + 2 event`；Week 77 默认冻结 contract surface。
- Desktop npm version 已是 `0.6.0`；根 `Directory.Build.props`、CLI assembly/file version 仍是 `0.5.0`，必须在 source freeze 前统一。
- Week 76 最终 AppHost SHA-256 为 `3EA3C27A54CB106ED098DFE1600FDB2B43E532707B831DF263415C4441E025BE`；这只是输入基线，Week 77 修改后必须重新计算，不能复用。

## Week 76 必须关闭的 Blockers

1. Renderer long-session private-bytes retention 曾两次达到 `17.72%` 与 `17.11%`，可重复超过 15% hard Gate；后续 `12.67%`、`14.78%` 不能删除或覆盖首轮失败。
2. Windows Narrator 的启动、thread/composer、approval、review、error/recovery 人工 smoke 未执行。
3. Week 76 source dirty，RC orchestrator 正确拒绝生成 `0.6.0-rc.1`；不存在 clean-source candidate、archive、checksums 或 RC smoke。

执行顺序是强制的：先关闭 1-2 并完成版本/文档修改，形成用户确认的 clean source revision；再从该 revision 重新生成全部 source-bound evidence 和 RC。任一 blocker 未关闭时，停止 Accepted 声明并创建 Blocked review。

## 本周范围

- 定位并修复 renderer reload/long-session private-memory retention，或修复会产生不稳定低基线的 measurement methodology；不得只放宽阈值、增加 retry 或删除 private-bytes 断言。
- 将 performance evidence 升级为单命令、固定 workload、连续独立 profile 的 versioned JSON；保存全部 profile，包括失败项。
- 创建并执行 Windows Narrator 人工 checklist，记录操作者、系统/显示设置、步骤、结果、问题与截图/文本证据；人工结果不得由自动 ARIA snapshot 代替。
- 统一 root CLI、Desktop package、assembly/file/informational version、release manifest、protocol/capability/source revision 文档到 `0.6.0`。
- 更新 CHANGELOG、installation、configuration、quickstart、security model、known limitations、capability status、troubleshooting、roadmap 和 Desktop README。
- 创建 `docs_md/release/final_acceptance_0.6.0.md`，明确 Accepted、Preview、Deferred、Skipped/Unproven。
- 在 clean source revision 上重新运行 security/accessibility/performance/protocol/.NET/Desktop/CLI evidence。
- 从同一 clean revision、相同锁文件和 SDK/runtime 连续构建两份 Desktop candidate，比较 manifest、inventory、AppHost/ASAR/executable/archive size 与 SHA-256。
- 对两份 candidate 分别执行 credential-free packaged acceptance smoke 和 process/temp cleanup；最终被接受的 archive 必须与已验证 candidate 字节一致。
- 创建 `77_week_review.md`，记录首次失败、修复、全部命令/计数/耗时、双构建差异与最终 release decision。

## 明确不做

- 不新增模型、MCP、工具、Gerber/TIFF correctness、任务中心、automation、远程控制或并发 write worker 能力。
- 不改变 `desktop-v1` 方法/事件数量；如发现必须改变，回退到 contract review，重新生成并运行所有兼容/安全证据。
- 不把 fake runtime、preview、文件存在、exit 0 或 metadata-valid 外推为 real-model/real-tool correctness。
- 不自动创建或推送 release tag，不上传制品，不发布 GitHub Release；这些需要用户单独授权。
- 不在 dirty tree 上生成“临时 RC”后再补写 revision，也不修改已 smoke 的 candidate 内容。

## Phase 1：关闭 G6 Performance Stability

冻结 workload：240 timeline items、5 次 renderer reload、50-row bounded diff、70 KiB terminal production/64 KiB retained projection、30 秒 idle recovery、单 worker、retries 0。

measurement 必须：

- 以 renderer PID/role 为主键记录 working set、private bytes 和 process count；同时记录 Main/GPU/Utility/AppHost。
- 对 warm baseline 与 post-workload idle 使用对称、固定的 readiness/idle window，避免从单个偶然低点计算增长。
- 一个命令连续运行至少 5 个相互隔离的 profile；profile 顺序、原始 sample、失败和 cleanup 结果全部写入同一 schema-versioned JSON。
- 每个 profile 的 renderer idle working set 与 private bytes 相对稳定 warm baseline 均 `<=15%`；任一 profile 超限即整次 Gate failed，不允许“重跑到绿”。
- 记录 reload transient peak，但不得把 transient peak 与 30 秒 idle retention 混为同一 Gate。
- 每个 profile process/temp delta 为 0；失败时仍必须执行 test-owned cleanup 并保留 evidence。
- package total、`app.asar`、AppHost 相对 Week 66 同口径增长均不得超过 15%，否则修复或 Blocked。

建议入口：`npm run test:e2e:performance` 与 `npm run measure:performance`；如升级 evidence schema，必须同步脚本测试、RC evidence validator 和 review。

## Phase 2：关闭 G6 Narrator/Accessibility

必须在最终 package 上人工完成并记录：

1. 启动应用并确认 title/runtime status 被合理朗读，无重复噪声。
2. 只用键盘打开 workspace，创建、重命名、归档 thread，并验证 Escape 与 focus restoration。
3. 在 composer 输入、打开 mention listbox、移动/选择 option、排队 prompt。
4. 处理 approval 的 Approve/Deny 与 restart alertdialog，确认风险摘要、按钮和终态可感知。
5. 遍历 Changes、Reports、Artifacts、Gerber tabs，确认 tab/tabpanel、warning、empty state 和 truncation 可理解。
6. 覆盖 AppHost error/recovery、inline error、live region、terminal status；不得朗读本地绝对路径、secret 或原始诊断。
7. 在 200% zoom、forced colors 和 reduced motion 下复核关键闭环与焦点可见性。

人工 evidence 至少包含：`status`、`sourceRevision`、package/AppHost SHA-256、Windows/Narrator version、操作者、UTC 时间、每步 Passed/Failed、问题链接和声明。任何 Failed 步骤必须修复并从新 clean revision 重跑受影响自动/人工矩阵。

## Phase 3：版本、能力与文档冻结

- 将 `Directory.Build.props` 的 Version/AssemblyVersion/FileVersion 更新为 `0.6.0` / `0.6.0.0`，同步 hardcoded release tests。
- 保持 `apps/desktop/package.json` 与 lockfile 为 exact `0.6.0`，验证 Electron/Node/Chromium/.NET/SDK/protocol 版本进入 manifest。
- `caicli version`、packaged Desktop、AppHost handshake、manifest 和 acceptance 文档必须指向同一产品版本和 source revision。
- 文档必须明确 CLI 稳定能力与 Desktop Preview/Accepted 边界；real model、real MCP、real Gerber/Gerbv/ImageMagick/LibTIFF/Magick.NET 未执行时保持 `Skipped/Unproven`。
- release 文档不包含测试 profile、绝对用户路径、token、环境 secret、原始 prompt 或未脱敏 diagnostics。
- 所有修改与修复完成后，要求用户确认 source-freeze commit；后续 acceptance build 不再修改 source。若必须修改，废弃旧 evidence/candidate，形成新 revision 后全量重来。

## Phase 4：Clean-source 全量回归

在冻结 revision 上顺序运行，避免把并行资源竞争误计为稳定通过；首次失败必须进入 review：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
dotnet build-server shutdown
dotnet build src\CSharpAiCli.sln -c Release --no-restore
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet build src\CSharpAiCli.AppHost\CSharpAiCli.AppHost.csproj -c Debug --no-restore
dotnet build src\CSharpAiCli.AppHost\CSharpAiCli.AppHost.csproj -c Release --no-restore

Set-Location apps\desktop
npm ci
npm audit --audit-level=high
npm run verify
npm run test:e2e:performance
npm run measure:performance
npm run measure:protocol
npm run package:dir
npm run test:e2e:unpacked
npm run test:e2e:packaged
```

还必须运行现有 CLI release/default smoke，至少覆盖 `version`、`doctor`、`config get`、`chat` credential-free failure、sessions、tools、MCP static diagnostics、workflow、packs/artifacts default fake path，以及 no residual process/temp。不得启用需要凭据、网络或真实外部工具的 opt-in smoke作为默认 Gate。

## Phase 5：双构建与 Candidate Acceptance

- 所有 Passed evidence 必须具有同一个 clean `sourceRevision`；AppHost-bound evidence 的 SHA-256 必须一致。
- 使用两个全新、互不覆盖且位于 `artifacts` 下的 output root，从同一 revision 分别构建 `0.6.0-rc.1`。
- 每次构建都从锁定依赖和 source-bound AppHost publish 开始，不允许 `-SkipBuild`。
- 比较相对路径、file count、file size、逐文件 SHA-256、AppHost、ASAR、Desktop executable、manifest、notices、checksums 与 archive SHA-256。
- 时间戳或签名导致的差异必须在 manifest 中显式分类并有书面解释；runtime payload/inventory 或业务内容差异为 Blocker。
- 两份 candidate 分别执行 initialize、workspace、thread、fake run、approval、changes、report、artifact、preview、restart/crash/recovery、corrupt-state 与 cleanup smoke。
- smoke 必须不依赖 API key、网络、real model、real MCP 或 real Gerber/TIFF tools，且不得包含 test hook/source map/tests/cache/PDB/user config。
- 最终 acceptance 引用被 smoke 的 candidate hash；不可在 smoke 后重新打包并沿用旧结果。

## 8 项关键验收 Gate

Week 77 只能标记 `Accepted`，当且仅当：

- [ ] G1 Renderer 无 Node、任意文件或任意进程权限；security/package audit 高风险问题为 0。
- [ ] G2 Desktop write path 复用 .NET Application/现有安全策略，不解析 CLI 文本或复制策略。
- [ ] G3 Thread/timeline/approval/artifact contract 有 version、bounds、corrupt-state 和 protocol fuzz tests。
- [ ] G4 两份 clean-source candidate 的 fake-runtime packaged smoke 均完成完整任务聊天与复核闭环。
- [ ] G5 approval deny、cancel、crash、restart、orphan/process/temp cleanup 全部通过。
- [ ] G6 Changes、Reports、Artifacts、Gerber/TIFF Preview 不扩大 ownership/correctness；Narrator 与 visual/accessibility evidence 通过。
- [ ] G7 CLI 0.5.0 核心行为、0.6.0 version metadata、Release build/full suite/default smoke 无回归。
- [ ] G8 Desktop package 的 source revision、依赖/runtime、inventory、notices、checksums、AppHost identity 与双构建证据完整一致。

附加 hard Gate：

- [ ] 5 个连续独立 performance profiles 全部满足 idle retention `<=15%`，且 process/temp delta 为 0。
- [ ] `npm audit` 无 high/critical vulnerability，许可证/notices 无未知或缺失项。
- [ ] `git diff --check` 通过；source freeze 后 acceptance 工作树始终 clean。
- [ ] 创建 `docs_md/release/final_acceptance_0.6.0.md` 与 `docs_md/weekly/77_week_review.md`，记录首次失败和最终真实决定。

## Release Decision

- 全部 8 项 Gate、附加 hard Gate、双构建与默认 smoke 通过：标记 `Accepted`，同步 schedule、roadmap、capability status、CHANGELOG 与 final acceptance；tag/push/publish 仍等待用户单独授权。
- 任一安全、memory、Narrator、CLI regression、identity、reproducibility、payload 或 cleanup Gate 未关闭：标记 `Blocked`，保留证据，不生成或推广最终 release，不降低阈值。
- Real model、real MCP 与 real Gerber/TIFF tool smoke 未执行时记录 `Skipped/Unproven`；除非文档另有明确 Gate，它们不把默认 credential-free acceptance 升格或降格。

## 任务交付物

- Week 76 三个 blocker 的关闭证据或明确 Blocked 决定。
- 同 revision 的 accessibility、performance、protocol、security、package 和 smoke evidence JSON。
- 两份 clean-source Desktop candidate、manifest、inventory、notices、checksums 与 archive comparison。
- 0.6.0 CLI/Desktop version 与 release 文档更新。
- `docs_md/release/final_acceptance_0.6.0.md`。
- `docs_md/weekly/77_week_review.md`。

本文件只创建 Week 77 执行任务，不在本次操作中修改版本、运行 acceptance、生成 RC、创建 tag 或发布制品。
