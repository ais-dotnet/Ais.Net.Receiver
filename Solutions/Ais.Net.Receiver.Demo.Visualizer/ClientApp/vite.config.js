import {defineConfig} from 'vite';
import path from 'path';

// The bundle is served by ASP.NET Core from wwwroot, not by a Node dev server, so Vite's only job
// here is to build. `base` matches where UseStaticFiles serves the emitted assets from.
export default defineConfig({
  build: {
    // Straight into wwwroot so UseDefaultFiles finds index.html at the site root. emptyOutDir is off
    // because wwwroot is the web root rather than a directory Vite owns outright.
    outDir: path.resolve(__dirname, '../wwwroot'),
    emptyOutDir: false
  },
  server: {
    port: 3000,
    host: true,
    open: false,

    // Only used when running Vite directly for frontend iteration; /api calls go to the ASP.NET Core
    // host so the page behaves as it does when served from wwwroot.
    proxy: {
      '/api': 'http://localhost:5100'
    }
  },
  resolve: {
    alias: {
      '@': '/src'
    }
  }
});
