#!/usr/bin/env node
import * as p from '@clack/prompts'
import { writeFile, readFile, cp, unlink, rename, readdir, stat } from 'fs/promises'
import { existsSync } from 'fs'
import { join, dirname, sep } from 'path'
import { fileURLToPath } from 'url'
import { execSync, spawn } from 'child_process'

const __dirname = dirname(fileURLToPath(import.meta.url))

const TEMPLATES = ['react', 'vue', 'svelte', 'solid', 'preact', 'vanilla'] as const
type Template = typeof TEMPLATES[number]
type PackageManager = 'pnpm' | 'npm' | 'yarn' | 'bun'
type Lang = 'ts' | 'js'

const TEMPLATE_LABELS: Record<Template, string> = {
    react: 'React', vue: 'Vue', svelte: 'Svelte', solid: 'Solid', preact: 'Preact', vanilla: 'Vanilla',
}
const PMS: readonly PackageManager[] = ['pnpm', 'npm', 'yarn', 'bun']
const EXCLUDE_DIRS = new Set(['node_modules', 'dist', 'bin', 'obj', 'out'])

async function main() {
    const args = process.argv.slice(2)
    const valueFlags = new Set(['--template', '--pm', '--lang'])
    const getFlag = (name: string) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined }

    const noInstall = args.includes('--no-install')
    const autoYes = args.includes('--yes') || args.includes('-y')
    const interactive = Boolean(process.stdin.isTTY) && !autoYes

    let name = args.find((a, i) => !a.startsWith('-') && !valueFlags.has(args[i - 1]))
    let template = getFlag('--template') as Template | undefined
    let lang = getFlag('--lang') as Lang | undefined
    let pm = getFlag('--pm') as PackageManager | undefined
    if (args.includes('--ts') || args.includes('--typescript')) lang = 'ts'
    if (args.includes('--js') || args.includes('--javascript')) lang = 'js'

    validate('template', template, TEMPLATES)
    validate('lang', lang, ['ts', 'js'])
    validate('pm', pm, PMS)

    p.intro('⚡ create-dotcarbon-app')

    if (!name) {
        name = interactive
            ? asString(await p.text({
                message: 'Project name',
                placeholder: 'my-carbon-app',
                defaultValue: 'my-carbon-app',
                validate: (v) => (v && existsSync(join(process.cwd(), v))) ? `"${v}" already exists` : undefined,
            }))
            : 'my-carbon-app'
    }

    if (!template) {
        template = interactive
            ? asString(await p.select({
                message: 'Frontend framework',
                initialValue: 'react',
                options: TEMPLATES.map(t => ({ value: t, label: TEMPLATE_LABELS[t] })),
            })) as Template
            : 'react'
    }

    if (!lang) {
        lang = interactive
            ? asString(await p.select({
                message: 'Language',
                initialValue: 'ts',
                options: [{ value: 'ts', label: 'TypeScript' }, { value: 'js', label: 'JavaScript' }],
            })) as Lang
            : 'ts'
    }

    if (!pm) {
        const detected = detectPackageManager()
        pm = interactive
            ? asString(await p.select({
                message: 'Package manager',
                initialValue: detected,
                options: PMS.map(m => ({ value: m, label: m })),
            })) as PackageManager
            : detected
    }

    const targetDir = join(process.cwd(), name)
    if (existsSync(targetDir)) fail(`Directory "${name}" already exists`)

    const templateDir = join(__dirname, lang === 'js' ? 'templates-js' : 'templates', template)
    if (!existsSync(templateDir)) fail(`Template not found: ${template} (${lang})`)

    const s = p.spinner()
    s.start(`Scaffolding ${TEMPLATE_LABELS[template]} + ${lang === 'ts' ? 'TypeScript' : 'JavaScript'}`)

    await cp(templateDir, targetDir, {
        recursive: true,
        filter: (src) => !src.slice(templateDir.length).split(sep).some(seg => EXCLUDE_DIRS.has(seg)),
    })

    const tmplGitignore = join(targetDir, 'gitignore')
    if (existsSync(tmplGitignore)) await rename(tmplGitignore, join(targetDir, '.gitignore'))

    const schemaSrc = join(__dirname, 'shared', 'carbon.schema.json')
    if (existsSync(schemaSrc)) await cp(schemaSrc, join(targetDir, 'carbon.schema.json'))

    await replaceInDir(targetDir, '{{APP_NAME}}', name)
    await replaceInDir(targetDir, '{{PM}}', pm)

    const oldCsproj = join(targetDir, 'src-carbon', 'APP_NAME.csproj')
    if (existsSync(oldCsproj)) {
        await writeFile(
            join(targetDir, 'src-carbon', `${name}.csproj`),
            (await readFile(oldCsproj, 'utf-8')).replaceAll('{{APP_NAME}}', name),
        )
        await unlink(oldCsproj)
    }
    s.stop('Project scaffolded')

    if (noInstall) {
        p.log.info('Skipped dependency install (--no-install)')
    } else {
        s.start(`Installing frontend dependencies with ${pm}`)
        const uiOk = await runInstall(pm, join(targetDir, 'ui'))
        s.stop(uiOk ? 'Frontend dependencies installed' : `Frontend install incomplete — run "${pm} install" in ${name}/ui`)

        s.start('Restoring .NET packages')
        const netOk = await runDotnetRestore(join(targetDir, 'src-carbon'))
        s.stop(netOk ? '.NET packages restored' : '.NET restore skipped (run "dotnet restore" in ' + name + '/src-carbon)')
    }

    await ensureCarbonCli(autoYes, interactive)

    p.note(`cd ${name}\ncarbon dev`, 'Next steps')
    p.outro('Happy building ⚡')
}

