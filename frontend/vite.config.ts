/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev server proxies /api to the ASP.NET Core API so the browser sees a single origin: the session cookie
// stays SameSite=Strict and no CORS is needed in development.
const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://127.0.0.1:5090'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    proxy: { '/api': { target: apiTarget, changeOrigin: false } },
  },
  // `vite preview` serves the production build with the same proxy (used by the end-to-end tests).
  preview: {
    port: 4173,
    proxy: { '/api': { target: apiTarget, changeOrigin: false } },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
})
