## 第 28 周回顾

状态：已验收

已完成：
- 新增 `caicli exec "<task>"` 根命令入口。
- 支持 `--json` 与 `--output text|json`，默认 text；JSON 输出为 newline-delimited JSON events。
- 新增 exec core DTO：`ExecRequest`、`ExecResult`、`ExecEvent`、`ExecOutputMode`。
- 新增 exec text renderer 与 JSON renderer。
- 新增 `ExecRunner` deterministic fallback，覆盖 `read <path>`、`shell <command>`、`create smoke note`、空任务和 unsupported task。
- `exec` 复用现有 direct tool registry、approval policy、workspace guard 与 tool executor。
- `run` 保持旧输出格式，并内部改为调用 `ExecRunner`。
- 固化退出码：成功为 `0`，任务失败为 `1`，参数/解析错误为 `2`。
- 增加测试覆盖 text 输出、NDJSON 输出、失败事件、approval 拒绝、workspace 越界、run 兼容和 exit code。
- 更新 quickstart、capability status、known limitations。
- 使用 Subagent-Driven execution，并对实现任务进行 spec review 与 quality review。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test src/CSharpAiCli.sln -c Release --no-build`
- 结果：通过，354 tests passed，0 failed，0 skipped。
- 说明：首次并行运行 Release test 时与 Release build 发生 obj DLL 写入锁冲突；随后使用已完成的 Release 构建产物顺序重跑 `dotnet test src/CSharpAiCli.sln -c Release --no-build` 通过。

运行时说明：
- `caicli exec --workspace . "read README.md"` 输出 text events，并以 `result: success|failure` 收尾。
- `caicli exec --json --workspace . "read README.md"` 输出合法 NDJSON，每行一个 JSON event，最终 event 为 `exec.result`。
- `--output json` 与 `--json` 等价选择 JSON 输出；`--output text` 保持 text 输出。
- `create smoke note` 和 `shell <command>` 仍需要 `--approve`，否则返回 approval-denied failure event。
- `run` 是兼容 smoke/direct-task 入口，旧文本输出格式保持不变。
- 所有 exec events 不依赖 OpenAI API key；当前 exec runner 是 deterministic direct-tool fallback。

风险：
- `exec` 尚未实现完整模型工具循环或通用自然语言规划；第 29 周需要继续推进 agent run loop。
- text 输出包含 tool summary，仍依赖各 tool 返回 safe summary，不作为 secret redaction layer。
- event timestamp 当前使用运行时当前时间，不适合 golden snapshot 断言。
- `shell <command>` 虽有 approval 和 restricted runner，仍需要用户审查命令意图。

第 29 周输入：
- 实现 agent run loop v1，将 deterministic exec runner 与模型工具调用流程衔接。
- 明确 exec event schema 的长期兼容策略与版本化需求。
- 考虑为 exec JSON 输出增加更细粒度的 tool started/tool completed event。
- 继续扩大 approval、workspace boundary、shell failure 与 disabled tool 的 release smoke 覆盖。
