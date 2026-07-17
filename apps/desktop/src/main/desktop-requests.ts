import {
  DESKTOP_METHOD_METADATA,
  DESKTOP_METHODS,
  DESKTOP_NOTIFICATIONS,
  isArtifactGetParams,
  isArtifactGetResult,
  isArtifactListParams,
  isArtifactListResult,
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
  type ChangesGetParams,
  type ChangesGetResult,
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

export const THREAD_CHANGED_NOTIFICATION = Object.freeze({
  method: DESKTOP_NOTIFICATIONS.ThreadChangedNotification,
  isParams: isThreadChangedParams as (value: unknown) => value is ThreadChangedParams,
});

export const THREAD_LIST_PAGE_SIZE = 200 as const;
export const TIMELINE_PAGE_SIZE = 100 as const;
export const REVIEW_LIST_PAGE_SIZE = 50 as const;
