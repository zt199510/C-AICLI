import { describe, expect, it } from "vitest";
import { DESKTOP_METHODS, DESKTOP_NOTIFICATIONS } from "../generated/desktop-contracts";
import {
  ARTIFACT_GET_REQUEST,
  ARTIFACT_LIST_REQUEST,
  CHANGES_GET_REQUEST,
  REPORT_GET_REQUEST,
  REPORT_LIST_REQUEST,
  REVIEW_LIST_PAGE_SIZE,
  THREAD_ARCHIVE_REQUEST,
  THREAD_CHANGED_NOTIFICATION,
  THREAD_CREATE_REQUEST,
  THREAD_GET_REQUEST,
  THREAD_LIST_PAGE_SIZE,
  THREAD_LIST_REQUEST,
  THREAD_RENAME_REQUEST,
  TIMELINE_PAGE_SIZE,
} from "./desktop-requests";

describe("reviewed desktop request descriptors", () => {
  it("binds only the Week 71 methods to generated validators", () => {
    expect([
      THREAD_LIST_REQUEST.method, THREAD_GET_REQUEST.method, THREAD_CREATE_REQUEST.method,
      THREAD_RENAME_REQUEST.method, THREAD_ARCHIVE_REQUEST.method, CHANGES_GET_REQUEST.method,
      REPORT_LIST_REQUEST.method, REPORT_GET_REQUEST.method, ARTIFACT_LIST_REQUEST.method,
      ARTIFACT_GET_REQUEST.method,
    ]).toEqual([
      DESKTOP_METHODS.ThreadListMethod, DESKTOP_METHODS.ThreadGetMethod, DESKTOP_METHODS.ThreadCreateMethod,
      DESKTOP_METHODS.ThreadRenameMethod, DESKTOP_METHODS.ThreadArchiveMethod, DESKTOP_METHODS.ChangesGetMethod,
      DESKTOP_METHODS.ReportListMethod, DESKTOP_METHODS.ReportGetMethod, DESKTOP_METHODS.ArtifactListMethod,
      DESKTOP_METHODS.ArtifactGetMethod,
    ]);
    expect(THREAD_CHANGED_NOTIFICATION.method).toBe(DESKTOP_NOTIFICATIONS.ThreadChangedNotification);
    expect([THREAD_LIST_PAGE_SIZE, TIMELINE_PAGE_SIZE, REVIEW_LIST_PAGE_SIZE]).toEqual([200, 100, 50]);
  });

  it("rejects Renderer-shaped parameters without Main injection", () => {
    expect(THREAD_LIST_REQUEST.isParams({ pageSize: 200 })).toBe(false);
    expect(THREAD_GET_REQUEST.isParams({ schemaVersion: 1, threadId: "x", afterSequence: 0, timelinePageSize: 101 })).toBe(false);
    expect(THREAD_CREATE_REQUEST.isParams({ schemaVersion: 1, title: "x", extra: true })).toBe(false);
  });
});
