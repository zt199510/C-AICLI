# Project Pack v1 Contract

更新时间：2026-07-14

状态：Week 58 contract frozen；仅 `packs list/doctor` skeleton 可实现

## 定位

Project Pack 是本地、确定性、pack-owned 的领域工具流程声明。它定义输入类型、外部依赖、
固定阶段、验证器、artifact 类型和诊断，但不等同于 skill，也不是新的 agent runtime、queue、
job、pipeline、automation 或 report truth。

Week 58 只冻结 contract 和安全诊断入口。`run/resume/accept/reject` 是命令族保留名，
真实执行、staging、TIFF verification、artifact prune 和 repo hooks 均未实现。

## 职责边界

| 能力 | 决策来源 | 是否调用模型 | 是否拥有领域 argv | 是否拥有执行状态 | v1 职责 |
|---|---|---:|---:|---:|---|
| Skill | 本地 skill manifest + 用户任务 | 可以 | 否 | 否 | 给模型 instructions、references、expert/report 和 validation hint；不能授予权限 |
| Pipeline | 固定 pipeline catalog | 各 role 可以 | 否 | 只聚合既有 queue/job 证据 | 串行编排 agent/skill/queue/job；不实现领域转换 |
| Project Pack | 编译期 pack manifest 与 adapter | list/doctor/plan 不调用；执行也不需要模型 | 是，仅 pack 编译期固定模板 | 后续复用既有 job/artifact/report | 确定性输入、依赖、typed stages、validators、artifact schema 和 diagnostics |
| Automation | workspace-local data manifest | 取决于已选 target | 否 | 否 | 显式触发稳定的 skill/pipeline/pack；不包含领域逻辑、脚本或 approval override |

Project Pack 不替代 pipeline：pipeline 可以把一个 pack 的稳定入口作为未来 target，但不能
改写 pack 的真实工具参数。Project Pack 也不替代 skill：skill 可以解释结果或辅助人工 review，
但模型文本不能成为外部工具 executable、argv、cwd、env 或 output path。

## 安全所有权

pack adapter 只能从以下来源构造真实工具调用：

1. 编译期声明的 dependency id 和 executable filename allowlist。
2. 编译期固定的 typed argument template。
3. 已通过 bounded discovery/plan 的逻辑输入与 managed output slots。
4. 当前运行重新取得的 canonical tool identity、input hash、policy 与 approval。

禁止把 prompt、skill instructions、model tool call、workspace manifest 自定义字段、repo script、
environment extra args 或 resume checkpoint 中的旧 approval 拼入真实 argv。

所有 probe/execute 必须使用 typed executable 与 argument list，不经过 `cmd /c`、PowerShell
command string 或任意 shell expansion。cwd、environment、stdout/stderr、运行时间、文件数量、
文件大小和 process tree cleanup 都必须显式 bounded。

## 通用 contract

通用类型位于 `CSharpAiCli.ProjectPacks`，Gerber/TIFF 具体构造位于
`CSharpAiCli.ProjectPacks/GerberTiff`。通用 DTO 包含：

- `ProjectPackManifest`
- `ProjectPackCapability`
- `ExternalToolRequirement`
- `ExternalToolIdentity`
- `ProjectPackPlan`
- `ProjectPackStage`
- `ProjectPackArtifactDeclaration`
- `ProjectPackDiagnostic`
- `IProjectPack`
- `ProjectPackRegistry`

通用字段只能表达 id、display metadata、schema version、dependency、capability、stage、artifact、
hash、version、trust/probe status 和 bounded diagnostic。通用 DTO 不出现 Gerber 扩展名列表、
TIFF page/DPI/compression/pixel fields或用户机器绝对路径。

领域值可以由具体 pack 填入通用 id/metadata 字段，例如 Gerber/TIFF pack 的 capability id；
这不把领域字段加入通用 schema。TIFF inspection 的具体 DTO 延后到 Week 62 并留在
`GerberTiff` namespace。

## Manifest v1

`schemaVersion` 固定为 `1`。manifest 具有稳定 pack id、display name、pack version、description、
capabilities、external tool requirements、ordered stage declarations 和 artifact declarations。

注册时必须拒绝：

- 非 v1 schema。
- 重复 pack/capability/dependency/stage/artifact id。
- stage 引用未声明 capability 或 dependency。
- dependency probe 没有 typed argv、timeout 或 output bound。
- stage 缺少 approval 声明或声明为可自动重放真实 execute。

