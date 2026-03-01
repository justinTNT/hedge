import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    outDir: '_site',
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        admin: resolve(__dirname, 'admin.html')
      }
    }
  },
  publicDir: false,
  server: {
    port: 3030,
    host: true,
    allowedHosts: true,
    watch: {
      ignored: ['!**/dist/**']
    },
    proxy: {
      '/api': {
        target: 'http://localhost:8787',
        changeOrigin: true,
        ws: true
      },
      '/blobs': {
        target: 'http://localhost:8787',
        changeOrigin: true
      }
    }
  }
});
