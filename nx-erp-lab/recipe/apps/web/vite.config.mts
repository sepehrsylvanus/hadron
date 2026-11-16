import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { nxViteTsPaths } from '@nx/vite/plugins/nx-tsconfig-paths.plugin';
export default defineConfig({
  root:import.meta.dirname,
  cacheDir:'../../node_modules/.vite/apps/web',
  plugins:[react(),nxViteTsPaths()],
  server:{ host:'0.0.0.0',port:4201,strictPort:true,allowedHosts:['localhost','.e2b.app'],
    proxy:{'/api':{target:'http://127.0.0.1:3101',changeOrigin:true}} },
  preview:{host:'0.0.0.0',port:4301,strictPort:true,allowedHosts:['localhost','.e2b.app']},
  build:{outDir:'../../dist/apps/web',emptyOutDir:true},
});
