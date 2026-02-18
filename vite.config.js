import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import fable from 'vite-plugin-fable';

export default defineConfig({
  plugins: [
    fable({
      fsproj: './src/Client/Client.fsproj',
      jsx: 'automatic'
    }),
    react()
  ],
  root: './public',
  build: {
    outDir: '../dist/client',
    emptyOutDir: true
  },
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:8787',
        changeOrigin: true
      }
    }
  }
});
