import { useEffect, useRef, useState } from "react";

const NARROW_VIEWPORT_MAX = 899;
const INSPECTOR_INLINE_MIN = 1121;
const NAVIGATION_DEFAULT_WIDTH = 288;
const NAVIGATION_MIN_WIDTH = 240;
const NAVIGATION_MAX_WIDTH = 360;
const INSPECTOR_MIN_WIDTH = 320;
const INSPECTOR_MAX_WIDTH = 480;

function viewportWidth() {
  return typeof window === "undefined" ? INSPECTOR_INLINE_MIN : window.innerWidth;
}

type ViewportRange = "narrow" | "compact" | "wide";

function viewportRange(width: number): ViewportRange {
  if (width <= NARROW_VIEWPORT_MAX) return "narrow";
  return width < INSPECTOR_INLINE_MIN ? "compact" : "wide";
}

export function useShellPanels() {
  const initialViewportRange = viewportRange(viewportWidth());
  const viewportRangeRef = useRef<ViewportRange>(initialViewportRange);
  const [narrowViewport, setNarrowViewport] = useState(initialViewportRange === "narrow");
  const [overlayInspector, setOverlayInspector] = useState(initialViewportRange !== "wide");
  const [leftOpen, setLeftOpen] = useState(initialViewportRange !== "narrow");
  const [summaryOpen, setSummaryOpen] = useState(false);
  const [bottomPanelOpen, setBottomPanelOpen] = useState(false);
  const [toolSidebarOpen, setToolSidebarOpen] = useState(initialViewportRange === "wide");
  const [navigationWidth, setNavigationWidth] = useState(NAVIGATION_DEFAULT_WIDTH);
  const [inspectorWidth, setInspectorWidth] = useState(360);
  const showThreadsTrigger = useRef<HTMLButtonElement>(null);
  const summaryTrigger = useRef<HTMLButtonElement>(null);
  const bottomPanelTrigger = useRef<HTMLButtonElement>(null);
  const toolSidebarTrigger = useRef<HTMLButtonElement>(null);
  const restoreThreadsFocus = useRef(false);
  const restoreSummaryFocus = useRef(false);
  const restoreBottomPanelFocus = useRef(false);
  const restoreToolSidebarFocus = useRef(false);
  const openedSurfaceOrder = useRef<Array<"summary" | "bottom">>([]);

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
        setToolSidebarOpen(next === "wide");
      } else if (next === "compact") setToolSidebarOpen(false);
      else setToolSidebarOpen(true);
    };

    window.addEventListener("resize", updateLayout);
    return () => window.removeEventListener("resize", updateLayout);
  }, []);

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
    setToolSidebarOpen(true);
    if (viewportWidth() <= NARROW_VIEWPORT_MAX) setLeftOpen(false);
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

function clampNavigationWidth(width: number) {
  return Math.min(NAVIGATION_MAX_WIDTH, Math.max(NAVIGATION_MIN_WIDTH, Math.round(width)));
}

function clampInspectorWidth(width: number) {
  return Math.min(INSPECTOR_MAX_WIDTH, Math.max(INSPECTOR_MIN_WIDTH, Math.round(width)));
}
