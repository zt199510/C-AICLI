# 第 24 周 安装、快速开始与安全文档 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加安装、配置、安全、MVP/增强能力状态和快速开始文档。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 06 计划：`docs_md/plans/06_packaging_release_hardening.plan.md`
- 第 23 周回顾：`docs_md/weekly/23_week_review.md`

## 本周范围

- 安装文档。
- 配置文档。
- 安全和权限模型文档。
- MVP/增强能力状态矩阵。
- 快速开始覆盖 `doctor` 和 `chat`。

## 任务清单

- [x] Step 1: 添加 installation 文档。
- [x] Step 2: 添加 configuration 文档。
- [x] Step 3: 添加 security model 文档。
- [x] Step 4: 添加 quickstart 文档。
- [x] Step 5: 验证新用户路径命令。
- [x] Step 6: 创建 `24_week_review.md` 并更新总排期。

## 固化记录

- 新增 `docs_md/release/installation.md`，覆盖 Windows self-contained 包、构建命令、运行方式和本地数据路径。
- 新增 `docs_md/release/configuration.md`，覆盖环境变量、user/workspace config 优先级、workspace `apiKey` 禁用、backend、MCP 和 workflow profile 配置。
- 新增 `docs_md/release/security_model.md`，覆盖 workspace guard、patch 审批、shell 审批、密钥、日志、transcript 和 Deferred 增强边界。
- 新增 `docs_md/release/quickstart.md`，覆盖 `version`、`doctor`、`chat`、session、MCP/workflow inspect 路径。
- 新增 `docs_md/release/capability_status.md`，明确 MVP、Solidified、Accepted 和 Deferred 能力状态。
- 已用干净临时 user/workspace 验证发布产物 `version`、`doctor`、`chat` 缺 model 和缺 key路径。

## 验收标准

- 新用户可以按照文档运行 `doctor` 和 `chat`。
