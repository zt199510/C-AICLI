import {
  DESKTOP_METHOD_METADATA,
  DESKTOP_METHODS,
  DESKTOP_NOTIFICATIONS,
  isCatalogListParams,
  isCatalogListResult,
  isComposerClearParams,
  isComposerEnqueueParams,
  isComposerGetParams,
  isComposerStateResult,
  isTurnStartParams,
  isTurnCancelParams,
  isApprovalResolveParams,
  isTurnResumeParams,
  isTurnRestartParams,
  isTurnExecutionStateResult,
  isContextResolveParams,
  isContextResolveResult,
  isContextSearchParams,
  isContextSearchResult,
  isArtifactGetParams,
  isArtifactGetResult,
  isArtifactListParams,
  isArtifactListResult,
  isTerminalOpenParams, isTerminalInputParams, isTerminalResizeParams, isTerminalMutationParams, isTerminalGetParams, isTerminalStateResult,
  isArtifactReviewParams, isArtifactReviewResult, isArtifactExportParams, isArtifactExportResult,
  isGerberReviewParams, isGerberDecisionParams, isGerberReviewResult,
  isChangesGetParams,
  isChangesGetResult,
  isInitializeResult,
  isReportGetParams,
  isReportGetResult,
  isReportListParams,
  isReportListResult,
  isShutdownResult,
  isThreadArchiveParams,
  isThreadChangedParams,
  isThreadCreateParams,
  isThreadGetParams,
  isThreadGetResult,
  isThreadListParams,
  isThreadListResult,
  isThreadRenameParams,
  isThreadSummaryResult,
  isWorkspaceOpenResult,
  type ArtifactGetParams,
  type ArtifactGetResult,
  type ArtifactListParams,
  type ArtifactListResult,
  type TerminalOpenParams, type TerminalInputParams, type TerminalResizeParams, type TerminalMutationParams, type TerminalGetParams, type TerminalStateResult,
  type ArtifactReviewParams, type ArtifactReviewResult, type ArtifactExportParams, type ArtifactExportResult,
  type GerberReviewParams, type GerberDecisionParams, type GerberReviewResult,
  type ChangesGetParams,
  type ChangesGetResult,
  type CatalogListParams,
  type CatalogListResult,
  type ComposerClearParams,
  type ComposerEnqueueParams,
  type ComposerGetParams,
  type ComposerStateResult,
  type TurnStartParams,
  type TurnCancelParams,
  type ApprovalResolveParams,
  type TurnResumeParams,
  type TurnRestartParams,
  type TurnExecutionStateResult,
  type ContextResolveParams,
  type ContextResolveResult,
  type ContextSearchParams,
  type ContextSearchResult,
  type InitializeResult,
  type ReportGetParams,
  type ReportGetResult,
  type ReportListParams,
  type ReportListResult,
  type ShutdownResult,
  type ThreadArchiveParams,
  type ThreadChangedParams,
  type ThreadCreateParams,
  type ThreadGetParams,
  type ThreadGetResult,
  type ThreadListParams,
  type ThreadListResult,
  type ThreadRenameParams,
  type ThreadSummaryResult,
  type WorkspaceOpenResult,
} from "../generated/desktop-contracts";

export type TimeoutClass = "initialize" | "query" | "mutation" | "shutdown";

export interface DesktopRequestDescriptor<TParams extends object, TResult> {
  readonly method: string;
  readonly timeoutClass: TimeoutClass;
  readonly isParams: (value: unknown) => value is TParams;
  readonly isResult: (value: unknown) => value is TResult;
}

const anyObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

function descriptor<TParams extends object, TResult>(
  method: string,
  timeoutClass: TimeoutClass,
  isParams: (value: unknown) => value is TParams,
  isResult: (value: unknown) => value is TResult,
): DesktopRequestDescriptor<TParams, TResult> {
  return Object.freeze({ method, timeoutClass, isParams, isResult });
}

export const INITIALIZE_REQUEST = descriptor<Record<string, unknown>, InitializeResult>(
  DESKTOP_METHODS.InitializeMethod,
  DESKTOP_METHOD_METADATA.InitializeMethod.timeout,
  anyObject,
  isInitializeResult,
);

export const WORKSPACE_OPEN_REQUEST = descriptor<Record<string, unknown>, WorkspaceOpenResult>(
  DESKTOP_METHODS.WorkspaceOpenMethod,
  DESKTOP_METHOD_METADATA.WorkspaceOpenMethod.timeout,
  anyObject,
  isWorkspaceOpenResult,
);

export const SHUTDOWN_REQUEST = descriptor<Record<string, unknown>, ShutdownResult>(
  DESKTOP_METHODS.ShutdownMethod,
  DESKTOP_METHOD_METADATA.ShutdownMethod.timeout,
  anyObject,
  isShutdownResult,
);

