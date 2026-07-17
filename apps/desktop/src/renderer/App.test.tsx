import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { App } from "./App";

describe("desktop shell", () => {
  beforeEach(() => {
    Object.defineProperty(window, "innerWidth", { value: 1440, configurable: true, writable: true });
    window.caicli = bridge();
  });

  it("reads the runtime snapshot without initializing AppHost from the renderer", async () => {
    render(<App />);
    expect(await screen.findByText("AppHost ready")).toBeTruthy();
    expect(window.caicli.getRuntimeStatus).toHaveBeenCalledOnce();
  });

  it("offers an explicit restart after failure", async () => {
    window.caicli = bridge(createRuntimeStatus("apphost-exited"));
    render(<App />);
    const restart = await screen.findByRole("button", { name: "Restart AppHost" });
    await userEvent.click(restart);
    expect(window.caicli.restartRuntime).toHaveBeenCalledOnce();
  });

  it("opens and collapses shell panels", async () => {
    render(<App />);
    await userEvent.click(screen.getByRole("button", { name: "Collapse threads" }));
    expect(screen.getByRole("button", { name: "Show threads" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Close runtime inspector" }));
    expect(screen.getByRole("button", { name: "Show runtime inspector" })).toBeTruthy();
  });

  it("keeps narrow drawers closed and mutually exclusive", async () => {
    window.innerWidth = 760;
    render(<App />);
    expect(screen.getByRole("button", { name: "Show threads" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Show runtime inspector" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Show threads" }));
    expect(screen.queryByRole("button", { name: "Show runtime inspector" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Show runtime inspector" }));
    expect(screen.getByRole("button", { name: "Show threads" })).toBeTruthy();
  });
});

function bridge(status = createRuntimeStatus("runtime-ready")): DesktopBridge {
  return {
    getRuntimeStatus: vi.fn(async () => status),
    restartRuntime: vi.fn(async () => createRuntimeStatus("runtime-ready")),
    openWorkspace: vi.fn(async () => null),
    onRuntimeStatus: vi.fn(() => () => undefined),
  };
}
