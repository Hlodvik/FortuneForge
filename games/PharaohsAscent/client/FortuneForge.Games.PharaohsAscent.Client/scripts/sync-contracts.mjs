import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'

// These are generated release assets; the game-root contracts remain authoritative.
const check = process.argv.includes('--check')
const destination = new URL('../src/assets/contracts/', import.meta.url)
if (!check) mkdirSync(destination, { recursive: true })
for (const name of ['guide-contract.json', 'math-v3.json', 'wagering.json']) {
  const authoritative = readFileSync(new URL(`../../../${name}`, import.meta.url))
  const target = new URL(name, destination)
  let actual
  try { actual = readFileSync(target) }
  catch (error) { if (error.code !== 'ENOENT') throw error }
  if (!actual?.equals(authoritative)) {
    if (check) throw new Error(`Stale Pharaoh client contract: ${name}. Run npm run build in this package.`)
    writeFileSync(target, authoritative)
  }
}
