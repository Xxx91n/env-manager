import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

export default defineConfig({
  plugins: [svelte()],
  // Tauri uses a custom protocol in production; relative base is required
  base: './',
  build: {
    outDir: '../dist',
    emptyOutDir: true,
    target: 'es2021',
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
