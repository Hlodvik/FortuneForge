import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'

// These are generated release assets; the game-root contracts remain authoritative.
const check = process.argv.includes('--check')
const destination = new URL('../src/assets/contracts/', import.meta.url)
if (!check) mkdirSync(destination, { recursive: true })
for (const name of ['guide-contract.json', 'math-v3.json', 'wagering.json']) {
  const authoritative = readFileSync(new URL(`../../../${name}`, import.meta.url))
  const target = new URL(name, destination)
  if (check) {
    let actual
    try { actual = readFileSync(target) } catch { /* Missing generated assets fail the same freshness check. */ }
    if (!actual?.equals(authoritative)) throw new Error(`Stale Pharaoh client contract: ${name}. Run npm run build in this package.`)
  } else writeFileSync(target, authoritative)
}
