import adapter from '@sveltejs/adapter-static'
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte'

// adapter-static prerenders the whole app to plain files — no Node server — which is what Carbon
// serves. `fallback` makes it an SPA so client-side routes still resolve.
export default {
    preprocess: vitePreprocess(),
    kit: {
        adapter: adapter({ fallback: 'index.html' }),
    },
}
