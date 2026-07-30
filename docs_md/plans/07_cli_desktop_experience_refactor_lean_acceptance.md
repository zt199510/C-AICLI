# 阶段 07 精简验收规则

更新时间：2026-07-30

状态：`Active`

本规则取代 Week84–92 原 104 Gate 重型执行方式，目标是让验收服务于产品重构，而不是让证据生成阻塞开发。原 Gate schema、validator、历史 evidence 和本地合同分支保留用于追溯或专项诊断，但默认不执行，也不作为进入下一项产品工作的前置条件。

## 保留的最小必要验证

### 每个 Desktop 改动切片

- 运行 `npm run typecheck`。
- 只运行直接受影响的 Vitest 文件。
- UI 可运行时做一次本地 renderer 冒烟。
- 只有修改 Main、Preload、`desktop-v1` 或恢复生命周期时，才运行一次对应 Electron E2E。

### 每个 CLI 改动切片

- 构建 `src/CSharpAiCli.Cli/CSharpAiCli.Cli.csproj`。
- 只运行直接受影响的测试类或测试过滤器。
- 对被迁移命令执行一组文本/JSON/退出码兼容冒烟。

### 跨边界改动

- 修改 `desktop-v1`、权限、审批、路径约束、secret/redaction 或进程执行边界时，补跑对应契约或安全测试。
- 未修改上述边界时，不重复执行协议全矩阵、安全全矩阵或 packaged 验证。

### 里程碑验收

- W86、W89、W92 保留用户视觉验收。
- W92 候选仅运行一次 Desktop verify、一次 .NET test，以及必要的最终冒烟；独立命令可并行，但整个自动验收批次的墙钟时间同样不得超过 5 分钟。
- 真实 provider 默认不运行；只有产品 provider 路径发生改动且用户再次要求时才执行。

## 默认删除或跳过

- 每个 Gate 的 schema、hash、lineage、anchor、handoff 和 evidence 对账。
- `validate-week84-92-goal-evidence.py` 全量语义套件。
- 相同 Electron 场景的 3 次或 5 次重复采样。
- 每个小改动后的全量 `npm run verify`、全 solution test、package 和 packaged E2E。
- 未触及资源所有权时的 listener/private-bytes/forced-GC 性能矩阵。
- 未触及可访问性结构时的全 viewport、zoom、forced-colors 与 Narrator 重跑。
- 未触及协议或权限时的 fuzz、corrupt-state、navigation/permission 全矩阵。
- 为了生成报告而重复执行已经通过的产品测试。

## 已知非阻塞技术债

- Week83 `provider-recovery-listener-retention` P1 保留为非阻塞技术债，不再作为 UI/CLI 重构的前置条件。
- 未触及 Renderer subscription、AppHost recovery 或 Electron lifecycle 的改动不运行 listener 诊断。
- 若改动触及上述生命周期，只执行一次不超过 5 分钟的同场景对比；只有相对已知基线继续恶化时才进入修复。
- 未重新验证不得把该 P1 表述为已关闭。

## 时间与失败策略

- 任意单条验证命令硬上限为 5 分钟。
- 任意改动切片或里程碑的自动验证批次墙钟时间硬上限也为 5 分钟；可安全并行的命令应并行执行。
- 达到 5 分钟立即终止本次验证及其 owned 子进程，按验证性能问题记录；不得延长 timeout、转后台继续等待或无修改重跑。
- 命令挂起或重复失败时立即停止，记录具体失败并回到代码定位；不自动扩大到更重的测试矩阵。
- 目标测试通过只证明本次改动范围；未运行的可选矩阵不得表述为已通过。

## 完成标准

一个开发切片在以下条件下可继续：

1. 受影响项目能够编译或 typecheck。
2. 直接相关测试通过。
3. 用户可观察行为没有已知破坏。
4. 没有新增开放 P0/P1。
5. 若触及安全或协议边界，对应专项测试通过。

阶段最终完成仍要求 W92 在 5 分钟硬上限内完成一次回归和用户视觉验收，但不要求恢复 104 Gate 证据流水线。若回归无法在 5 分钟内完成，应修复或拆分验证性能问题，不能靠延长等待完成验收。
