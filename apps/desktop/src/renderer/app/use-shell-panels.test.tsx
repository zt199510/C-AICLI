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
  it("opens an inline inspector on wide viewports and keeps overlays closed initially", () => {
    setViewportWidth(1440);
    const wide = renderHook(() => useShellPanels());
    expect(wide.result.current.toolSidebarOpen).toBe(true);
    expect(wide.result.current.summaryOpen).toBe(false);
    expect(wide.result.current.bottomPanelOpen).toBe(false);
    expect(wide.result.current.overlayInspector).toBe(false);
    wide.unmount();

    setViewportWidth(1024);
    const compact = renderHook(() => useShellPanels());
    expect(compact.result.current.toolSidebarOpen).toBe(false);
    expect(compact.result.current.overlayInspector).toBe(true);
    compact.unmount();
  });

  it("keeps the navigation and inspector mutually exclusive on narrow viewports", () => {
    setViewportWidth(800);
    const { result } = renderHook(() => useShellPanels());

    expect(result.current.leftOpen).toBe(false);
    expect(result.current.toolSidebarOpen).toBe(false);

    act(() => result.current.showThreads());
    expect(result.current.leftOpen).toBe(true);
    expect(result.current.toolSidebarOpen).toBe(false);

    act(() => result.current.showToolSidebar());
    expect(result.current.leftOpen).toBe(false);
    expect(result.current.toolSidebarOpen).toBe(true);
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
    expect(result.current.toolSidebarOpen).toBe(false);

    act(() => {
      setViewportWidth(1000);
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current.navigationWidth).toBe(340);
    expect(result.current.leftOpen).toBe(true);
    expect(result.current.toolSidebarOpen).toBe(false);
  });

  it("keeps the three workspace surfaces independently switchable", () => {
    setViewportWidth(1440);
    const { result } = renderHook(() => useShellPanels());

    act(() => result.current.toggleSummary());
    expect(result.current.summaryOpen).toBe(true);
    expect(result.current.bottomPanelOpen).toBe(false);
    expect(result.current.toolSidebarOpen).toBe(true);

    act(() => result.current.showBottomPanel());
    expect(result.current.summaryOpen).toBe(true);
    expect(result.current.bottomPanelOpen).toBe(true);
    expect(result.current.toolSidebarOpen).toBe(true);

    act(() => result.current.toggleToolSidebar());
    expect(result.current.summaryOpen).toBe(true);
    expect(result.current.bottomPanelOpen).toBe(true);
    expect(result.current.toolSidebarOpen).toBe(false);
  });

  it("closes the last opened non-modal surface first", () => {
    const { result } = renderHook(() => useShellPanels());
    act(() => {
      result.current.toggleSummary();
      result.current.showBottomPanel();
    });

    act(() => result.current.closeLastSurface());
    expect(result.current.bottomPanelOpen).toBe(false);
    expect(result.current.summaryOpen).toBe(true);

    act(() => result.current.closeLastSurface());
    expect(result.current.summaryOpen).toBe(false);
  });

  it("preserves the tool sidebar width across responsive mode changes", () => {
    setViewportWidth(1440);
    const { result } = renderHook(() => useShellPanels());
    act(() => result.current.nudgeInspectorWidth(72));
    expect(result.current.inspectorWidth).toBe(432);

    act(() => {
      setViewportWidth(1024);
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current.inspectorWidth).toBe(432);

    act(() => {
      setViewportWidth(1440);
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current.inspectorWidth).toBe(432);
  });
});

function setViewportWidth(width: number) {
  Object.defineProperty(window, "innerWidth", { configurable: true, value: width });
}