function validate<T extends string>(label: string, value: string | undefined, allowed: readonly T[]) {
    if (value && !allowed.includes(value as T))
        fail(`Invalid --${label} "${value}". Options: ${allowed.join(', ')}`)
}

function asString(value: unknown): string {
    if (p.isCancel(value)) { p.cancel('Cancelled.'); process.exit(0) }
    return value as string
}

function fail(message: string): never {
    p.cancel(message)
    process.exit(1)
}

function hasDotnet(): boolean {
    try { execSync('dotnet --version', { stdio: 'ignore' }); return true } catch { return false }
}

function carbonCliInstalled(): boolean {
    try { return /dotcarbon\.cli/i.test(execSync('dotnet tool list --global', { encoding: 'utf-8' })) }
    catch { return false }
}

function runToolInstall(): Promise<void> {
    return new Promise((resolve, reject) => {
        const proc = spawn('dotnet', ['tool', 'install', '--global', 'DotCarbon.Cli'], {
            stdio: 'ignore', shell: process.platform === 'win32',
        })
        proc.on('close', code => code === 0 ? resolve() : reject(new Error(`exited with code ${code}`)))
        proc.on('error', reject)
    })
}

async function ensureCarbonCli(autoYes: boolean, interactive: boolean) {
    if (carbonCliInstalled()) { p.log.success('Carbon CLI is installed'); return }

    if (!hasDotnet()) {
        p.log.warn('Carbon CLI needs the .NET SDK: https://dotnet.microsoft.com/download\n   Then: dotnet tool install -g DotCarbon.Cli')
        return
    }

    let install = autoYes
    if (!install && interactive) {
        const r = await p.confirm({ message: 'Install the Carbon CLI now? (dotnet tool install -g DotCarbon.Cli)' })
        install = p.isCancel(r) ? false : r
    }
    if (!install) { p.log.info('Install the CLI later: dotnet tool install -g DotCarbon.Cli'); return }

    const s = p.spinner()
    s.start('Installing Carbon CLI')
    try { await runToolInstall(); s.stop('Carbon CLI installed') }
    catch { s.stop('Carbon CLI install failed — run: dotnet tool install -g DotCarbon.Cli') }
}

function detectPackageManager(): PackageManager {
    for (const m of ['pnpm', 'bun', 'yarn', 'npm'] as const) {
        try { execSync(`${m} --version`, { stdio: 'ignore' }); return m } catch { /* keep looking */ }
    }
    return 'npm'
}

// Resolves true when deps are usable. pnpm exits non-zero on its ignored-builds
// warning even though node_modules is fully populated — treat that as success.
function runInstall(pm: PackageManager, cwd: string): Promise<boolean> {
    return new Promise((resolve) => {
        const proc = spawn(pm, ['install'], { cwd, stdio: 'ignore', shell: process.platform === 'win32' })
        proc.on('close', code => resolve(code === 0 || existsSync(join(cwd, 'node_modules'))))
        proc.on('error', () => resolve(false))
    })
}

function runDotnetRestore(cwd: string): Promise<boolean> {
    return new Promise((resolve) => {
        const proc = spawn('dotnet', ['restore'], { cwd, stdio: 'ignore', shell: process.platform === 'win32' })
        proc.on('close', code => resolve(code === 0))
        proc.on('error', () => resolve(false))
    })
}

async function replaceInDir(dir: string, search: string, replace: string) {
    for (const entry of await readdir(dir)) {
        const fullPath = join(dir, entry)
        if ((await stat(fullPath)).isDirectory()) {
            await replaceInDir(fullPath, search, replace)
        } else {
            try {
                const content = await readFile(fullPath, 'utf-8')
                if (content.includes(search)) await writeFile(fullPath, content.replaceAll(search, replace))
            } catch { /* skip binary/unreadable */ }
        }
    }
}

main().catch(err => {
    p.cancel(`Failed: ${err.message}`)
    process.exit(1)
})
