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

const diceOutputDirectory = resolve(outputDirectory, 'dice')
await mkdir(diceOutputDirectory, { recursive: true })
for (let face = 1; face <= 4; face++) {
  const fileName = `dice-throw-${face}.png`
  await copyFile(
    resolve(packageRoot, 'src/assets/dice', fileName),
    resolve(diceOutputDirectory, fileName),
  )
}
