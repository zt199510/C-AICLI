import type { TerminalDimensions, XtermAdapter } from "./XtermAdapter";

export class TerminalResizeObserver {
  private readonly observer: ResizeObserver;
  private frame: number | null = null;
  private last: TerminalDimensions | null = null;

  public constructor(
    element: HTMLElement,
    private readonly adapter: XtermAdapter,
    private readonly onResize: (dimensions: TerminalDimensions) => void,
  ) {
    this.observer = new ResizeObserver(() => this.schedule());
    this.observer.observe(element);
    this.schedule();
  }

  private schedule() {
    if (this.frame !== null) cancelAnimationFrame(this.frame);
    this.frame = requestAnimationFrame(() => {
      this.frame = null;
      const dimensions = this.adapter.fit();
      if (!dimensions || (this.last?.cols === dimensions.cols && this.last.rows === dimensions.rows)) return;
      this.last = dimensions;
      this.onResize(dimensions);
    });
  }

  public dispose() {
    this.observer.disconnect();
    if (this.frame !== null) cancelAnimationFrame(this.frame);
    this.frame = null;
  }
}
