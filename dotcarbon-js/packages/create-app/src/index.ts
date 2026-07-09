import { writeFile, readFile, cp, unlink, rename, readdir, stat } from 'fs/promises'
import { existsSync } from 'fs'
import { join, dirname, sep } from 'path'
import { fileURLToPath } from 'url'
import { execSync, spawn } from 'child_process'
import { createInterface } from 'node:readline'

const __dirname = dirname(fileURLToPath(import.meta.url))

const TEMPLATES = ['react', 'vue', 'svelte', 'solid', 'preact', 'vanilla'] as const
type Template = typeof TEMPLATES[number]
type PackageManager = 'pnpm' | 'npm' | 'bun' | 'yarn'

const EXCLUDE_DIRS = new Set(['node_modules', 'dist', 'bin', 'obj', 'out'])

const c = {
    reset: '\x1b[0m', dim: '\x1b[2m', bold: '\x1b[1m',
    cyan: '\x1b[36m', green: '\x1b[32m', yellow: '\x1b[33m',
}

async function main() {
    const args = process.argv.slice(2)

    const appName = args.find(a => !a.startsWith('--')) ?? 'my-carbon-app'
    const templateArg = args.find((_, i) => args[i - 1] === '--template') ?? 'react'
    const pmArg = args.find((_, i) => args[i - 1] === '--pm')
    const noInstall = args.includes('--no-install')
    const autoYes = args.includes('--yes') || args.includes('-y')
    const template = TEMPLATES.includes(templateArg as Template)
        ? templateArg as Template
        : 'react'

    if (!TEMPLATES.includes(templateArg as Template) && args.includes('--template')) {
        console.error(`❌ Unknown template: ${templateArg}`)
        console.error(`   Available: ${TEMPLATES.join(', ')}`)
        process.exit(1)
    }

    const pm: PackageManager = (pmArg as PackageManager) ?? detectPackageManager()

    console.log(`\n${c.bold}${c.cyan}⚡ Creating Carbon app${c.reset} ${c.bold}${appName}${c.reset}`)
    console.log(`${c.dim}   Template:        ${template}${c.reset}`)
    console.log(`${c.dim}   Package manager: ${pm}${c.reset}\n`)

    const targetDir = join(process.cwd(), appName)

    if (existsSync(targetDir)) {
        console.error(`❌ Directory already exists: ${targetDir}`)
        process.exit(1)
    }

    const templateDir = join(__dirname, 'templates', template)

    if (!existsSync(templateDir)) {
        console.error(`❌ Template not found: ${template}`)
        console.error(`   Available: ${TEMPLATES.join(', ')}`)
        process.exit(1)
    }

    process.stdout.write('📁 Copying template...')
    await cp(templateDir, targetDir, {
        recursive: true,
        filter: (src) => {
            const rel = src.slice(templateDir.length)
            return !rel.split(sep).some(seg => EXCLUDE_DIRS.has(seg))
        },
    })

    const tmplGitignore = join(targetDir, 'gitignore')
    if (existsSync(tmplGitignore)) {
        await rename(tmplGitignore, join(targetDir, '.gitignore'))
    }

    const schemaSrc = join(__dirname, 'shared', 'carbon.schema.json')
    if (existsSync(schemaSrc)) {
        await cp(schemaSrc, join(targetDir, 'carbon.schema.json'))
    }

    await replaceInDir(targetDir, '{{APP_NAME}}', appName)
    await replaceInDir(targetDir, '{{PM}}', pm)

    const oldCsproj = join(targetDir, 'src-carbon', 'APP_NAME.csproj')
    const newCsproj = join(targetDir, 'src-carbon', `${appName}.csproj`)
    if (existsSync(oldCsproj)) {
        const content = await readFile(oldCsproj, 'utf-8')
        await writeFile(newCsproj, content.replaceAll('{{APP_NAME}}', appName))
        await unlink(oldCsproj)
    }
    console.log(' ✅')

    if (noInstall) {
        console.log('⏭️  Skipping dependency install (--no-install)')
    } else {
        process.stdout.write(`📦 Installing frontend dependencies with ${pm}...`)
        try {
            await runInstall(pm, join(targetDir, 'ui'))
            console.log(' ✅')
        } catch (err) {
            console.log(' ⚠️')
            console.warn(`   Skipped: ${(err as Error).message}`)
            console.warn(`   Run "${pm} install" inside ${appName}/ui later.`)
        }

        process.stdout.write('🔧 Restoring .NET packages...')
        try {
            await runDotnetRestore(join(targetDir, 'src-carbon'))
            console.log(' ✅')
        } catch (err) {
            console.log(' ⚠️')
            console.warn(`   Skipped: ${(err as Error).message}`)
            console.warn('   DotCarbon.Core may not be published to NuGet yet.')
            console.warn(`   Run "dotnet restore" inside ${appName}/src-carbon once it is.`)
        }
    }

    await ensureCarbonCli(autoYes)

    console.log(`\n✅ Created ${appName}!\n`)
    console.log('Get started:\n')
    console.log(`  cd ${appName}`)
    if (noInstall) {
        console.log(`  ${pm} --prefix ui install`)
    }
    console.log('  carbon dev\n')
    console.log('Happy building! ⚡\n')
}

