import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles.css";
import "./shared/theme/tokens.css";
import "./app/app-shell.css";

const root = document.getElementById("root");
if (!root) throw new Error("Desktop root element is missing.");
const desktopRoot = root;

function renderApp() {
  createRoot(desktopRoot).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

const preview = import.meta.env.DEV && new URLSearchParams(window.location.search).get("preview") === "week86";
if (preview) {
  void import("./dev-preview-bridge").then(({ installWeek86PreviewBridge }) => {
    installWeek86PreviewBridge();
    renderApp();
  });
} else {
  renderApp();
}
