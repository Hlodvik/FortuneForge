import { copyFileSync, mkdirSync, readdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
function copyStyles(relative = '') {
  for (const entry of readdirSync(resolve(root, 'src', relative), { withFileTypes: true })) {
    const path = relative ? `${relative}/${entry.name}` : entry.name
    if (entry.isDirectory()) copyStyles(path)
    else if (entry.name.endsWith('.css')) {
      const destination = resolve(root, 'dist', path)
      mkdirSync(dirname(destination), { recursive: true })
      copyFileSync(resolve(root, 'src', path), destination)
    }
  }
}
copyStyles()
