import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

export default defineConfig({
  plugins: [svelte()],
  // Tauri uses a custom protocol in production; relative base is required
  base: './',
  build: {
    outDir: '../dist',
    emptyOutDir: true,
    // v0.7.10: es2022 is the minimum that enables top-level await (used by
    // main.ts to await applyPersistedLocale before mounting App, eliminating
    // the first-paint English flash). WebView2 (Chromium-based, preinstalled
    // on Windows 11, available on Windows 10 21H2+) supports es2022 fully.
    target: 'es2022',
    assetsDir: 'assets',
  },
  server: {
    port: 5173,
    strictPort: false,
  },
  test: {
    // Unit tests only. E2e tests use Playwright separately.
    include: ['src/**/*.test.ts'],
    exclude: ['tests/**', 'node_modules/**', 'dist/**'],
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./tests/setup.ts'],
  },
})
