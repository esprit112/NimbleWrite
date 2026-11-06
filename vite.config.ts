import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5317,
    strictPort: true,
    proxy: { '/api': { target: 'http://127.0.0.1:4317', changeOrigin: false } },
  },
  build: {
    outDir: 'dist',
    chunkSizeWarningLimit: 2000,
    rolldownOptions: {
      output: {
        // Libraries change far less often than the app, so they get chunks of their own that stay cached across updates.
        codeSplitting: {
          groups: [
            { name: 'react', test: /node_modules[\/](react|react-dom|scheduler)[\/]/ },
            { name: 'editor', test: /node_modules[\/](@tiptap|prosemirror-[a-z-]+|orderedmap|rope-sequence|w3c-keyname|linkifyjs)[\/]/ },
          ],
        },
      },
    },
  },
});
