# 第 60 周回顾

状态：已完成；Isolated Run Staging、Checkpoint 与 Safe Resume Foundation 为 0.5.0 source-only Preview，真实 Gerber/TIFF 转换仍 Deferred

## 范围结论

- 新增 schema-v1 `ProjectPackRunId`、run record、checkpoint、stage event、correlation、artifact pointer、
  stable error code 和封闭 transition table。状态覆盖 `created/discovered/staged/ready/running/verifying/
  awaiting-acceptance/accepted/rejected/failed/canceled/interrupted`。
- 新增 `%USERPROFILE%\.caicli\runs\<run-id>` managed layout：`run.json`、`checkpoint.json`、sanitized
  `plan.json`、`input-manifest.json`、`staging/working/logs/artifacts/reports`。状态根可由
  `CAICLI_USER_PROFILE` 重定向。
- run id 在路径拼接前验证；managed root 做 canonical containment 和 existing-chain reparse 检查。
  run/checkpoint 使用同目录临时文件、flush-to-disk、atomic rename、短时独占 mutation lock 和 revision
  check。missing/corrupt/unknown-field/unsupported-schema/id/revision/state/fingerprint 不一致均 fail closed。
- staging 只复制 Week 59 inventory 的 supported 文件，unknown 不复制。每项在复制前重验 workspace
  relative path、kind、layer role、tool-input flag、size、SHA256；目标使用扁平安全 rename mapping 和
  create-new；复制后复核 source/destination SHA256。源输入不写入，explicit workspace output 不创建。
- pre-run/resume 重新构建 Week 59 deterministic plan，复核 plan id/fingerprint、input、static tool identity、
  no-overwrite output 和 policy fingerprint。plan managed copy 只保留运行所需安全字段；unknown/raw/
  approval 字段不原样持久化。
- checkpoint 固定 `approvalPersisted=false`，不保存 approval token/override、raw argv、raw input、secret
  或 absolute portable tool path。`staged/ready` 仅在重验后可继续且未来 execute 必须重新审批；
  `verifying` 还要求 managed artifact hash 完整；`awaiting-acceptance` 仅允许后续人工 gate；
  `running/interrupted` 只返回 `pack-run-restart-required`，不自动 replay execute。
- in-process fake driver 通过同一 state machine 覆盖 success/failure/timeout/cancel/partial-output/
  interrupted。fake success 只到 `awaiting-acceptance`，summary 明确真实 conversion、TIFF verification 和
  acceptance 均未证明；partial output 始终是 failed evidence。
- 新增 `packs run gerber-tiff --plan <workspace-json> --dry-run`、`packs runs show`、`packs cancel` 和
  `packs resume --dry-run`。Week 60 CLI 不暴露 fake execute；未传 `--dry-run` 在创建 job/run 前拒绝。
- 每次 CLI dry-run 预创建一个普通 job，run record 关联 job id；job 仅保存 `project-pack-run` 和
  `project-pack-input-manifest` artifact pointers，`TaskReport` 保持 null。可选 queue id 只作为已校验
  correlation schema；没有新增 Project Pack queue family 或 worker。

## Fake Driver 阶段证据

- success：`ready -> running -> verifying -> awaiting-acceptance`；render/encode/inspect stage event 为
  succeeded，但 managed output kind 明确为 `fake-driver-evidence`。
- failure：terminal `failed`，error `pack-run-driver-failed`。
- timeout：terminal `failed`，error `pack-run-driver-timeout`。
- cancel：terminal `canceled`，error `pack-run-driver-canceled`。
- partial output：保留 `fake-partial-evidence` pointer，terminal `failed`，error
  `pack-run-driver-partial-output`。
- interrupted：state `interrupted`，`restartRequired=true`；resume 只返回
  `explicit-restart-decision`，不触发 driver。
- 这些结果只验证运行时阶段契约，不是 Gerber/TIFF 真实业务验证通过。

## 测试与验证

- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --filter
  "FullyQualifiedName~SmokeTestScriptTests|FullyQualifiedName~ProjectPackCliTests|FullyQualifiedName~ProjectPackRunRuntimeTests"`
- 最终定向结果：31 passed，0 failed，0 skipped。
- 本周新增 20 tests；覆盖 transition、atomic/corrupt/unsupported store、restart read、precreated-run conflict、concurrent lock conflict、
  bounded staging/source preservation/safe mapping、fake 六类结果、cancel/resume、policy/input/tool/output drift、
  staged tamper、unknown approval field、plan sanitization、managed-root reparse、CLI/job/artifact correlation 和
  packaged smoke contract。
- build 命令：`$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build
  src\CSharpAiCli.sln -c Release`
- build 结果：0 warnings，0 errors。
- full test 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- full test 最终结果：1211 passed，0 failed，0 skipped；比 Week 59 的 1191 增加 20。最终验证首轮曾有
  1 个既有 `McpDoctorReportTests.Create_reports_stdio_initialize_timeout_as_unavailable` 时序断言失败
  （elapsed 2.551s，其余 1210 passed）；该测试单独复跑 495ms 通过，随后同一 full test 命令 1211/1211
  通过。本周未修改 MCP 实现或该测试。
- package 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
  -OutputRoot artifacts\week60-validation`
- validation zip：44572980 bytes，SHA256
  `D9DBA69DFEB8AC9148A531711738398E5D748FAFE9825B185FD342FD6EBEE846`。
- validation exe：49923487 bytes，SHA256
  `1DB0F9ED1567A5E273788A76EC36729CEAE169EA6A6396B4C4B1976AE5431596`。
- default smoke：显式移除 `CAICLI_REAL_MODEL_SMOKE`、`CAICLI_DAEMON_SMOKE` 和
  `CAICLI_GERBER_TIFF_TOOL_SMOKE` 后，对 validation executable 运行
  `tools\Invoke-SmokeTests.ps1 -ExecutablePath <validation-exe>`。
- smoke 结果：`smoke tests passed`；real model、daemon/API、real Gerber/TIFF tool 分支均明确 skipped。
- Project Pack packaged smoke 实际验证 plan -> dry-run staging -> show -> resume eligibility -> cancel ->
  terminal resume；source/staged SHA256 相同、unknown 未复制、artifact directory 为空、job `taskReport=null`、
  atomic `*.tmp` 为 0、`caicli/gerbv/magick` process set 不变。
- 最终 cleanup：`caicli/gerbv/magick` process count 均为 0；`caicli-smoke-*`、
  `caicli-pack-probe-*` 和相关 `*.tmp` count 均为 0；`git diff --check` 通过，仅有既有 LF -> CRLF warning。
- validation package 是本周 source Preview smoke evidence，不是新的 0.4.0 Accepted artifact，也未覆盖
  `artifacts/release` 中既有 0.4.0 文件。

## Accepted / Preview / Deferred

- Accepted：0.4.0 release decision 不变；本周未改变版本号，也未把 Week 60 source Preview 写入 0.4.0
  Accepted。
- Preview（0.5.0 source only）：managed run id/layout、canonical/reparse guard、atomic run/checkpoint store、
  immutable input manifest、bounded staging、plan/input/tool/output/policy revalidation、state machine、fake
  stage events、cancel/resume eligibility、job/run/artifact correlation、CLI dry-run/show/cancel/resume 和
  credential-free packaged smoke。
- Gate Passed evidence：Week 58 的 Gerbv 2.13.0、ImageMagick 7.1.2-27、LibTIFF 4.5.1、CC0 fixture、
  fixed typed argv 与许可结论不变；本周没有调用这些真实工具。
- Deferred：真实 Gerbv/ImageMagick adapter execution、TIFF metadata/content/baseline verification、preview、
  human accept/reject command、execute restart decision command、artifact export/prune、real-tool smoke、
  ZIP/network input、repo hooks、scheduler、parallel/concurrent worker、cross-process lease/heartbeat、remote
  runner、provider routing、API control/SSE、team platform、marketplace 和 UI。

