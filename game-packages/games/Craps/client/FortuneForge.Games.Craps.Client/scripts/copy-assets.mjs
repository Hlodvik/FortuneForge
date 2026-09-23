import { copyFile, mkdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const outputDirectory = resolve(packageRoot, 'dist/assets')

await mkdir(outputDirectory, { recursive: true })
await copyFile(
  resolve(packageRoot, 'src/assets/craps-table-backdrop.png'),
  resolve(outputDirectory, 'craps-table-backdrop.png'),
)
