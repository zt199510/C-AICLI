# Week 95+ 验收：Desktop Codex Parity V1.4

状态：`ImplementedWithKnownLimitations`

日期：2026-08-06

权威需求：`docs_md/spec/codex_app_ui_parity_blueprint_v1.md` V1.4（D1–D19）

## 结论

C-AICLI Desktop 已从只读/占位表面推进为可打包的 Windows 桌面闭环：三面板、连续对话、真实 ConPTY、Changes 写操作、Branch/Worktree/Compare/GitHub PR、两级 Settings、Slash command、受控附件、不可变消息 Edit/Branch 来源链、以及显式创建的 Sub-agent 均接入 typed IPC 和 AppHost 权威路径。

严格按“完整图像多模态等同 Windows ChatGPT Desktop Codex”判定时仍有一项已知限制：图片附件已执行类型签名校验、内容哈希快照、任务隔离和发送时重校验，但当前 `AgentRunRequest`/OpenAI provider adapter 仍是文本输入合同，只向模型提示词提供图片的受管引用、大小与 SHA-256，没有把像素作为 provider 的 `input_image` 内容部分发送。因此本次不宣称图像理解已完全对齐。

## D1–D19 结果矩阵

| 决策 | 结果 | 权威证据 |
|---|---|---|
| D1–D4 基线、范围、品牌、三面板 | 通过 | 组件化 PanelTop/PanelBottom/PanelRight；workspace 级持久化；未复制 ChatGPT 品牌资产 |
| D5、D8 Full PTY | 通过 | Windows ConPTY、PowerShell/cmd/WSL/Git Bash profile、多会话、resize/input/Ctrl+C/cancel/close、进程回收 |
| D6、D9、D10 Changes | 通过 | porcelain/diff/file+hunk；stage/unstage/revert；commit hooks；push/upstream；stale revision；无 force |
| D7、D13 三面板与视觉 | 通过 | 1440/1024/800 结构矩阵；唯一 1024×768 golden 以零像素容差复验 |
| D11 其他产品面 | 通过 | Skills/Experts/Automation 保留；Reports/Artifacts/Preview 可达；Settings 与工具注册表分层 |
| D12 Composer/对话 | 通过 | 连续多轮、任务草稿隔离、slash command、Copy/Retry/Continue/Edit/Branch |
| D14 Terminal 隐私 | 通过 | PTY 默认不进 Agent；只分享选区；预览、脱敏、8 KiB 截断；有界内存 transcript |
| D15 Worktree | 通过 | 仓库外受管目录、`caicli/` 分支、owner/thread 登记、dirty/unmerged 拒绝删除、接管保留 |
| D16 PR | 通过 | provider-neutral UI；首发 `gh`；Draft create、title/body update、browser open；不保存 PAT |
| D17 Local Settings | 通过 | Main 进程验证 JSON；user/workspace precedence；model/approval/disabled tools/default shell 只影响下一 Turn；Renderer 不保存凭据 |
| D18 文本/代码附件与消息历史 | 通过 | 外部附件为 task/thread/content-hash 只读快照；文本真实注入模型提示；Edit/Branch 新建 Thread 并持久化精确 `thread-message` source pointer |
| D18 图片附件模型视觉 | **受限** | 图片快照和身份校验通过；provider 尚未接入 `input_image`，不宣称像素理解完成 |
| D19 Sub-agent | 通过（会话内调度） | 每主任务 3 个上限、禁止嵌套、只读共享、写入独占 Worktree、独立审批、显式 start/cancel、takeover 先停后交接；线程与 Worktree 证据保留 |

## 验收命令与结果

- `.NET` 全量：`dotnet test src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj --no-restore` → **1452/1452 通过**。
- Desktop 全量：`npm run verify` → contract/notices/accessibility/typecheck/lint/unit/build/security 全部通过；Vitest **211/211**。
- 响应式矩阵：`playwright test e2e/chat-ui-visual.spec.ts --project=unpacked` → **1/1 通过**，覆盖 1440×900、1024×768、800×900 与多状态矩阵。
- 精确基线：`playwright test e2e/codex-parity-golden.spec.ts --project=unpacked` → **1/1 通过**，1024×768 零容差。
- 真实 PTY：`playwright test e2e/full-pty.spec.ts --project=unpacked` → **1/1 通过**。
- Git 写闭环：`playwright test e2e/changes-write.spec.ts --project=unpacked` → **1/1 通过**；只操作临时仓库和本地 bare remote。
- Packaged E2E：`playwright test --project=packaged` → **8/8 通过**。
- 包审计：**78 files、12 asar entries、48 invoke channels、2 event channels、0 forbidden payloads**。
- AppHost：`CSharpAiCli.AppHost.exe`，80,129,584 bytes，SHA-256 `4B42C2CD84DDAC9BB8AEFF66C48BB38630E68273AE96954C89D9626CFCC7EE46`。
- Desktop protocol canonical SHA-256：`12b21308ab7c9ea9a33c739eafc7b9ab4fc46bcff6777030f46f9ca6df2543b9`。

## 安全与仓库约束

- 当前仓库未执行 stage、revert、commit、push、force push 或 PR 创建。
- Git mutation E2E 仅使用独占临时仓库和本地 bare remote。
- Provider 测试使用 deterministic fake；未发送真实 API 请求，未读取或写入明文凭据。
- 打包产物位于 `apps/desktop/out/C-AICLI Desktop-win32-x64`。

## 后续解除限制

要把状态提升为无条件完整对齐，需要扩展 Core 的 `AgentRunRequest` 与 OpenAI Responses gateway，使受管图片快照以有界的 `input_image` 内容部分进入首个用户消息，并补充 fake-gateway contract test 与真实 provider 手工验收。该工作不得把 base64、原图或凭据写入 durable thread JSON、日志或 Renderer 状态。
