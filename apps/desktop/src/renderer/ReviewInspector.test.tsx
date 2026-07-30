import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ArtifactMetadataData } from "../generated/desktop-contracts";
import type { ReviewState } from "./desktop-state";
import { ReviewInspector, type ReviewCommands } from "./ReviewInspector";

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

  it("routes artifact and human-decision commands through the supplied boundary", async () => {
    const commands: ReviewCommands = {
      previewArtifact: vi.fn(async () => "Preview ready."),
      verifyArtifact: vi.fn(async () => "Identity verified."),
      exportArtifact: vi.fn(async () => "Export canceled."),
      loadGerber: vi.fn(async () => gerberReview),
      decideGerber: vi.fn(async () => ({ ...gerberReview, revision: 8, state: "accepted", decision: "accepted", humanDecisionEligible: false })),
    };
    const view = render(<ReviewInspector review={{ ...review, activeTab: "artifacts", artifacts: [artifact], selectedArtifact: artifact }} workspaceReady onTab={vi.fn()} onReport={vi.fn()} onArtifact={vi.fn()} commands={commands} />);
    await userEvent.click(screen.getByRole("button", { name: "Verify identity" }));
    expect(commands.verifyArtifact).toHaveBeenCalledWith("artifact-1");
    expect(await screen.findByText("Identity verified.")).toBeTruthy();

    view.rerender(<ReviewInspector review={{ ...review, activeTab: "preview", artifacts: [artifact], selectedArtifact: artifact }} workspaceReady onTab={vi.fn()} onReport={vi.fn()} onArtifact={vi.fn()} commands={commands} />);
    await userEvent.click(screen.getByRole("button", { name: "Load verification" }));
    await userEvent.click(await screen.findByRole("button", { name: "Accept verified run" }));
    expect(commands.decideGerber).toHaveBeenCalledWith("run-1", 7, "Reviewed in Desktop", true);
  });

  it("connects tabs to their panel and supports arrow-key navigation", async () => {
    const onTab = vi.fn();
    render(<ReviewInspector review={review} workspaceReady onTab={onTab} onReport={vi.fn()} onArtifact={vi.fn()} />);
    const changesTab = screen.getByRole("tab", { name: "Changes" });
    changesTab.focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(onTab).toHaveBeenCalledWith("reports");
    expect(changesTab.getAttribute("aria-controls")).toBe("review-panel-changes");
    expect(screen.getByRole("tabpanel").getAttribute("aria-labelledby")).toBe("review-tab-changes");
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

const gerberReview = {
  runId: "run-1", revision: 7, state: "awaiting-human", hardVerificationPassed: true,
  humanDecisionEligible: true, previewAvailable: true, correctnessProof: false, decision: null,
  disabledReason: null, verificationArtifactId: "artifact-1", previewArtifactIds: ["artifact-1"],
} as const;
