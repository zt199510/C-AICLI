import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles.css";
import "./shared/theme/tokens.css";
import "./app/app-shell.css";
import "./composer.css";
import "./thread-sidebar.css";

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

const previewFixture = new URLSearchParams(window.location.search).get("preview");
const preview = import.meta.env.DEV && (previewFixture === "week86" || previewFixture === "week89");
if (preview) {
  void import("./dev-preview-bridge").then(({ installDesktopPreviewBridge }) => {
    installDesktopPreviewBridge();
    renderApp();
  });
} else {
  renderApp();
}
