import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ArtifactMetadataData } from "../generated/desktop-contracts";
import type { ReviewState } from "./desktop-state";
import { ReviewInspector } from "./ReviewInspector";

describe("read-only review inspector", () => {
  it("never turns projected artifact paths into links", async () => {
    const onArtifact = vi.fn();
    render(<ReviewInspector review={{ ...review, activeTab: "artifacts", artifacts: [artifact], selectedArtifact: artifact }} workspaceReady onTab={vi.fn()} onReport={vi.fn()} onArtifact={onArtifact} />);
    expect(screen.getByText("file:///sentinel-secret")).toBeTruthy();
    expect(screen.queryByRole("link")).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: /gerber/i }));
    expect(onArtifact).toHaveBeenCalledWith("artifact-1");
  });

  it("presents managed preview metadata with a correctness disclaimer", () => {
    render(<ReviewInspector review={{ ...review, activeTab: "preview", artifacts: [artifact], selectedArtifact: artifact }} workspaceReady onTab={vi.fn()} onReport={vi.fn()} onArtifact={vi.fn()} />);
    expect(screen.getByText(/do not prove manufacturing or image correctness/i)).toBeTruthy();
    expect(screen.getByText("verified", { selector: "dd" })).toBeTruthy();
  });
});

const artifact: ArtifactMetadataData = {
  artifactId: "artifact-1", pointerId: "pointer-1", kind: "gerber-preview", ownership: "managed", relativePath: "file:///sentinel-secret",
  size: 10, sha256: "a".repeat(64), availability: "available", verification: "verified", runState: "completed",
  declaredAtUtc: "2026-07-17T00:00:00.000Z", updatedAtUtc: "2026-07-17T00:00:00.000Z",
  owner: { runId: "run-1", jobId: null, queueId: null, rootRunId: null, parentRunId: null, attempt: 1 },
  retention: { class: "managed", owned: true, prunable: false, defaultMinimumAgeDays: 7 },
};

const review: ReviewState = { activeTab: "changes", status: "ready", error: null, changes: null, reports: [], selectedReport: null, artifacts: [], selectedArtifact: null, truncated: false };
