#!/usr/bin/env node

import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { spawnSync } from 'node:child_process'
import { tmpdir } from 'node:os'

const root = process.cwd()
const packagesRoot = join(root, 'packages')
const npmQueryCwd = join(tmpdir(), 'dotcarbon-npm-query')
const rawVersion = process.env.RELEASE_VERSION ?? process.argv[2]
const dryRun = process.env.CARBON_NPM_DRY_RUN === 'true'
const verifyOnly = process.env.CARBON_NPM_VERIFY_ONLY === 'true'
const provenance =
  process.env.CARBON_NPM_PROVENANCE !== 'false' && process.env.GITHUB_ACTIONS === 'true'

if (process.env.NODE_AUTH_TOKEN === 'XXXXX-XXXXX-XXXXX-XXXXX') {
  delete process.env.NODE_AUTH_TOKEN
}

if (!rawVersion?.trim()) {
  fail('Missing RELEASE_VERSION. Pass a release tag like v0.6.0 or set RELEASE_VERSION.')
}

mkdirSync(npmQueryCwd, { recursive: true })

const version = rawVersion.trim().replace(/^v/, '')
if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(version)) {
  fail(`Invalid npm package version: ${version}`)
}

const packages = readdirSync(packagesRoot, { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .map((entry) => {
    const dir = join(packagesRoot, entry.name)
    const packageJson = join(dir, 'package.json')
    if (!existsSync(packageJson)) return null
    const pkg = JSON.parse(readFileSync(packageJson, 'utf8'))
    return { dir, packageJson, pkg }
  })
  .filter(Boolean)
  .filter((entry) => entry.pkg.name)
  .sort((a, b) => a.pkg.name.localeCompare(b.pkg.name))

if (packages.length === 0) {
  fail('No workspace packages found under dotcarbon-js/packages.')
}

console.log(`[Carbon] Preparing ${packages.length} workspace packages for ${version}.`)
console.log('[Carbon] npm auth mode: trusted publishing / OIDC only.')
for (const entry of packages) {
  if (entry.pkg.version === version) continue

  entry.pkg.version = version
  if (dryRun || verifyOnly) {
    const mode = dryRun ? 'dry-run' : 'verify-only'
    console.log(`[Carbon] [${mode}] Would set ${entry.pkg.name} to ${version}.`)
    continue
  }

  writeFileSync(entry.packageJson, `${JSON.stringify(entry.pkg, null, 4)}\n`)
}

const publishable = packages.filter((entry) => entry.pkg.private !== true)
console.log(`[Carbon] Publishable packages: ${publishable.map((entry) => entry.pkg.name).join(', ')}`)

if (!dryRun) {
  console.log('[Carbon] Building workspace packages before publish...')
  const build = spawnSync('pnpm', ['--recursive', '--if-present', 'run', 'build'], {
    cwd: root,
    env: process.env,
    stdio: 'inherit'
  })

  if (build.status !== 0) {
    fail('Workspace package build failed; nothing was published.')
  }
}

const publishQueue = []

for (const entry of publishable) {
  const { name } = entry.pkg
  const spec = `${name}@${version}`

  if (dryRun) {
    console.log(`[Carbon] [dry-run] Would check and publish ${spec}.`)
    continue
  }

  if (registryFieldExists(spec, 'version')) {
    console.log(`[Carbon] Skipping ${spec}; it already exists on npm.`)
    continue
  }

  publishQueue.push({ entry, spec })
}

if (
  publishQueue.length > 0 &&
  !dryRun &&
  !verifyOnly &&
  (!process.env.GITHUB_ACTIONS ||
    !process.env.ACTIONS_ID_TOKEN_REQUEST_URL ||
    !process.env.ACTIONS_ID_TOKEN_REQUEST_TOKEN)
) {
  fail('Publishing requires GitHub Actions OIDC. Rerun this from the trusted publish.yml workflow.')
}

for (const { entry, spec } of publishQueue) {
  if (verifyOnly) {
    console.log(`[Carbon] [verify-only] Would publish ${spec}.`)
    continue
  }

  console.log(`[Carbon] Publishing ${spec} from ${relative(root, entry.dir)}...`)
  const args = ['--dir', entry.dir, 'publish', '--access', 'public', '--no-git-checks']
  if (provenance) args.push('--provenance')

  const result = spawnSync('pnpm', args, {
    cwd: root,
    env: process.env,
    stdio: 'inherit'
  })

  if (result.status !== 0) {
    console.error('')
    console.error(`[Carbon] Failed to publish ${spec}.`)
    console.error(
      '[Carbon] If npm reported an OIDC token-exchange 404, verify this package has npm trusted publishing configured for GitHub Actions: owner DanKaufmanDev, repository dotcarbon, workflow filename publish.yml, and allowed action npm publish.'
    )
    process.exit(result.status ?? 1)
  }
}

console.log('[Carbon] npm publish complete.')

function registryFieldExists(spec, field) {
  const result = spawnSync('npm', ['view', spec, field, '--json'], {
    cwd: npmQueryCwd,
    env: process.env,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe']
  })

  if (result.status === 0) return true

  const output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`
  if (/\bE404\b|404 Not Found|not found/i.test(output)) return false

  console.error(output.trim())
  fail(`Unable to query npm for ${spec}.`)
}

function fail(message) {
  console.error(`[Carbon] ${message}`)
  process.exit(1)
}
