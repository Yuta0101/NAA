import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "");
  const ddmTarget = env.DDM_PROXY_TARGET || "https://localhost:7079";

  return {
    plugins: [react()],
    server: {
      port: 5124,
      strictPort: true,
      proxy: {
        "/ddm": {
          target: ddmTarget,
          changeOrigin: true,
          rewrite: (path) => path.replace(/^\/ddm/, ""),
        },
      },
    },
    preview: {
      port: 5124,
      strictPort: true,
    },
    build: {
      // WEB_NAA serves static files from wwwroot. Building directly there keeps
      // the deployed assets in sync with the React source.
      outDir: "../wwwroot",
      emptyOutDir: true,
      // Three.js is intentionally bundled for the interactive Liquid Ether login.
      chunkSizeWarningLimit: 600,
    },
  };
});
