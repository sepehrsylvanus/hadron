import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '../../', '')

  return {
    envDir: '../../',
    plugins: [react()],
    server: {
      host: 'localhost',
      port: 5173,
      strictPort: true,
      proxy: env.VITE_API_BASE_URL
        ? undefined
        : {
            '/api': {
              target: env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080',
              changeOrigin: true,
            },
          },
    },
  }
})
