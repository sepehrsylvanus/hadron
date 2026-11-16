import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '../../', '')
  const configuredApiOrigin = env.VITE_API_BASE_URL?.replace(/\/api\/?$/, '')
  const apiProxyTarget = env.VITE_API_PROXY_TARGET?.trim()
    || (configuredApiOrigin && /^https?:\/\//i.test(configuredApiOrigin) ? configuredApiOrigin : '')
    || 'http://127.0.0.1:5080'

  return {
    envDir: '../../',
    plugins: [react()],
    server: {
      host: '127.0.0.1',
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': {
          target: apiProxyTarget,
          changeOrigin: true,
        },
        '/hubs': {
          target: apiProxyTarget,
          changeOrigin: true,
          ws: true,
        },
      },
    },
  }
})
