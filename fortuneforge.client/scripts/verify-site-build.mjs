// Edited: 2026-11-18T22:49:50-06:00 | Computer: Vinnabok
import { readdir, readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { SITE_ASSET_DIRECTORY, SITE_OUTPUT_DIRECTORY } from '../../scripts/site-release-contract.mjs'
import { assertSiteModuleBoundary } from '../build/siteBuildBoundary.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))
const directory = path.join(root, SITE_OUTPUT_DIRECTORY)
async function filesAt(directory, prefix = '') {
  const files = []
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const name = prefix + entry.name
    if (entry.isDirectory()) files.push(...await filesAt(path.join(directory, entry.name), name + '/'))
    else if (entry.isFile()) files.push(name)
    else throw new Error(`Site artifact contains unsupported file: ${name}`)
  }
  return files
}
const files = await filesAt(directory)
for (const file of files) {
  if (file !== 'index.html' && !file.startsWith(`${SITE_ASSET_DIRECTORY}/`)) throw new Error(`Site artifact escapes its namespace: ${file}`)
}
const graphs = files.filter(file => new RegExp(`^${SITE_ASSET_DIRECTORY}/site-graph-[A-Za-z0-9_-]+\\.json$`).test(file))
if (graphs.length !== 1) throw new Error('Site artifact must have exactly one module graph.')
const graph = JSON.parse(await readFile(path.join(directory, graphs[0]), 'utf8'))
if (graph.schemaVersion !== 1 || !Array.isArray(graph.modules) || !graph.modules.length || !graph.modules.every(id => typeof id === 'string')) throw new Error('Site module graph is invalid.')
assertSiteModuleBoundary(graph.modules)
const html = await readFile(path.join(directory, 'index.html'), 'utf8')
if (!html.includes('<div id="root"></div>') || !html.includes(`/${SITE_ASSET_DIRECTORY}/`)) throw new Error('Site entry document is missing its isolated bundle.')
console.log(`Site build verified: ${files.length} files; ${graph.modules.length} modules; no playable game clients.`)
