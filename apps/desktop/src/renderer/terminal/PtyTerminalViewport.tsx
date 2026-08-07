import { useEffect, useRef, type MutableRefObject } from "react";
import { TerminalResizeObserver } from "./TerminalResizeObserver";
import { XtermAdapter, type TerminalDimensions } from "./XtermAdapter";

export interface PtyTerminalViewportProps {
  readonly output: string;
  readonly active: boolean;
  readonly readOnly: boolean;
  readonly onData: (data: string) => void;
  readonly onResize: (dimensions: TerminalDimensions) => void;
  readonly adapterRef?: MutableRefObject<XtermAdapter | null>;
}

export function PtyTerminalViewport({ output, active, readOnly, onData, onResize, adapterRef }: PtyTerminalViewportProps) {
  const root = useRef<HTMLDivElement>(null);
  const localAdapter = useRef<XtermAdapter | null>(null);
  const latestData = useRef(onData);
  const latestResize = useRef(onResize);
  latestData.current = onData;
  latestResize.current = onResize;

  useEffect(() => {
    if (!root.current || typeof ResizeObserver === "undefined" || navigator.userAgent.includes("jsdom")) return;
    const adapter = new XtermAdapter({
      onData: (data) => { if (!readOnly) latestData.current(data); },
      onResize: (dimensions) => latestResize.current(dimensions),
    });
    localAdapter.current = adapter;
    if (adapterRef) adapterRef.current = adapter;
    adapter.open(root.current);
    const resize = new TerminalResizeObserver(root.current, adapter, (dimensions) => latestResize.current(dimensions));
    return () => {
      resize.dispose();
      adapter.dispose();
      localAdapter.current = null;
      if (adapterRef) adapterRef.current = null;
    };
  }, [adapterRef, readOnly]);

  useEffect(() => localAdapter.current?.replaceOutput(output), [output]);
  useEffect(() => { if (active) localAdapter.current?.focus(); }, [active]);

  return <div className="pty-terminal-viewport" data-active={active} data-readonly={readOnly} ref={root} />;
}
