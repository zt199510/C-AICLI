# 运行时、日志与诊断说明

## Runtime

当前项目目标框架为 `net9.0`。当前验收和本机诊断环境使用 SDK `9.0.308`。当前仓库通过根目录 `global.json` 锁定 SDK `9.0.308`，并使用 `latestPatch` roll-forward 策略。

`doctor` 负责显示：

- target framework
- dotnet SDK
- dotnet runtime
- SDK lock 状态
- workspace 路径和状态
- 用户配置路径
- 工作区配置路径
- 日志目录
- API key 是否存在

## 配置来源

第 8 周仍沿用最小配置 schema：

以下 key 仅为示例值，不可作为真实凭据。

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-example"
}
```

配置优先级：

- API key：`OPENAI_API_KEY` > 用户配置 `apiKey` > missing
- model：`OPENAI_MODEL` > 用户配置 `model` > 工作区配置 `model` > `not configured`

工作区配置中的 `apiKey` 不进入 effective configuration；诊断和日志只记录 `ignored workspace config apiKey: <path>`，不记录原始 key。

报告和日志只打印 API key 的 `present` 或 `missing`，以及来源；不打印原始 key。

## 日志目录

当工作区状态是 `ready`：

```text
<workspace>/.caicli/logs
```

当工作区状态是 `missing` 或 `not directory`：

```text
<user profile>/.caicli/logs
```

命令日志按 UTC 日期写入：

```text
yyyy-MM-dd.log
```

每行日志记录：

- `timestampUtc`
- `command`
- `workspace`
- `workspaceStatus`
- `model`
- `modelSource`
- `baseUrl`
- `baseUrlSource`
- `agentBackend`
- `agentBackendSource`
- `agentBackendStatus`
- `apiKey`
- `apiKeySource`
- `warnings`
- `instructionWarnings`

日志不记录 `SecretValue.Value`。

## Week 37 Diagnostics And Trace Logs

Global `--verbose` is recursive and may be used on subcommands. It prints safe, human-readable diagnostics for text output, including command name, command/session ids, timestamp, workspace, log directory, config sources, warning counts, model/base URL/backend status, and API key presence/source. Commands that emit JSON (`--json` or `--output json`) must keep stdout JSON-clean and must not append verbose diagnostics to the JSON stream.

Global `--trace` is recursive. `CAICLI_TRACE=1` enables the same trace behavior without passing the flag. Trace logs are local JSONL files under `LogPathResolver.ResolveLogDirectory(snapshot)` and use this UTC date file name:

```text
yyyy-MM-dd.trace.log
```

Command logs continue to use:

```text
yyyy-MM-dd.log
```

Trace records share these core fields:

- `timestampUtc`
- `command`
- `commandId`
- `sessionId`
- `workspace`
- `type`
- `sequence`

Exec trace event/result records include `status`, `stepIndex`, `stopReason`, `durationMs`, `approvalDurationMs`, `errorCode`, `approvalStatus`, and `payload` when available. Payload keys are stable enough for diagnostics but must be treated as diagnostic data, not a public API contract.

For agentic `exec`, `status` is the coarse terminal outcome (`success`, `failure`, or `timeout`) and `stopReason` is the machine-readable terminal reason, such as `completed`, `max-steps-exceeded`, `max-tool-calls-exceeded`, `overall-timeout`, `model-timeout`, `tool-timeout`, `tool-disabled`, `approval-denied`, `tool-failure`, `model-error`, or `backend-unavailable`. `errorCode` remains the specific model/tool/runtime error code and is not interchangeable with `stopReason`.

Agent event order is shared by text output, NDJSON output, and trace output. `model.turn`, `tool.call`, and `tool.result` events can include `stepIndex`; terminal `final.response` and `agent.error` events include `stopReason`. Session transcript v1 is not a full event log, but agentic exec sessions add `agentRuns[]` summaries with final `status`, `stopReason`, optional `errorCode`, summary, event count, and tool call count.

Week 44 adds final review/report diagnostics for every agentic `exec` run:

- `review.gate` summarizes the final git diff through the read-only `git.diff` planning-phase path. Its payload includes `readOnly=true`, `toolName=git.diff`, `hasDiff`, and `truncated`. It must not call shell, patch, or workspace write tools.
- `taskReport` records the final report as an event. Its payload includes status, stop reason, prompt, plan, tools, workflow reference count/list, changed file count/list, command count/list, verification count/statuses, risk count/list, trace path when trace is enabled, optional review gate status, and secret presence metadata.
- The terminal `exec.result.payload.taskReport` contains the structured report payload used by JSON consumers and trace readers.
- Session transcript `agentRuns[]` stores the same task report summary object when `exec --session` is used. Markdown session export renders compact task report counts and trace path only.
- Full standalone markdown task report files are not written by default and remain deferred.

Week 47 adds workflow reference and changes-view diagnostics:

- `context.references` records bounded `@file` / `@folder` resolution for `exec`. Its payload includes reference count, included file count, skipped file count, byte count, truncation status, kinds, paths, and warning summaries. It is emitted before model/tool work for normal agent runs and before terminal failure for reference validation failures.
- `exec.result.payload.taskReport.references[]` records reference metadata only: kind, requested path, resolved path, status, included/skipped file counts, byte count, truncation flag, warnings, and optional error code. Raw referenced file content is not persisted in task reports, traces, or session exports.
- `caicli changes --output json` emits a single JSON object with `type="changes.view"`, `status`, `git`, `changedFiles`, optional `taskReport`, optional `session`, and `warnings`.
- `caicli changes --trace` or `CAICLI_TRACE=1` writes a redacted `changes.view` trace command event. `changes` does not write command logs by default.

Trace and verbose diagnostics must redact secrets before writing output. Redaction covers API keys, access/refresh tokens, passwords, `Authorization` headers and common variants, `secretKey`, `privateKey`, nested or escaped `argumentsJson`, OpenAI `sk-...` keys, and GitHub token formats such as `ghp_...` and `github_pat_...`. Diagnostics may record key presence and source, but never raw key values.

`logs path` prints the resolved CLI log directory. It must not create the directory just to print the path.

`logs show --tail <n>` reads existing direct `*.log` files in the resolved log directory, including command logs and `*.trace.log` trace files. The default tail is `20`; `n` must be positive. A missing log directory is an empty success. Locked, deleted, unreadable, or raced files are skipped best-effort. `logs show` must not mutate command logs for itself.

`logs clear` deletes only direct `*.log` files in the resolved log directory. It does not recurse and must preserve non-log files, subdirectories, workspace files, and other `.caicli` content. Locked, deleted, or unreadable files are skipped best-effort. It refuses to clear if the resolved log directory or any ancestor is a symlink/reparse point. `logs clear` must not mutate command logs for itself.

Known residual risk: parse-time `System.CommandLine` validation errors can return before trace context creation. This is accepted because no model/tool flow has begun.

## Chat 边界

第 4 周只添加 `chat` 的 Phase 02 边界提示。该命令用于告诉用户模型客户端和流式渲染器属于第 5-6 周，不执行模型调用。

`chat` 命令可以使用 `--workspace <path>`，用于显示与记录当前工作区上下文。
