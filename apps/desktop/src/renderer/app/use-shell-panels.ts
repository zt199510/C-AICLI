import { useEffect, useRef, useState } from "react";

const NARROW_VIEWPORT_MAX = 899;
const INSPECTOR_INLINE_MIN = 1121;
const INSPECTOR_MIN_WIDTH = 320;
const INSPECTOR_MAX_WIDTH = 480;

function viewportWidth() {
  return typeof window === "undefined" ? INSPECTOR_INLINE_MIN : window.innerWidth;
}

export function useShellPanels() {
  const [leftOpen, setLeftOpen] = useState(() => viewportWidth() > NARROW_VIEWPORT_MAX);
  const [inspectorOpen, setInspectorOpen] = useState(false);
  const [inspectorWidth, setInspectorWidth] = useState(360);
  const showThreadsTrigger = useRef<HTMLButtonElement>(null);
  const showInspectorTrigger = useRef<HTMLButtonElement>(null);
  const restoreThreadsFocus = useRef(false);
  const restoreInspectorFocus = useRef(false);

  useEffect(() => {
    const updateLayout = () => {
      const width = viewportWidth();
      if (width <= NARROW_VIEWPORT_MAX) {
        setLeftOpen(false);
        setInspectorOpen(false);
      } else if (width < INSPECTOR_INLINE_MIN) {
        setLeftOpen(true);
        setInspectorOpen(false);
      } else {
        setLeftOpen(true);
        setInspectorOpen(true);
      }
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

  function resizeInspectorAt(clientX: number) {
    setInspectorWidth(clampInspectorWidth(viewportWidth() - clientX));
  }

  function nudgeInspectorWidth(delta: number) {
    setInspectorWidth((width) => clampInspectorWidth(width + delta));
  }

  return {
    leftOpen,
    inspectorOpen,
    inspectorWidth,
    showThreadsTrigger,
    showInspectorTrigger,
    showThreads,
    showInspector,
    closeThreads,
    closeInspector,
    resizeInspectorAt,
    nudgeInspectorWidth,
  };
}

function clampInspectorWidth(width: number) {
  return Math.min(INSPECTOR_MAX_WIDTH, Math.max(INSPECTOR_MIN_WIDTH, Math.round(width)));
}
