import { FitAddon } from "@xterm/addon-fit";
import { Terminal } from "@xterm/xterm";
import "@xterm/xterm/css/xterm.css";

export interface TerminalDimensions {
  readonly cols: number;
  readonly rows: number;
}

export interface XtermAdapterOptions {
  readonly onData: (data: string) => void;
  readonly onResize: (dimensions: TerminalDimensions) => void;
}

export class XtermAdapter {
  private readonly terminal: Terminal;
  private readonly fitAddon = new FitAddon();
  private readonly disposables: Array<{ dispose(): void }> = [];
  private lastOutput = "";
  private disposed = false;

  public constructor(private readonly options: XtermAdapterOptions) {
    const dark = document.documentElement.dataset.theme === "dark" ||
      (document.documentElement.dataset.theme === "system" && window.matchMedia?.("(prefers-color-scheme: dark)").matches);
    this.terminal = new Terminal({
      allowProposedApi: false,
      allowTransparency: false,
      convertEol: false,
      cursorBlink: true,
      cursorStyle: "block",
      disableStdin: false,
      drawBoldTextInBrightColors: true,
      fontFamily: '"Cascadia Mono", "Cascadia Code", Consolas, monospace',
      fontSize: 12,
      lineHeight: 1.18,
      rightClickSelectsWord: true,
      screenReaderMode: true,
      scrollback: 5_000,
      theme: dark ? {
        background: "#111713",
        foreground: "#e7efe9",
        cursor: "#72d3aa",
        cursorAccent: "#111713",
        selectionBackground: "#315b49",
      } : {
        background: "#ffffff",
        foreground: "#242824",
        cursor: "#155f49",
        cursorAccent: "#ffffff",
        selectionBackground: "#cfe7dc",
      },
    });
    this.terminal.loadAddon(this.fitAddon);
    // Consume clipboard-write requests from terminal programs. Clipboard access
    // remains available only through an explicit user gesture in our toolbar.
    this.terminal.parser.registerOscHandler(52, () => true);
    this.disposables.push(this.terminal.onData((data) => this.options.onData(data)));
    this.disposables.push(this.terminal.onResize(({ cols, rows }) => this.options.onResize({ cols, rows })));
  }

  public open(element: HTMLElement) {
    if (this.disposed) return;
    this.terminal.open(element);
    this.terminal.textarea?.setAttribute("aria-label", "Terminal input");
    this.fit();
  }

  public replaceOutput(output: string) {
    if (this.disposed || output === this.lastOutput) return;
    this.lastOutput = output;
    this.terminal.reset();
    if (output) this.terminal.write(output);
  }

  public fit(): TerminalDimensions | null {
    if (this.disposed || !this.terminal.element) return null;
    try {
      this.fitAddon.fit();
      return { cols: this.terminal.cols, rows: this.terminal.rows };
    } catch {
      return null;
    }
  }

  public focus() {
    if (!this.disposed) this.terminal.focus();
  }

  public selectedText() {
    return this.disposed ? "" : this.terminal.getSelection();
  }

  public paste(text: string) {
    if (!this.disposed && text) this.options.onData(text);
  }

  public dispose() {
    if (this.disposed) return;
    this.disposed = true;
    for (const disposable of this.disposables) disposable.dispose();
    this.fitAddon.dispose();
    this.terminal.dispose();
  }
}
