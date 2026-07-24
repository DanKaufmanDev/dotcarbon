import { readdir, readFile, writeFile, mkdir, rm, cp, stat, unlink } from 'fs/promises'
import { existsSync } from 'fs'
import { join, extname, dirname } from 'path'
import { fileURLToPath } from 'url'
import { transformSync } from 'esbuild'

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)))
const SRC = join(ROOT, 'templates')
const OUT = join(ROOT, 'templates-js')

const DROP_DEV_DEPS = new Set(['typescript', 'vue-tsc', 'svelte-check', '@tsconfig/svelte', 'tslib'])
// Decorator-based and TS-first templates have no meaningful JavaScript variant.
const TS_ONLY = new Set(['lit', 'angular', 'sveltekit', 'nuxt'])

async function walk(dir) {
    const out = []
    for (const e of await readdir(dir)) {
        const p = join(dir, e)
        if ((await stat(p)).isDirectory()) out.push(...await walk(p))
        else out.push(p)
    }
    return out
}

function stripTypes(code, loader) {
    return transformSync(code, { loader, jsx: 'preserve', format: 'esm' }).code
}

async function processFile(src, dst) {
    const ext = extname(src)
    await mkdir(dirname(dst), { recursive: true })

    if (ext === '.ts') {
        await writeFile(dst.replace(/\.ts$/, '.js'), stripTypes(await readFile(src, 'utf-8'), 'ts'))
    } else if (ext === '.tsx') {
        await writeFile(dst.replace(/\.tsx$/, '.jsx'), stripTypes(await readFile(src, 'utf-8'), 'tsx'))
    } else if (ext === '.vue') {
        await writeFile(dst, (await readFile(src, 'utf-8')).replace(/<script setup lang="ts">/, '<script setup>'))
    } else if (ext === '.svelte') {
        await writeFile(dst, (await readFile(src, 'utf-8')).replace(/<script lang="ts">/, '<script>'))
    } else if (src.endsWith('package.json')) {
        const pkg = JSON.parse(await readFile(src, 'utf-8'))
        for (const k of Object.keys(pkg.devDependencies ?? {}))
            if (DROP_DEV_DEPS.has(k) || k.startsWith('@types/')) delete pkg.devDependencies[k]
        if (pkg.scripts?.build)
            pkg.scripts.build = pkg.scripts.build.split('&&').map(s => s.trim())
                .filter(s => !/\b(tsc|vue-tsc|svelte-check)\b/.test(s)).join(' && ')
        delete pkg.scripts?.check
        await writeFile(dst, JSON.stringify(pkg, null, 4) + '\n')
    } else if (src.endsWith('index.html')) {
        await writeFile(dst, (await readFile(src, 'utf-8')).replace(/\/src\/(main|index)\.tsx?"/, (_, n) =>
            `/src/${n}.${_.includes('.tsx') ? 'jsx' : 'js'}"`))
    } else {
        await cp(src, dst)
    }
}

const IGNORE = (p) => p.endsWith('tsconfig.json') || p.endsWith('.d.ts')

await rm(OUT, { recursive: true, force: true })
for (const template of await readdir(SRC)) {
    if (TS_ONLY.has(template)) continue
    const base = join(SRC, template)
    for (const file of await walk(base)) {
        if (IGNORE(file)) continue
        await processFile(file, join(OUT, template, file.slice(base.length + 1)))
    }
}
console.log('generated templates-js for:', (await readdir(OUT)).join(', '))
