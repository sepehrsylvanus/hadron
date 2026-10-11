import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '../../', '')
  const apiProxyTarget = env.VITE_API_PROXY_TARGET
    ?? env.VITE_API_BASE_URL?.replace(/\/api\/?$/, '')
    ?? 'http://127.0.0.1:5080'

  return {
    envDir: '../../',
    plugins: [react()],
    server: {
      host: '127.0.0.1',
      port: 5310,
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
