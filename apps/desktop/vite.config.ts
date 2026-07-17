import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig(({ command }) => ({
  plugins: [
    react(),
    {
      name: "caicli-content-security-policy",
      transformIndexHtml(html) {
        const development = command === "serve";
        return html
          .replace(
            "__CAICLI_STYLE_SRC__",
            development ? "'self' 'unsafe-inline'" : "'self'",
          )
          .replace(
            "__CAICLI_CONNECT_SRC__",
            development
              ? "'self' ws://127.0.0.1:5173 http://127.0.0.1:5173"
              : "'self'",
          );
      },
    },
  ],
  base: "./",
  build: {
    outDir: "dist/renderer",
    emptyOutDir: false,
    sourcemap: true,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
  },
  test: {
    projects: [
      {
        test: {
          name: "main-preload",
          environment: "node",
          include: ["src/**/*.test.ts"],
        },
      },
      {
        test: {
          name: "renderer",
          environment: "jsdom",
          include: ["src/**/*.test.tsx"],
          setupFiles: ["./src/renderer/test-setup.ts"],
        },
      },
    ],
  },
}));
