import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  assetsInclude: ['**/*.jpe'],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5165',
        changeOrigin: true,
      },
    },
  },
})
