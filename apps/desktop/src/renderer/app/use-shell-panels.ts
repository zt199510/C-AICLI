import { useEffect, useRef, useState } from "react";

const NARROW_VIEWPORT_MAX = 899;
const INSPECTOR_INLINE_MIN = 1200;
const NAVIGATION_DEFAULT_WIDTH = 288;
const NAVIGATION_MIN_WIDTH = 240;
const NAVIGATION_MAX_WIDTH = 360;
const INSPECTOR_DEFAULT_WIDTH = 640;
const INSPECTOR_LEGACY_DEFAULT_WIDTH = 360;
const INSPECTOR_MIN_WIDTH = 480;
const INSPECTOR_MAX_WIDTH = 720;
const memoryPreferences = new Map<string, { summaryOpen: boolean; bottomPanelOpen: boolean; toolSidebarOpen: boolean; navigationWidth: number; inspectorWidth: number }>();

function viewportWidth() {
  return typeof window === "undefined" ? INSPECTOR_INLINE_MIN : window.innerWidth;
}

type ViewportRange = "narrow" | "compact" | "wide";

function viewportRange(width: number): ViewportRange {
  if (width <= NARROW_VIEWPORT_MAX) return "narrow";
  return width < INSPECTOR_INLINE_MIN ? "compact" : "wide";
}

