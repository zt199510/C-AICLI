import {
  Bot,
  FileText,
  GitBranch,
  GitCompare,
  GitPullRequest,
  HardDrive,
  Package,
  ScanLine,
  Send,
  SquareTerminal,
} from "lucide-react";
import type { ReviewState } from "../desktop-state";

export type WorkspacePanel =
  | "changes"
  | "local"
  | "branch"
  | "terminal"
  | "git-actions"
  | "pull-request"
  | "compare"
  | "worktrees"
  | "reports"
  | "artifacts"
  | "preview"
  | "agents";

export type ReviewPanelId = Extract<WorkspacePanel, "changes" | "reports" | "artifacts" | "preview">;
export type ToolStatus = "unknown" | "loading" | "ready" | "warning" | "error" | "disabled";

export interface ToolDefinition {
  readonly id: WorkspacePanel;
  readonly label: string;
  readonly icon: typeof GitCompare;
  readonly status: ToolStatus;
  readonly statusLabel: string;
  readonly badge?: number | string;
  readonly disabledReason?: string;
  readonly selectable: boolean;
}

export interface ToolGroup {
  readonly id: "environment" | "evidence" | "agents";
  readonly label: string;
  readonly tools: readonly ToolDefinition[];
}

export function buildWorkspaceToolGroups(
  review: ReviewState,
  workspaceReady: boolean,
  terminalReady: boolean,
): readonly ToolGroup[] {
  const changesStatus = reviewStatus(review, "changes", Boolean(review.changes));
  const reportsStatus = reviewStatus(review, "reports", review.reports.length > 0);
  const artifactsStatus = reviewStatus(review, "artifacts", review.artifacts.length > 0);
  const previewStatus = reviewStatus(review, "preview", review.artifacts.length > 0);
  const branch = review.changes?.branch ?? detectBranch(review.changes?.gitStatusSummary);
  const gitWritable = Boolean(review.changes?.repositoryId && review.changes?.revision);
  return [
    {
      id: "environment",
      label: "Environment",
      tools: [
        tool("changes", "Changes", GitCompare, workspaceReady ? changesStatus : "disabled", review.changes?.dirty ? review.changes.changedFiles.length || 1 : undefined, workspaceReady ? undefined : "Open a workspace to inspect changes."),
        tool("local", "Local", HardDrive, workspaceReady ? "ready" : "disabled", workspaceReady ? "Connected" : undefined, workspaceReady ? undefined : "Open a workspace to inspect the local environment."),
        tool("branch", branch ?? "Branch", GitBranch, branch ? "ready" : "unknown"),
        tool("worktrees", "Worktrees", GitBranch, gitWritable ? "ready" : "unknown", review.changes?.worktrees?.length),
        tool("terminal", "Terminal", SquareTerminal, workspaceReady && terminalReady ? "ready" : "disabled", undefined, workspaceReady ? "Terminal commands are unavailable." : "Open a workspace to use the terminal."),
        tool("git-actions", "Commit or push", Send, gitWritable ? "ready" : "unknown"),
        tool("pull-request", "Pull request", GitPullRequest, review.changes?.ghAvailable ? "ready" : "unknown", review.changes?.pullRequest?.number),
        tool("compare", "Compare branches", GitCompare, review.changes?.compareBase ? "ready" : "unknown"),
      ],
    },
    {
      id: "evidence",
      label: "Results and evidence",
      tools: [
        tool("reports", "Reports", FileText, workspaceReady ? reportsStatus : "disabled", countBadge(reportsStatus, review.reports.length), workspaceReady ? undefined : "Open a workspace to review reports."),
        tool("artifacts", "Artifacts", Package, workspaceReady ? artifactsStatus : "disabled", countBadge(artifactsStatus, review.artifacts.length), workspaceReady ? undefined : "Open a workspace to review artifacts."),
        tool("preview", "Preview", ScanLine, workspaceReady ? previewStatus : "disabled", previewStatus === "ready" ? "Available" : undefined, workspaceReady ? undefined : "Open a workspace to review previews."),
      ],
    },
    {
      id: "agents",
      label: "Sub-agents",
      tools: [tool("agents", "Activity", Bot, workspaceReady ? "ready" : "disabled", workspaceReady ? "Explicit start" : undefined, workspaceReady ? undefined : "Open a workspace to manage Sub-agents.")],
    },
  ];
}

export function isSelectableTool(tool: ToolDefinition): boolean {
  return tool.selectable;
}

export function isReviewWorkspacePanel(panel: WorkspacePanel): panel is ReviewPanelId {
  return panel === "changes" || panel === "reports" || panel === "artifacts" || panel === "preview";
}

export function nextEnabledTool(tools: readonly ToolDefinition[], index: number, direction: 1 | -1): number {
  for (let offset = 1; offset <= tools.length; offset++) {
    const candidate = (index + offset * direction + tools.length) % tools.length;
    if (isSelectableTool(tools[candidate]!)) return candidate;
  }
  return index;
}

export function lastEnabledTool(tools: readonly ToolDefinition[]): number {
  for (let index = tools.length - 1; index >= 0; index--) {
    if (isSelectableTool(tools[index]!)) return index;
  }
  return 0;
}

export function formatToolBadge(value: number | string): string {
  if (typeof value === "number" && value > 999) return "999+";
  return String(value);
}

export function detectBranch(summary: string | undefined): string | null {
  const match = summary?.match(/^##\s+([^\s.]+)(?:\.\.\.|\s|$)/m);
  return match?.[1] ?? null;
}

function tool(
  id: WorkspacePanel,
  label: string,
  icon: ToolDefinition["icon"],
  status: ToolStatus,
  badge?: number | string,
  disabledReason?: string,
  selectable = status !== "disabled",
): ToolDefinition {
  return { id, label, icon, status, statusLabel: statusLabel(status), badge, disabledReason, selectable };
}

function reviewStatus(review: ReviewState, panel: ReviewPanelId, hasData: boolean): ToolStatus {
  if (review.activeTab === panel && review.status === "loading") return "loading";
  if (review.activeTab === panel && review.status === "error") return "error";
  if (hasData || (review.activeTab === panel && review.status === "ready")) return "ready";
  return "unknown";
}

function statusLabel(status: ToolStatus): string {
  if (status === "loading") return "加载中";
  if (status === "ready") return "就绪";
  if (status === "warning") return "需要处理";
  if (status === "error") return "不可用";
  if (status === "disabled") return "未接通";
  return "未知";
}

function countBadge(status: ToolStatus, count: number): number | undefined {
  return status === "ready" ? count : undefined;
}
