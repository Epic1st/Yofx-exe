import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: true,
    proxy: {
      '/v1': {
        target: 'https://127.0.0.1:7209',
        changeOrigin: false,
        secure: false,
      },
      '/health': {
        target: 'https://127.0.0.1:7209',
        changeOrigin: false,
        secure: false,
      },
    },
  },
  preview: {
    host: '127.0.0.1',
    port: 4174,
    strictPort: true,
  },
  build: {
    sourcemap: true,
    reportCompressedSize: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/tests/setup.ts'],
    css: true,
    pool: 'threads',
    fileParallelism: false,
    maxWorkers: 1,
    environmentOptions: {
      jsdom: {
        url: 'http://127.0.0.1:4173/',
      },
    },
  },
});