export function useWorkspacePanels(workspaceId?: string) {
  const initialViewportRange = viewportRange(viewportWidth());
  const initialPreferences = workspaceId ? memoryPreferences.get(workspaceId) : undefined;
  const initialBottomPanelOpen = initialPreferences?.bottomPanelOpen ?? false;
  const initialToolSidebarOpen = !initialBottomPanelOpen &&
    (initialPreferences?.toolSidebarOpen ?? initialViewportRange === "wide");
  const viewportRangeRef = useRef<ViewportRange>(initialViewportRange);
  const [narrowViewport, setNarrowViewport] = useState(initialViewportRange === "narrow");
  const [overlayInspector, setOverlayInspector] = useState(initialViewportRange !== "wide");
  const [leftOpen, setLeftOpen] = useState(initialViewportRange !== "narrow");
  const [summaryOpen, setSummaryOpen] = useState(initialPreferences?.summaryOpen ?? initialViewportRange === "wide");
  const [bottomPanelOpen, setBottomPanelOpen] = useState(initialBottomPanelOpen);
  const [toolSidebarOpen, setToolSidebarOpen] = useState(initialToolSidebarOpen);
  const [navigationWidth, setNavigationWidth] = useState(initialPreferences?.navigationWidth ?? NAVIGATION_DEFAULT_WIDTH);
  const [inspectorWidth, setInspectorWidth] = useState(
    initialPreferences ? normalizeInspectorWidth(initialPreferences.inspectorWidth) : INSPECTOR_DEFAULT_WIDTH,
  );
  const hydratedWorkspace = useRef(workspaceId ?? null);
  const skipNextPersist = useRef(true);
  const showThreadsTrigger = useRef<HTMLButtonElement>(null);
  const summaryTrigger = useRef<HTMLButtonElement>(null);
  const bottomPanelTrigger = useRef<HTMLButtonElement>(null);
  const toolSidebarTrigger = useRef<HTMLButtonElement>(null);
  const restoreThreadsFocus = useRef(false);
  const restoreSummaryFocus = useRef(false);
  const restoreBottomPanelFocus = useRef(false);
  const restoreToolSidebarFocus = useRef(false);
  const bottomPanelOpenRef = useRef(initialBottomPanelOpen);
  const openedSurfaceOrder = useRef<Array<"summary" | "bottom">>([]);

  useEffect(() => {
    bottomPanelOpenRef.current = bottomPanelOpen;
  }, [bottomPanelOpen]);

  useEffect(() => {
    const updateLayout = () => {
      const previous = viewportRangeRef.current;
      const next = viewportRange(viewportWidth());
      if (previous === next) return;
      viewportRangeRef.current = next;
      setNarrowViewport(next === "narrow");
      setOverlayInspector(next !== "wide");
      if (next === "narrow") {
        setLeftOpen(false);
        setToolSidebarOpen(false);
      } else if (previous === "narrow") {
        setLeftOpen(true);
        setToolSidebarOpen(next === "wide" && !bottomPanelOpenRef.current);
      } else if (next === "compact") setToolSidebarOpen(false);
      else setToolSidebarOpen(!bottomPanelOpenRef.current);
    };

    window.addEventListener("resize", updateLayout);
    return () => window.removeEventListener("resize", updateLayout);
  }, []);

  useEffect(() => {
    let current = true;
    const range = viewportRangeRef.current;
    if (!workspaceId) {
      bottomPanelOpenRef.current = false;
      setSummaryOpen(range === "wide"); setBottomPanelOpen(false); setToolSidebarOpen(range === "wide");
      setNavigationWidth(NAVIGATION_DEFAULT_WIDTH); setInspectorWidth(INSPECTOR_DEFAULT_WIDTH); hydratedWorkspace.current = null;
      return () => { current = false; };
    }
    if (!window.caicli?.getSettings) {
      hydratedWorkspace.current = workspaceId;
      skipNextPersist.current = false;
      return () => { current = false; };
    }
    void window.caicli.getSettings({ workspaceId }).then(({ user, workspace }) => {
      if (!current) return;
      const preferences = { ...user, ...workspace };
      bottomPanelOpenRef.current = preferences.bottomDefault;
      setSummaryOpen(preferences.summaryDefault);
      setBottomPanelOpen(preferences.bottomDefault);
      setToolSidebarOpen(range === "wide" && !preferences.bottomDefault ? preferences.toolsDefault : false);
      setNavigationWidth(clampNavigationWidth(preferences.navigationWidth));
      setInspectorWidth(normalizeInspectorWidth(preferences.inspectorWidth));
      hydratedWorkspace.current = workspaceId;
      skipNextPersist.current = true;
    }).catch(() => { hydratedWorkspace.current = workspaceId; });
    return () => { current = false; };
  }, [workspaceId]);

  useEffect(() => {
    if (!workspaceId || hydratedWorkspace.current !== workspaceId) return;
    if (skipNextPersist.current) {
      skipNextPersist.current = false;
      return;
    }
    memoryPreferences.set(workspaceId, { summaryOpen, bottomPanelOpen, toolSidebarOpen, navigationWidth, inspectorWidth });
    if (!window.caicli?.setSettings) return;
    void window.caicli.setSettings({ scope: "workspace", workspaceId, value: {
      summaryDefault: summaryOpen,
      bottomDefault: bottomPanelOpen,
      toolsDefault: toolSidebarOpen,
      navigationWidth,
      inspectorWidth,
    }}).catch(() => undefined);
  }, [bottomPanelOpen, inspectorWidth, navigationWidth, summaryOpen, toolSidebarOpen, workspaceId]);

  useEffect(() => {
    if (!leftOpen && restoreThreadsFocus.current) {
      restoreThreadsFocus.current = false;
      showThreadsTrigger.current?.focus();
    }
  }, [leftOpen]);

  useEffect(() => {
    if (!summaryOpen && restoreSummaryFocus.current) {
      restoreSummaryFocus.current = false;
      summaryTrigger.current?.focus();
    }
  }, [summaryOpen]);

  useEffect(() => {
    if (!bottomPanelOpen && restoreBottomPanelFocus.current) {
      restoreBottomPanelFocus.current = false;
      bottomPanelTrigger.current?.focus();
    }
  }, [bottomPanelOpen]);

  useEffect(() => {
    if (!toolSidebarOpen && restoreToolSidebarFocus.current) {
      restoreToolSidebarFocus.current = false;
      toolSidebarTrigger.current?.focus();
    }
  }, [toolSidebarOpen]);

  function showThreads() {
    setLeftOpen(true);
    if (viewportWidth() <= NARROW_VIEWPORT_MAX) setToolSidebarOpen(false);
  }

  function showToolSidebar() {
    restoreBottomPanelFocus.current = false;
    bottomPanelOpenRef.current = false;
    openedSurfaceOrder.current = openedSurfaceOrder.current.filter((surface) => surface !== "bottom");
    setBottomPanelOpen(false);
    setToolSidebarOpen(true);
    if (viewportRangeRef.current !== "wide") {
      setLeftOpen(false);
    }
  }

  function closeThreads(restoreFocus = true) {
    restoreThreadsFocus.current = restoreFocus;
    setLeftOpen(false);
  }

  function closeToolSidebar(restoreFocus = true) {
    restoreToolSidebarFocus.current = restoreFocus;
    setToolSidebarOpen(false);
  }

  function toggleSummary() {
    if (summaryOpen) closeSummary();
    else {
      if (viewportRangeRef.current !== "wide") {
        setToolSidebarOpen(false);
      }
      openedSurfaceOrder.current = [...openedSurfaceOrder.current.filter((surface) => surface !== "summary"), "summary"];
      setSummaryOpen(true);
    }
  }

  function closeSummary(restoreFocus = true) {
    restoreSummaryFocus.current = restoreFocus;
    openedSurfaceOrder.current = openedSurfaceOrder.current.filter((surface) => surface !== "summary");
    setSummaryOpen(false);
  }

  function showBottomPanel() {
    if (!bottomPanelOpen) {
      restoreToolSidebarFocus.current = false;
      setToolSidebarOpen(false);
      bottomPanelOpenRef.current = true;
      openedSurfaceOrder.current = [...openedSurfaceOrder.current.filter((surface) => surface !== "bottom"), "bottom"];
      setBottomPanelOpen(true);
    }
  }

  function toggleBottomPanel() {
    if (bottomPanelOpen) closeBottomPanel();
    else showBottomPanel();
  }

  function closeBottomPanel(restoreFocus = true) {
    restoreBottomPanelFocus.current = restoreFocus;
    bottomPanelOpenRef.current = false;
    openedSurfaceOrder.current = openedSurfaceOrder.current.filter((surface) => surface !== "bottom");
    setBottomPanelOpen(false);
  }

  function toggleToolSidebar() {
    if (toolSidebarOpen) closeToolSidebar();
    else showToolSidebar();
  }

  function closeLastSurface() {
    const surface = openedSurfaceOrder.current.at(-1);
    if (surface === "bottom" && bottomPanelOpen) closeBottomPanel();
    else if (surface === "summary" && summaryOpen) closeSummary();
    else if (bottomPanelOpen) closeBottomPanel();
    else if (summaryOpen) closeSummary();
  }

  function resizeNavigationAt(clientX: number) {
    setNavigationWidth(clampNavigationWidth(clientX));
  }

  function nudgeNavigationWidth(delta: number) {
    setNavigationWidth((width) => clampNavigationWidth(width + delta));
  }

  function resetNavigationWidth() {
    setNavigationWidth(NAVIGATION_DEFAULT_WIDTH);
  }

  function resizeInspectorAt(clientX: number) {
    setInspectorWidth(clampInspectorWidth(viewportWidth() - clientX));
  }

  function nudgeInspectorWidth(delta: number) {
    setInspectorWidth((width) => clampInspectorWidth(width + delta));
  }

  return {
    leftOpen,
    summaryOpen,
    bottomPanelOpen,
    toolSidebarOpen,
    narrowViewport,
    overlayInspector,
    navigationWidth,
    inspectorWidth,
    showThreadsTrigger,
    summaryTrigger,
    bottomPanelTrigger,
    toolSidebarTrigger,
    showThreads,
    showToolSidebar,
    closeThreads,
    closeToolSidebar,
    toggleSummary,
    closeSummary,
    showBottomPanel,
    toggleBottomPanel,
    closeBottomPanel,
    toggleToolSidebar,
    closeLastSurface,
    resizeNavigationAt,
    nudgeNavigationWidth,
    resetNavigationWidth,
    resizeInspectorAt,
    nudgeInspectorWidth,
  };
}

export const useShellPanels = useWorkspacePanels;

function clampNavigationWidth(width: number) {
  return Math.min(NAVIGATION_MAX_WIDTH, Math.max(NAVIGATION_MIN_WIDTH, Math.round(width)));
}

function clampInspectorWidth(width: number) {
  return Math.min(INSPECTOR_MAX_WIDTH, Math.max(INSPECTOR_MIN_WIDTH, Math.round(width)));
}

function normalizeInspectorWidth(width: number) {
  return width === INSPECTOR_LEGACY_DEFAULT_WIDTH ? INSPECTOR_DEFAULT_WIDTH : clampInspectorWidth(width);
}
