import { rm } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import './sync-rules.mjs'

const packageRoot = path.resolve(fileURLToPath(new URL('../', import.meta.url)))
const destination = path.resolve(packageRoot, 'dist')
if (!destination.startsWith(packageRoot + path.sep)) throw new Error('Build destination escaped the game package.')
await rm(destination, { recursive: true, force: true })
