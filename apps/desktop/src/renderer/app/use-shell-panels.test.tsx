import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { useShellPanels } from "./use-shell-panels";

const originalViewportWidth = window.innerWidth;

afterEach(() => {
  setViewportWidth(originalViewportWidth);
});

describe("useShellPanels navigation width", () => {
  it("starts each hook session at 288 pixels without persisted state", () => {
    const first = renderHook(() => useShellPanels());
    expect(first.result.current.navigationWidth).toBe(288);

    act(() => first.result.current.nudgeNavigationWidth(48));
    expect(first.result.current.navigationWidth).toBe(336);
    first.unmount();

    const second = renderHook(() => useShellPanels());
    expect(second.result.current.navigationWidth).toBe(288);
  });

  it("resizes from the left-edge pointer coordinate and clamps to 240-360 pixels", () => {
    const { result } = renderHook(() => useShellPanels());

    act(() => result.current.resizeNavigationAt(319.6));
    expect(result.current.navigationWidth).toBe(320);

    act(() => result.current.resizeNavigationAt(120));
    expect(result.current.navigationWidth).toBe(240);

    act(() => result.current.resizeNavigationAt(480));
    expect(result.current.navigationWidth).toBe(360);
  });

  it("nudges within the bounds and resets to the session default", () => {
    const { result } = renderHook(() => useShellPanels());

    act(() => result.current.nudgeNavigationWidth(24));
    expect(result.current.navigationWidth).toBe(312);

    act(() => result.current.nudgeNavigationWidth(100));
    expect(result.current.navigationWidth).toBe(360);

    act(() => result.current.nudgeNavigationWidth(-500));
    expect(result.current.navigationWidth).toBe(240);

    act(() => result.current.resetNavigationWidth());
    expect(result.current.navigationWidth).toBe(288);
  });
});

describe("useShellPanels responsive panel behavior", () => {
  it("keeps the navigation and inspector mutually exclusive on narrow viewports", () => {
    setViewportWidth(800);
    const { result } = renderHook(() => useShellPanels());

    expect(result.current.leftOpen).toBe(false);
    expect(result.current.inspectorOpen).toBe(false);

    act(() => result.current.showThreads());
    expect(result.current.leftOpen).toBe(true);
    expect(result.current.inspectorOpen).toBe(false);

    act(() => result.current.showInspector());
    expect(result.current.leftOpen).toBe(false);
    expect(result.current.inspectorOpen).toBe(true);
  });

  it("preserves the navigation width while viewport layout changes", () => {
    setViewportWidth(1200);
    const { result } = renderHook(() => useShellPanels());

    act(() => result.current.resizeNavigationAt(340));
    act(() => {
      setViewportWidth(800);
      window.dispatchEvent(new Event("resize"));
    });

    expect(result.current.navigationWidth).toBe(340);
    expect(result.current.leftOpen).toBe(false);
    expect(result.current.inspectorOpen).toBe(false);

    act(() => {
      setViewportWidth(1000);
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current.navigationWidth).toBe(340);
    expect(result.current.leftOpen).toBe(true);
    expect(result.current.inspectorOpen).toBe(false);
  });
});

function setViewportWidth(width: number) {
  Object.defineProperty(window, "innerWidth", { configurable: true, value: width });
}
