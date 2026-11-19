// Edited: 2026-11-18T22:54:10-06:00 | Computer: Vinnabok
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { assertSiteModuleBoundary } from '../build/siteBuildBoundary.mjs'

const clientRoot = fileURLToPath(new URL('..', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url))
const result = spawnSync(process.execPath, [fileURLToPath(import.meta.resolve('typescript/bin/tsc')), '-p', path.join(clientRoot, 'tsconfig.site.json'), '--listFiles'],
  { cwd: clientRoot, encoding: 'utf8', windowsHide: true, maxBuffer: 16 * 1024 * 1024 })
if (result.error) throw result.error
const lines = result.stdout.split(/\r?\n/).filter(Boolean)
const modules = lines.filter(line => path.isAbsolute(line)).map(file => path.relative(repositoryRoot, file).replaceAll('\\', '/'))
assertSiteModuleBoundary(modules)
if (result.status !== 0) {
  process.stderr.write(lines.filter(line => !path.isAbsolute(line)).join('\n') + '\n' + result.stderr)
  process.exitCode = result.status || 1
} else console.log(`Site TypeScript verified: ${modules.length} files; no playable game source or declaration dependencies.`)
