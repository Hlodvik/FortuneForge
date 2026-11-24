import { spawnSync } from 'node:child_process'
import { realpathSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

// Node and Vite must use the same path when this checkout is opened through a Windows junction.
const client = realpathSync(fileURLToPath(new URL('../', import.meta.url)))
const require = createRequire(import.meta.url)
const browser = process.argv[2] === '--browser'
const directory = dirname(require.resolve(browser ? '@playwright/test/package.json' : 'vitest/package.json'))
const runner = resolve(directory, browser ? 'cli.js' : 'vitest.mjs')
const result = spawnSync(process.execPath, [runner, browser ? 'test' : 'run', '--config',
  resolve(client, browser ? 'playwright.config.ts' : 'vitest.config.ts'), ...process.argv.slice(browser ? 3 : 2)],
{ cwd: client, env: process.env, stdio: 'inherit', windowsHide: true })
if (result.error) throw result.error
process.exitCode = result.status ?? 1
