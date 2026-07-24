# Week 78 执行回顾：0.6.0 Preview 真实项目试用

状态：Blocked

日期：2026-07-24

试用源码提交：`fa13cb12d246476bbca07f2f41d11f4cae68d48f`

## 最终结论

Week 78 已按计划完成可执行部分并以 `Blocked` 收尾，不是 `Preview Ready`。credential-free 基线、CLI 真实模型只读、固定版本 filesystem stdio MCP、最终 package 自动化与清理检查通过；Desktop 真实模型 Gate 失败，因为生产 AppHost 的 `turn.start` 固定使用 `DeterministicFakeTurnExecutionRuntime`，没有注册可调用真实模型的生产 runtime。

计划禁止把 fake evidence 外推为真实能力。因此 Phase 4 真实模型受控写入和 Phase 5 真实模型 crash/restart 在前置 Gate 失败后均未启动，三个 disposable workspace 保持 clean。0.6.0 正式 release 继续保持 `Blocked / Preview`，0.5.0 仍是最后 Accepted 版本。

## 真实模型 CLI 首败、修复与重跑

首次真实模型请求在工具执行前被 endpoint 拒绝：内部工具名 `workspace.read_text` 含点号，不符合 Responses function name 只允许字母、数字、下划线和连字符的约束。首败 evidence 已保留，随后完成以下小范围修复：

- 提交 `39f4d1a`：为 Responses 出站工具建立确定性 API-safe alias，并把返回调用映射回 canonical 内部名称；加入冲突和长度回归。
- 提交 `9a25ee0`：真实模型 smoke 隔离并恢复 `OPENAI_BASE_URL`，避免无凭据用例继承第三方 gateway。
- 提交 `1ad7ea4`：endpoint 不支持 `previous_response_id` 续接时，使用原始输入、function call 和 function output 重建无状态续接历史。
- 提交 `fa13cb1`：真实模型 smoke 仅暴露 `workspace.read_text`，使 bounded read-only 场景不会先消费计划或写入工具调用预算。

最终 CLI smoke 使用 `approval never`，两轮内只调用一次 `workspace.read_text`，结构化结果成功，fixture 字节未变化，owned process delta 为 0。真实模型标识为用户指定的 `gpt-5.6-sol`；evidence 未保存 key、raw prompt、raw response、请求头或绝对路径。

## Desktop 真实模型阻塞

源码预检确认 `DesktopWriteExecutionSupervisor` 的默认构造路径直接创建 `DeterministicFakeTurnExecutionRuntime`，仓库内也没有第二个 `ITurnExecutionRuntime` 生产实现。该 runtime 生成固定的 plan/model/tool/verification/changes/final 时间线，不读取授权模型配置，也不会发出 provider request。

因此：

- Desktop credential-free E2E 仍是有效的协议、Renderer、ThreadStore、approval 和恢复回归；
- 它不能满足 Week 78 Phase 3.2 的真实模型要求；
- 没有把固定 fake timeline、Reports 或 Changes 标记成 real-model Passed；
- 没有为制造 crash 而终止任何不属于真实模型场景的进程；
- 这是阻止 W78-G3、G5、G6 和 G8 关闭的产品能力缺口。

下一步必须先实现生产 Desktop runtime：复用受支持的 Responses agent path，把模型、tool、verification 和 final 事件映射到 durable timeline，并把 write/shell 审批桥接到现有持久化 `IInteractiveApprovalGateway`。完成 deterministic tests 后，从新的 clean revision 重跑 Phase 3.2、4、5、6。

## 固定版本 filesystem stdio MCP

用户授权的 `@modelcontextprotocol/server-filesystem@2026.7.10` 仅在 disposable project A 配置和运行。最终重跑结果：

- static list 与 live initialize/doctor 通过；
- 发现 14 个工具，其中 7 个具有写能力，仅审计、未调用；
- 唯一成功调用为 `read_text_file`，预声明的 fixture identity 匹配；
- project B 越界读取和 unknown tool 均 fail closed；
- user config 逐字节恢复，project A clean，server process delta 为 0。

最终重跑第一次使用 PowerShell stdin 传 JSON 时得到 `invalid-tool-arguments`。同一 JSON 改为临时 arguments file 后场景通过，文件在 finally 中删除。该传参摩擦保留为 P2，不影响 MCP server、workspace containment 或清理结论。

## 最终自动化矩阵

所有下列结果绑定试用源码提交 `fa13cb1`：

- .NET：`1403/1403`，0 failed，0 skipped。
- Desktop verify：`23/23 files`、`102/102 tests`；contracts、notices、accessibility、typecheck、lint、production security 和 build 通过。
- `npm audit --audit-level=high`：0 vulnerabilities。
- package security：78 个文件、12 个 ASAR entry、39 个 invoke channel、2 个 event channel、forbidden payload 0。
- unpacked E2E：`9/9`；packaged E2E：`8/8`。
- accessibility evidence：`2/2`，process/temp delta `0/0`。
- packaged smoke：`8/8`，process/temp delta `0/0`。
- protocol：`3/3`，每轮 `48/48` response，process/temp delta `0/0`。
- 最终 package SHA-256：`E5A5EA65B5F593D0A1CD03EC29CDDE220C14A0E79D69BACC3B06C097D73C2EEC`。
- 最终 AppHost SHA-256：`D6D005B4362861462A156D22B80B9367BDF32074910D964D5B8573F3F71DBE66`。

此前 source revision 的一次 packaged smoke 首轮曾因 2 秒 case-local cleanup 检查点报告剩余 PID 而失败；外层 cleanup 随后为 0，静止状态单次重跑 `8/8`。首败保留为 Windows cleanup timing flake，不覆盖最终源码 identity。

完整 .NET 最终重跑前还有两次未进入测试的操作错误：系统 PATH 只解析到 SDK 10.0.302，以及首次给出了错误的 solution 相对路径。改用用户本地固定 SDK 9.0.308 与 `src/CSharpAiCli.sln` 后，正式测试为 `1403/1403`。

## 资源、隐私与工作区终态

三个 disposable workspace 均位于冻结 revision `fa13cb1`，Git status clean。CLI real-model、MCP、accessibility、packaged smoke 与 protocol 的 measured owned-process delta 均为 0；MCP 临时 user config 已逐字节回滚。未观察到未经授权的文件写入、网络目标、重复执行、悬空 approval 或错误 workspace。

Phase 6 不能声明完整通过，因为没有真实 provider-backed Desktop 长会话可测。unpacked long-session 回归已在最终 package 上通过，但只作为 credential-free 生命周期证据，不替代真实模型资源观测。

用户曾在对话中直接提供 API key；该值未写入跟踪文件或 evidence，只存在 ignored dotenv。建议在试用结束后撤销并轮换该 key。

## Gate 结果

- Passed：W78-G0、G1、G2、G4、G7。
- Failed/Blocked：W78-G3、G5、G6、G8。
- 最终决定：**Blocked**。

没有创建 tag、没有上传制品、没有发布 Release，也没有把 Desktop 描述为 Accepted、Released 或 Production Ready。
