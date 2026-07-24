// `nuxt generate` (npm run build) prerenders every route to static files under .output/public,
// which is what Carbon serves — no Node server. ssr stays on so pages are prerendered.
export default defineNuxtConfig({
    devServer: { port: 3000 },
    devtools: { enabled: true },
})
