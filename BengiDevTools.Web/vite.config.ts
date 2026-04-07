import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5050',
        changeOrigin: true,
        // Required for SSE (Server-Sent Events) streaming to work through the proxy
        headers: { 'X-Forwarded-For': '' },
        configure: (proxy) => {
          proxy.on('proxyRes', (proxyRes) => {
            // Disable buffering so SSE events arrive immediately
            proxyRes.headers['x-accel-buffering'] = 'no'
          })
        },
      },
    },
  },
  build: {
    outDir: '../BengiDevTools.Api/wwwroot',
    emptyOutDir: true,
  },
})
