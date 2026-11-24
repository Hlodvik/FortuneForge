import { cp, mkdir, readdir, rm, stat } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = path.resolve(fileURLToPath(new URL('../', import.meta.url)))
await mkdir(path.join(root, 'dist'), { recursive: true })
const destination = path.resolve(root, 'dist/assets')
if (!destination.startsWith(root + path.sep)) throw new Error('Asset destination escaped the game package.')
await rm(destination, { recursive: true, force: true })
await cp(path.join(root, 'src/assets'), destination, {
  recursive: true,
  filter: async source => {
    const relative = path.relative(path.join(root, 'src/assets'), source).split(path.sep).join('/')
    if (relative === 'frontier/bonus' || relative.startsWith('frontier/bonus/') || relative.endsWith('frontier-showdown.webp')) return false
    return (await stat(source)).isDirectory() || /\.(webp|json)$/.test(source)
  },
})
for (const file of await readdir(path.join(root, 'src'))) {
  if (file.endsWith('.css')) await cp(path.join(root, 'src', file), path.join(root, 'dist', file))
}