function hasDotnet(): boolean {
    try {
        execSync('dotnet --version', { stdio: 'ignore' })
        return true
    } catch { return false }
}

function carbonCliInstalled(): boolean {
    try {
        const out = execSync('dotnet tool list --global', { encoding: 'utf-8' })
        return /dotcarbon\.cli/i.test(out)
    } catch { return false }
}

function promptYesNo(question: string, defaultYes = true): Promise<boolean> {
    if (!process.stdin.isTTY) return Promise.resolve(false)

    const rl = createInterface({ input: process.stdin, output: process.stdout })
    const hint = `${c.dim}${defaultYes ? '(Y/n)' : '(y/N)'}${c.reset}`
    return new Promise(resolve => {
        rl.question(`${c.cyan}?${c.reset} ${question} ${hint} `, answer => {
            rl.close()
            const a = answer.trim().toLowerCase()
            resolve(a === '' ? defaultYes : a === 'y' || a === 'yes')
        })
    })
}

function runToolInstall(): Promise<void> {
    return new Promise((resolve, reject) => {
        const proc = spawn('dotnet', ['tool', 'install', '--global', 'DotCarbon.Cli'], {
            stdio: 'ignore',
            shell: process.platform === 'win32',
        })
        proc.on('close', code => code === 0 ? resolve() : reject(new Error(`dotnet tool install exited with code ${code}`)))
        proc.on('error', reject)
    })
}

async function ensureCarbonCli(autoYes: boolean) {
    process.stdout.write('🔎 Checking for the Carbon CLI...')

    if (carbonCliInstalled()) {
        console.log(` ${c.green}✅${c.reset}`)
        return
    }
    console.log(` ${c.yellow}not found${c.reset}`)

    if (!hasDotnet()) {
        console.log(`${c.dim}   The Carbon CLI needs the .NET SDK — install .NET 10:${c.reset} https://dotnet.microsoft.com/download`)
        console.log(`${c.dim}   Then run:${c.reset} dotnet tool install -g DotCarbon.Cli`)
        return
    }

    const install = autoYes || await promptYesNo('   Install the Carbon CLI now?')
    if (!install) {
        console.log(`${c.dim}   Install later with:${c.reset} dotnet tool install -g DotCarbon.Cli`)
        return
    }

    process.stdout.write('⬇️  Installing Carbon CLI...')
    try {
        await runToolInstall()
        console.log(` ${c.green}✅${c.reset}`)
    } catch (err) {
        console.log(` ${c.yellow}⚠️${c.reset}`)
        console.log(`${c.dim}   ${(err as Error).message}${c.reset}`)
        console.log(`${c.dim}   Install manually:${c.reset} dotnet tool install -g DotCarbon.Cli`)
    }
}

function detectPackageManager(): PackageManager {
    const managers: PackageManager[] = ['pnpm', 'bun', 'yarn', 'npm']
    for (const pm of managers) {
        try {
            execSync(`${pm} --version`, { stdio: 'ignore' })
            return pm
        } catch {
        }
    }
    return 'npm'
}

function runInstall(pm: PackageManager, cwd: string): Promise<void> {
    return new Promise((resolve, reject) => {
        const args = pm === 'pnpm'
            ? ['install', '--ignore-workspace']
            : ['install']

        const proc = spawn(pm, args, {
            cwd,
            stdio: 'ignore',
            shell: process.platform === 'win32'
        })

        proc.on('close', code => {
            if (code === 0) resolve()
            else reject(new Error(`${pm} install failed with code ${code}`))
        })

        proc.on('error', reject)
    })
}

function runDotnetRestore(cwd: string): Promise<void> {
    return new Promise((resolve, reject) => {
        const proc = spawn('dotnet', ['restore'], {
            cwd,
            stdio: 'ignore',
            shell: process.platform === 'win32'
        })

        proc.on('close', code => {
            if (code === 0) resolve()
            else reject(new Error(`dotnet restore failed with code ${code}`))
        })

        proc.on('error', reject)
    })
}

async function replaceInDir(dir: string, search: string, replace: string) {
    const entries = await readdir(dir)

    for (const entry of entries) {
        const fullPath = join(dir, entry)
        const info = await stat(fullPath)

        if (info.isDirectory()) {
            await replaceInDir(fullPath, search, replace)
        } else {
            try {
                const content = await readFile(fullPath, 'utf-8')
                if (content.includes(search)) {
                    await writeFile(fullPath, content.replaceAll(search, replace))
                }
            } catch {
            }
        }
    }
}

main().catch(err => {
    console.error('\n❌ Failed:', err.message)
    process.exit(1)
})
