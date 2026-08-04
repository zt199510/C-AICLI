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
  const [leftOpen, setLeftOpen] = useState(initialViewportRange !== "narrow");
  const [inspectorOpen, setInspectorOpen] = useState(false);
  const [navigationWidth, setNavigationWidth] = useState(NAVIGATION_DEFAULT_WIDTH);
  const [inspectorWidth, setInspectorWidth] = useState(360);
  const showThreadsTrigger = useRef<HTMLButtonElement>(null);
  const showInspectorTrigger = useRef<HTMLButtonElement>(null);
  const restoreThreadsFocus = useRef(false);
  const restoreInspectorFocus = useRef(false);

  useEffect(() => {
    const updateLayout = () => {
      const previous = viewportRangeRef.current;
      const next = viewportRange(viewportWidth());
      if (previous === next) return;
      viewportRangeRef.current = next;
      setNarrowViewport(next === "narrow");
      if (next === "narrow") {
        setLeftOpen(false);
        setInspectorOpen(false);
      } else if (previous === "narrow") {
        setLeftOpen(true);
        setInspectorOpen(next === "wide");
      } else if (next === "compact") setInspectorOpen(false);
      else setInspectorOpen(true);
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
    if (!inspectorOpen && restoreInspectorFocus.current) {
      restoreInspectorFocus.current = false;
      showInspectorTrigger.current?.focus();
    }
  }, [inspectorOpen]);

  function showThreads() {
    setLeftOpen(true);
    if (viewportWidth() <= NARROW_VIEWPORT_MAX) setInspectorOpen(false);
  }

  function showInspector() {
    setInspectorOpen(true);
    if (viewportWidth() <= NARROW_VIEWPORT_MAX) setLeftOpen(false);
  }

  function closeThreads(restoreFocus = true) {
    restoreThreadsFocus.current = restoreFocus;
    setLeftOpen(false);
  }

  function closeInspector(restoreFocus = true) {
    restoreInspectorFocus.current = restoreFocus;
    setInspectorOpen(false);
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
    inspectorOpen,
    narrowViewport,
    navigationWidth,
    inspectorWidth,
    showThreadsTrigger,
    showInspectorTrigger,
    showThreads,
    showInspector,
    closeThreads,
    closeInspector,
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