export const THREAD_LIST_REQUEST = descriptor<ThreadListParams, ThreadListResult>(
  DESKTOP_METHODS.ThreadListMethod,
  DESKTOP_METHOD_METADATA.ThreadListMethod.timeout,
  isThreadListParams,
  isThreadListResult,
);
export const THREAD_GET_REQUEST = descriptor<ThreadGetParams, ThreadGetResult>(
  DESKTOP_METHODS.ThreadGetMethod,
  DESKTOP_METHOD_METADATA.ThreadGetMethod.timeout,
  isThreadGetParams,
  isThreadGetResult,
);
export const THREAD_CREATE_REQUEST = descriptor<ThreadCreateParams, ThreadSummaryResult>(
  DESKTOP_METHODS.ThreadCreateMethod,
  DESKTOP_METHOD_METADATA.ThreadCreateMethod.timeout,
  isThreadCreateParams,
  isThreadSummaryResult,
);
export const THREAD_RENAME_REQUEST = descriptor<ThreadRenameParams, ThreadSummaryResult>(
  DESKTOP_METHODS.ThreadRenameMethod,
  DESKTOP_METHOD_METADATA.ThreadRenameMethod.timeout,
  isThreadRenameParams,
  isThreadSummaryResult,
);
export const THREAD_ARCHIVE_REQUEST = descriptor<ThreadArchiveParams, ThreadSummaryResult>(
  DESKTOP_METHODS.ThreadArchiveMethod,
  DESKTOP_METHOD_METADATA.ThreadArchiveMethod.timeout,
  isThreadArchiveParams,
  isThreadSummaryResult,
);
export const CATALOG_LIST_REQUEST = descriptor<CatalogListParams, CatalogListResult>(
  DESKTOP_METHODS.CatalogListMethod,
  DESKTOP_METHOD_METADATA.CatalogListMethod.timeout,
  isCatalogListParams,
  isCatalogListResult,
);
export const CONTEXT_SEARCH_REQUEST = descriptor<ContextSearchParams, ContextSearchResult>(
  DESKTOP_METHODS.ContextSearchMethod,
  DESKTOP_METHOD_METADATA.ContextSearchMethod.timeout,
  isContextSearchParams,
  isContextSearchResult,
);
export const CONTEXT_RESOLVE_REQUEST = descriptor<ContextResolveParams, ContextResolveResult>(
  DESKTOP_METHODS.ContextResolveMethod,
  DESKTOP_METHOD_METADATA.ContextResolveMethod.timeout,
  isContextResolveParams,
  isContextResolveResult,
);
export const COMPOSER_GET_REQUEST = descriptor<ComposerGetParams, ComposerStateResult>(
  DESKTOP_METHODS.ComposerGetMethod,
  DESKTOP_METHOD_METADATA.ComposerGetMethod.timeout,
  isComposerGetParams,
  isComposerStateResult,
);
export const COMPOSER_ENQUEUE_REQUEST = descriptor<ComposerEnqueueParams, ComposerStateResult>(
  DESKTOP_METHODS.ComposerEnqueueMethod,
  DESKTOP_METHOD_METADATA.ComposerEnqueueMethod.timeout,
  isComposerEnqueueParams,
  isComposerStateResult,
);
export const COMPOSER_CLEAR_REQUEST = descriptor<ComposerClearParams, ComposerStateResult>(
  DESKTOP_METHODS.ComposerClearMethod,
  DESKTOP_METHOD_METADATA.ComposerClearMethod.timeout,
  isComposerClearParams,
  isComposerStateResult,
);
export const TURN_START_REQUEST = descriptor<TurnStartParams, TurnExecutionStateResult>(
  DESKTOP_METHODS.TurnStartMethod, DESKTOP_METHOD_METADATA.TurnStartMethod.timeout, isTurnStartParams, isTurnExecutionStateResult,
);
export const TURN_CANCEL_REQUEST = descriptor<TurnCancelParams, TurnExecutionStateResult>(
  DESKTOP_METHODS.TurnCancelMethod, DESKTOP_METHOD_METADATA.TurnCancelMethod.timeout, isTurnCancelParams, isTurnExecutionStateResult,
);
export const APPROVAL_RESOLVE_REQUEST = descriptor<ApprovalResolveParams, TurnExecutionStateResult>(
  DESKTOP_METHODS.ApprovalResolveMethod, DESKTOP_METHOD_METADATA.ApprovalResolveMethod.timeout, isApprovalResolveParams, isTurnExecutionStateResult,
);
export const TURN_RESUME_REQUEST = descriptor<TurnResumeParams, TurnExecutionStateResult>(
  DESKTOP_METHODS.TurnResumeMethod, DESKTOP_METHOD_METADATA.TurnResumeMethod.timeout, isTurnResumeParams, isTurnExecutionStateResult,
);
export const TURN_RESTART_REQUEST = descriptor<TurnRestartParams, TurnExecutionStateResult>(
  DESKTOP_METHODS.TurnRestartMethod, DESKTOP_METHOD_METADATA.TurnRestartMethod.timeout, isTurnRestartParams, isTurnExecutionStateResult,
);
export const CHANGES_GET_REQUEST = descriptor<ChangesGetParams, ChangesGetResult>(
  DESKTOP_METHODS.ChangesGetMethod,
  DESKTOP_METHOD_METADATA.ChangesGetMethod.timeout,
  isChangesGetParams,
  isChangesGetResult,
);
export const REPORT_LIST_REQUEST = descriptor<ReportListParams, ReportListResult>(
  DESKTOP_METHODS.ReportListMethod,
  DESKTOP_METHOD_METADATA.ReportListMethod.timeout,
  isReportListParams,
  isReportListResult,
);
export const REPORT_GET_REQUEST = descriptor<ReportGetParams, ReportGetResult>(
  DESKTOP_METHODS.ReportGetMethod,
  DESKTOP_METHOD_METADATA.ReportGetMethod.timeout,
  isReportGetParams,
  isReportGetResult,
);
export const ARTIFACT_LIST_REQUEST = descriptor<ArtifactListParams, ArtifactListResult>(
  DESKTOP_METHODS.ArtifactListMethod,
  DESKTOP_METHOD_METADATA.ArtifactListMethod.timeout,
  isArtifactListParams,
  isArtifactListResult,
);
export const ARTIFACT_GET_REQUEST = descriptor<ArtifactGetParams, ArtifactGetResult>(
  DESKTOP_METHODS.ArtifactGetMethod,
  DESKTOP_METHOD_METADATA.ArtifactGetMethod.timeout,
  isArtifactGetParams,
  isArtifactGetResult,
);
export const TERMINAL_OPEN_REQUEST = descriptor<TerminalOpenParams, TerminalStateResult>(DESKTOP_METHODS.TerminalOpenMethod, "mutation", isTerminalOpenParams, isTerminalStateResult);
export const TERMINAL_INPUT_REQUEST = descriptor<TerminalInputParams, TerminalStateResult>(DESKTOP_METHODS.TerminalInputMethod, "mutation", isTerminalInputParams, isTerminalStateResult);
export const TERMINAL_RESIZE_REQUEST = descriptor<TerminalResizeParams, TerminalStateResult>(DESKTOP_METHODS.TerminalResizeMethod, "mutation", isTerminalResizeParams, isTerminalStateResult);
export const TERMINAL_CANCEL_REQUEST = descriptor<TerminalMutationParams, TerminalStateResult>(DESKTOP_METHODS.TerminalCancelMethod, "mutation", isTerminalMutationParams, isTerminalStateResult);
export const TERMINAL_CLOSE_REQUEST = descriptor<TerminalMutationParams, TerminalStateResult>(DESKTOP_METHODS.TerminalCloseMethod, "mutation", isTerminalMutationParams, isTerminalStateResult);
export const TERMINAL_GET_REQUEST = descriptor<TerminalGetParams, TerminalStateResult>(DESKTOP_METHODS.TerminalGetMethod, "query", isTerminalGetParams, isTerminalStateResult);
export const ARTIFACT_PREVIEW_REQUEST = descriptor<ArtifactReviewParams, ArtifactReviewResult>(DESKTOP_METHODS.ArtifactPreviewMethod, "query", isArtifactReviewParams, isArtifactReviewResult);
export const ARTIFACT_EXPORT_REQUEST = descriptor<ArtifactExportParams, ArtifactExportResult>(DESKTOP_METHODS.ArtifactExportMethod, "mutation", isArtifactExportParams, isArtifactExportResult);
export const ARTIFACT_VERIFY_REQUEST = descriptor<ArtifactReviewParams, ArtifactReviewResult>(DESKTOP_METHODS.ArtifactVerifyMethod, "query", isArtifactReviewParams, isArtifactReviewResult);
export const GERBER_REVIEW_GET_REQUEST = descriptor<GerberReviewParams, GerberReviewResult>(DESKTOP_METHODS.GerberReviewGetMethod, "query", isGerberReviewParams, isGerberReviewResult);
export const GERBER_PREVIEW_REQUEST = descriptor<GerberReviewParams, GerberReviewResult>(DESKTOP_METHODS.GerberPreviewMethod, "query", isGerberReviewParams, isGerberReviewResult);
export const GERBER_ACCEPT_REQUEST = descriptor<GerberDecisionParams, GerberReviewResult>(DESKTOP_METHODS.GerberAcceptMethod, "mutation", isGerberDecisionParams, isGerberReviewResult);
export const GERBER_REJECT_REQUEST = descriptor<GerberDecisionParams, GerberReviewResult>(DESKTOP_METHODS.GerberRejectMethod, "mutation", isGerberDecisionParams, isGerberReviewResult);

export const THREAD_CHANGED_NOTIFICATION = Object.freeze({
  method: DESKTOP_NOTIFICATIONS.ThreadChangedNotification,
  isParams: isThreadChangedParams as (value: unknown) => value is ThreadChangedParams,
});

export const THREAD_LIST_PAGE_SIZE = 200 as const;
export const TIMELINE_PAGE_SIZE = 100 as const;
export const REVIEW_LIST_PAGE_SIZE = 50 as const;
