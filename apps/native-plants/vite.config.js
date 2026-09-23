import { defineConfig } from 'vite';
import { resolve } from 'node:path';
export default defineConfig({
  build: { outDir: '_site', rollupOptions: { input: { main: resolve(import.meta.dirname, 'index.html'), admin: resolve(import.meta.dirname, 'admin.html') } } },
  server: { port: 3038, host: '127.0.0.1', fs: { allow: ['../..'] }, proxy: { '/api': 'http://127.0.0.1:8794', '/blobs': 'http://127.0.0.1:8794' } }
});
