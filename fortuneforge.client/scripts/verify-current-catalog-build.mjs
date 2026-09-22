import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

const outputDirectory = resolve('dist')
const retiredCatalogueLabels = ['In the forge', 'Balut', 'Falling Blocks', 'Shoot ’Em Up!', 'Etc.']

if (!existsSync(outputDirectory)) throw new Error('Expected Vite to create dist before catalogue verification.')

const files = collectTextFiles(outputDirectory)
const retiredOutput = retiredCatalogueLabels.flatMap((label) => files
  .filter((file) => readFileSync(file, 'utf8').includes(label))
  .map((file) => `${label} in ${file}`))

if (retiredOutput.length > 0) {
  throw new Error(`Deprecated catalogue content reached the production build:\n${retiredOutput.join('\n')}`)
}

function collectTextFiles(directory) {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry)
    if (statSync(path).isDirectory()) return collectTextFiles(path)
    return /\.(?:html|js|css|json)$/i.test(path) ? [path] : []
  })
}
