import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync } from 'node:fs'

const source = new URL('../src/', import.meta.url)
const destination = new URL('../dist/', import.meta.url)

if (process.argv.includes('--sync-contract')) {
  const contract = new URL('../../../src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json', import.meta.url)
  const generated = new URL('assets/contracts/crooked-road-v1.json', source)
  mkdirSync(new URL('.', generated), { recursive: true })
  if (!existsSync(generated) || !readFileSync(generated).equals(readFileSync(contract))) cpSync(contract, generated)
} else {
  mkdirSync(destination, { recursive: true })
  cpSync(new URL('assets/', source), new URL('assets/', destination), { recursive: true })
  for (const path of readdirSync(source, { recursive: true })) {
    if (!path.endsWith('.css')) continue
    const relative = path.replaceAll('\\', '/')
    const target = new URL(relative, destination)
    mkdirSync(new URL('.', target), { recursive: true })
    cpSync(new URL(relative, source), target)
  }
}
