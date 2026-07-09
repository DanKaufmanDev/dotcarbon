import { defineConfig } from 'vite'
import preact from '@preact/preset-vite'

export default defineConfig({
    plugins: [preact()],
    clearScreen: false,
    server: { port: 5173, strictPort: true },
})
