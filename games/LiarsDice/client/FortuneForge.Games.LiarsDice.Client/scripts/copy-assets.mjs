import { copyFile, mkdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const outputDirectory = resolve(packageRoot, 'dist/assets/dice')

await mkdir(outputDirectory, { recursive: true })
for (let face = 1; face <= 4; face++) {
  const fileName = `dice-throw-${face}.png`
  await copyFile(
    resolve(packageRoot, 'src/assets/dice', fileName),
    resolve(outputDirectory, fileName),
  )
}
