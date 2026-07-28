import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { DesktopBridge } from "../shared/bridge-contract";
import { TerminalPanel } from "./TerminalPanel";

describe("user terminal panel", () => {
  it("requires explicit open, input, cancel and close actions", async () => {
    const openTerminal = vi.fn(async () => result("running"));
    const inputTerminal = vi.fn(async () => result("running", "terminal-user-sentinel\n"));
    const cancelTerminal = vi.fn(async () => result("exited", "terminal-user-sentinel\n", 130));
    const closeTerminal = vi.fn(async () => result("closed", "terminal-user-sentinel\n", 130));
    window.caicli = { openTerminal, inputTerminal, cancelTerminal, closeTerminal,
      getTerminal: vi.fn(async () => result("running", "terminal-user-sentinel\n")) } as unknown as DesktopBridge;
    render(<TerminalPanel workspaceReady />);

    await userEvent.click(screen.getByRole("button", { name: "Open terminal" }));
    await userEvent.type(screen.getByRole("textbox", { name: "Terminal input" }), "echo terminal-user-sentinel");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(inputTerminal).toHaveBeenCalledWith(expect.objectContaining({ text: "echo terminal-user-sentinel\n" }));
    expect(await screen.findByText(/terminal-user-sentinel/)).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Cancel process" }));
    expect(cancelTerminal).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole("button", { name: "Close terminal" }));
    expect(closeTerminal).toHaveBeenCalledOnce();
    expect(screen.queryByText(/terminal-user-sentinel/)).toBeNull();
    expect(screen.getByText("Closed")).toBeTruthy();
  });

  it("preserves the terminal DOM structure across its full lifecycle", async () => {
    window.caicli = {
      openTerminal: vi.fn(async () => result("running")),
      inputTerminal: vi.fn(async () => result("running", "terminal-user-sentinel\n")),
      cancelTerminal: vi.fn(async () => result("exited", "terminal-user-sentinel\n", 130)),
      closeTerminal: vi.fn(async () => result("closed", "terminal-user-sentinel\n", 130)),
      getTerminal: vi.fn(async () => result("running", "terminal-user-sentinel\n")),
    } as unknown as DesktopBridge;
    const view = render(<TerminalPanel workspaceReady />);
    const output = view.container.querySelector(".terminal-output");
    const outputText = output?.firstChild;
    const structuralMutations: MutationRecord[] = [];
    const observer = new MutationObserver((records) => structuralMutations.push(...records.filter((record) =>
      record.type === "childList" && [...record.addedNodes, ...record.removedNodes].some((node) => node.nodeType === Node.ELEMENT_NODE))));
    observer.observe(view.container, { childList: true, subtree: true });

    await userEvent.click(screen.getByRole("button", { name: "Open terminal" }));
    await userEvent.type(screen.getByRole("textbox", { name: "Terminal input" }), "terminal-user-sentinel");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel process" }));
    await userEvent.click(screen.getByRole("button", { name: "Close terminal" }));
    observer.disconnect();

    expect(structuralMutations).toHaveLength(0);
    expect(outputText).toBeInstanceOf(Text);
    expect(output?.firstChild).toBe(outputText);
  });

  it("materializes only a bounded tail of terminal scrollback", async () => {
    const tail = "terminal-tail-sentinel\n";
    const scrollback = `${"x".repeat((64 * 1024) - tail.length)}${tail}`;
    window.caicli = {
      openTerminal: vi.fn(async () => result("running")),
      inputTerminal: vi.fn(async () => result("running", scrollback)),
      getTerminal: vi.fn(async () => result("running", scrollback)),
    } as unknown as DesktopBridge;
    const view = render(<TerminalPanel workspaceReady />);

    await userEvent.click(screen.getByRole("button", { name: "Open terminal" }));
    await userEvent.type(screen.getByRole("textbox", { name: "Terminal input" }), "long-output");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));

    const output = view.container.querySelector(".terminal-output");
    expect(output?.textContent).toContain("[earlier output truncated]");
    expect(output?.textContent).toContain("terminal-tail-sentinel");
    expect(output?.textContent.length).toBeLessThanOrEqual((8 * 1024) + 64);
  });

  it("does not open without a workspace", () => {
    window.caicli = {} as DesktopBridge;
    render(<TerminalPanel workspaceReady={false} />);
    expect((screen.getByRole("button", { name: "Open terminal" }) as HTMLButtonElement).disabled).toBe(true);
  });
});

function result(status: "running" | "exited" | "closed", output = "", exitCode: number | null = null) {
  return { schemaVersion: 1 as const, succeeded: true, data: {
    sessionId: "terminal_0123456789abcdef", status, shellProfile: "system-default", output,
    cursor: output.length, truncated: false, exitCode, startedAtUtc: "2026-07-18T00:00:00Z",
    exitedAtUtc: status === "running" ? null : "2026-07-18T00:00:01Z",
  }, error: null, diagnostics: [], truncated: false };
}
