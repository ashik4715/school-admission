import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// The UI talks to the API through VITE_API_BASE_URL (e.g. http://localhost:8080).
// When it is not set, requests go to the same origin and are proxied by Vite (dev) or
// nginx (Docker) to the backend, which keeps the app free of CORS issues in any deployment.
const proxyTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: Number(process.env.PORT ?? 5173),
    strictPort: false,
    proxy: {
      '/classes': { target: proxyTarget, changeOrigin: true },
      '/holds': { target: proxyTarget, changeOrigin: true },
      '/me': { target: proxyTarget, changeOrigin: true },
      '/health': { target: proxyTarget, changeOrigin: true }
    }
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false
  }
});