## 风险

- `run.json` 与 `checkpoint.json` 是两个原子文件而不是文件系统事务；进程在两次 rename 之间崩溃会
  产生 revision/state mismatch。读取会 fail closed，需要人工检查，不会猜测较新的状态。
- mutation lock 只保护短写操作，revision 防止 stale update；它不是 worker ownership lease，也不恢复
  被终止的 running process。Week 61 不得把它扩写为后台并发 worker。
- Week 60 cancel 只验证 checkpoint terminal 语义，因为 CLI 没有启动真实外部进程。真实 process
  cancellation、timeout、tree cleanup、bounded stdout/stderr 必须由 Week 61 structured adapter 验证。
- run/job artifact pointer 包含本机 user-level managed path；虽然 secret-like 内容会 redaction，本地路径仍是
  敏感 metadata。portable report 不应复制 absolute path。
- policy fingerprint 是当前 pack/schema/approval mode 与固定 safety invariants 的摘要，不是 approval grant。
  Week 61 仍需在每个 external stage 前调用当前 approval policy，不能仅比较 fingerprint。
- `awaiting-acceptance` 目前只由 fake driver service test 到达；没有 accept/reject CLI，也没有 TIFF verifier。
  不得把该状态或 fake artifact 当作真实输出可验收。
- 既有 MCP initialize timeout 测试对主机调度有小概率时序波动；本周最终单测与 full rerun 均通过，
  但 Week 61 验证仍应关注该非 Project Pack flake，不能把偶发失败静默从计数中删除。

## Week 61 冻结前置条件

- 直接复用 `ManagedProjectPackRunStore`、`ProjectPackRunService`、run/checkpoint DTO、transition table、
  `ProjectPackInputManifest`、staging mapping 和 stable error codes；不要建立第二套 run/stage/inventory truth。
- 真实 adapter 只接收 pack-owned typed executable/arguments。Gerbv/ImageMagick executable 和 argv 必须来自
  Week 58 固定契约，不接受 model/prompt/skill/workspace manifest/env extra args 扩展，不调用 shell。
- adapter 只读取 managed staging 中 manifest 声明且 hash 已验证的文件；cwd、TEMP/TMP、environment、
  output roots 必须绑定到当前 managed run。explicit workspace output 仍 no-overwrite，managed artifact 与
  external workspace pointer 必须区分。
- 每个 external stage 启动前重新验证 plan/input/tool/output/policy，并通过当前 `IApprovalPolicy` 重新申请
  shell-risk approval。checkpoint/job/session/report 均不得保存 approval token、override 或批准结果作为
  后续 bypass。
- 复用 Week 58 probe 的 `ProcessStartInfo.ArgumentList`、`UseShellExecute=false`、environment clear/allowlist、
  bounded stdout/stderr、timeout、cancellation、`Kill(entireProcessTree)`/Windows cleanup 和 post-execution tool
  identity recheck。进程 cleanup 失败必须是 terminal error evidence。
- running/interrupted execute 不得进入自动 resume。real adapter 遇到崩溃、timeout、cancel 或 partial output
  必须保留 bounded evidence、进入明确 failed/canceled/interrupted 状态，并要求后续显式 restart decision。
- default tests/smoke 继续 model-free、network-free、real-tool-free；真实工具 smoke 只能通过
  `CAICLI_GERBER_TIFF_TOOL_SMOKE=1`、caller-provided installed tool paths 和当前 approval 单独启用，并记录
  tool name/version/SHA256。fake success、exit 0、文件存在、metadata valid 或 preview 不得写成真实业务验证。
- Week 61 仍不实现 TIFF verifier、accept/reject、prune、scheduler、parallel worker、lease/heartbeat、remote
  worker 或 API control；这些边界按 Week 62-64 排期处理。
