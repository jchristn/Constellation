import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  define: {
    __DEFAULT_API_KEY__: JSON.stringify('constellationadmin')
  },
  server: {
    host: '0.0.0.0',
    port: 8080,
    allowedHosts: true
  }
})
