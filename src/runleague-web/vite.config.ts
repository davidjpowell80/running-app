import { defineConfig } from 'vite';
export default defineConfig({
  build: { outDir: '../RunLeague.Api/wwwroot', emptyOutDir: true },
  server: { host: 'localhost', proxy: { '/api': { target: 'https://localhost:7043', secure: false } } }
});
