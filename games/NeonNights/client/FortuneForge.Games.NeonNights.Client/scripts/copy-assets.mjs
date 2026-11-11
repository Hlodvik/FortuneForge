import { cpSync, mkdirSync, readdirSync } from 'node:fs'

const source = new URL('../src/', import.meta.url)
const destination = new URL('../dist/', import.meta.url)
mkdirSync(destination, { recursive: true })
cpSync(new URL('assets/', source), new URL('assets/', destination), { recursive: true })

for (const path of readdirSync(source, { recursive: true })) {
  if (!path.endsWith('.css')) continue
  const relative = path.replaceAll('\\', '/')
  const target = new URL(relative, destination)
  mkdirSync(new URL('.', target), { recursive: true })
  cpSync(new URL(relative, source), target)
}