registry 只注册编译期 pack 实例，不扫描 workspace、不下载 pack、不加载 remote plugin，
也不建立 marketplace/signing/update 系统。

## Plan 与 stage

`ProjectPackPlan` 是 immutable deterministic projection，不是 shell script。它只保存 pack id、
plan id、input identities/hashes、ordered stages、artifact slots、diagnostics 和 redaction summary；
不保存 approval bypass、secret、raw model text、用户绝对路径或任意 argv。

`ProjectPackStage` 声明：

- stable stage id / kind
- capability id
- optional dependency id
- requires approval
- restart policy
- expected artifact ids

真实 executable 和 argv 由对应 pack adapter 在 stage 执行时根据固定模板生成，不进入可由模型
编辑的 plan DTO。外部执行 stage 默认 `manual-reapproval`，崩溃后不得自动重放。

## Artifact 与 report

`ProjectPackArtifactDeclaration` 只声明 logical id、role、media type、required/managed flags。
它不是 artifact pointer，也不包含文件系统路径。后续运行复用 0.4.0 job/artifact pointer、trace、
session、task report 和 redaction，不创建第二套最终报告真相。

preview、文件存在、metadata valid 或 fake driver success 都只是证据项，不能单独把业务状态标成
Accepted。人工 accept/reject 计划在 Week 63 建立，并引用同一 job/pack run/artifact evidence。

## Tool identity 与 trust

静态 identity 至少包含 dependency id、file name、file size、SHA256、last-write UTC、source、
trust status 和可选 probe version；不包含 canonical absolute path。canonical path 只在本地执行
上下文短暂存在，text/JSON 默认输出不回显。

静态 doctor：

- 只解析显式/user config path，检查 regular file、filename allowlist、reparse chain、size 和 hash。
- 不启动进程，不请求 approval，不写 workspace/job/session/trace/report。

显式 `--probe`：

- 静态检查通过后才申请 `shell` risk approval。
- approval request 只显示 file name、dependency、hash、probe argv identity，不显示 secret/path。
- 使用 dependency 固定 probe argv、bounded temp cwd/env/stdout/stderr/timeout/cancel/cleanup。
- probe 后重新 hash；文件变化使 identity 失效。

没有 expected hash 时状态为 `untrusted`，不是失败但不能执行转换；匹配为 `trusted`；不匹配为
`hash-changed` 并阻止 probe/execute。Week 58 不持久化 trust 或 approval。resume/restart 必须重新
执行全部 identity/input/output/policy 检查并重新申请 approval。

## CLI contract

本周实现：

```text
caicli packs list [--output text|json]
caicli packs doctor <pack> [--tool-path [<dependency>=]<absolute-path>]...
  [--probe] [--approval never|on-request|on-failure|always]
```

`packs list` 和默认 doctor 不调用模型、不启动工具、不访问网络。JSON 顶层 `type` 分别为
`packs.list` 和 `packs.doctor`，`schemaVersion` 为 `1`，数组排序稳定，diagnostic 使用稳定 code。

命令族保留但本周不实现：

```text
packs plan
packs run
packs resume
packs accept
packs reject
```

## Fake driver protocol

fake driver 只用于默认 test/smoke，不能作为真实工具 Gate。协议是单进程、单次调用、UTF-8
JSON stdout，固定 `protocolVersion: 1`，status 仅允许 `succeeded`、`failed`、`partial-output`。
结果只含 logical output id、size/hash 和 bounded diagnostics，不含机器绝对路径。

timeout/cancel 时进程可能没有完整 JSON；runner 必须终止 process tree 并以本地稳定 error code
结束。`partial-output` 永远是失败证据，不得升级为 succeeded。默认测试不得下载、安装或探测
Gerbv/ImageMagick，也不得访问模型或网络。

## 后续复用

- Week 59：复用 manifest/registry/doctor contract，实现 bounded inventory/preflight/plan；仍不转换。
- Week 60：复用 0.4.0 job/artifact pointers 建立 managed staging/checkpoint；不持久化 approval bypass。
- Week 61：adapter 才接入本 Gate 已验证的真实 fixed argv。
- Week 62：增加 Gerber/TIFF-specific TIFF inspection DTO 与 verification。
- Week 63：复用 artifact pointer/report/job，增加人工 gate 和 managed prune。
