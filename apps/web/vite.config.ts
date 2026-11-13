import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    host: 'localhost',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: false },
      '/signin-google': { target: 'http://localhost:5080', changeOrigin: false },
    },
  },
  test: {
    environment: 'jsdom', setupFiles: ['./src/test-setup.ts'], clearMocks: true,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test-setup.ts', 'src/**/*.d.ts'],
      reporter: ['text-summary', 'html', 'json-summary'],
      reportsDirectory: '../../artifacts/coverage/web',
      reportOnFailure: true,
    },
  },
});
