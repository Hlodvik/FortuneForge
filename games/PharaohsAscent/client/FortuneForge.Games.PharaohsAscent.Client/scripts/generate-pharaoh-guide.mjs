import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const directory = dirname(fileURLToPath(import.meta.url))
const repository = resolve(directory, '../../../../..')
const source = resolve(repository, 'games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotDefinitionFactory.cs')
const text = readFileSync(source, 'utf8')
const declaration = /CreatePaylines\(\) =>\s*(\[[\s\S]*?\]);/.exec(text)
if (!declaration) throw new Error('The authoritative classic payline declaration was not found.')
const patterns = JSON.parse(declaration[1])
if (!Array.isArray(patterns) || !patterns.length || patterns.some(rows =>
  !Array.isArray(rows) || rows.length !== 5 || rows.some(row => !Number.isInteger(row) || row < 0 || row >= 4)))
  throw new Error('The authoritative payline layout has an unsupported shape.')
const output = resolve(directory, '../src/assets/rules/payline-layouts.json')
mkdirSync(dirname(output), { recursive: true })
writeFileSync(output, JSON.stringify({
  schemaVersion: 1,
  source: relative(repository, source).replaceAll('\\', '/') + '#CreatePaylines',
  sourceSha256: createHash('sha256').update(text).digest('hex'),
  generator: relative(repository, fileURLToPath(import.meta.url)).replaceAll('\\', '/'),
  patterns,
}, null, 2) + '\n')
console.log(`Exported ${patterns.length} authoritative payline shapes for the illustrated Pharaoh guide.`)
