import { resolve } from 'node:path'
import type { Plugin } from 'vite'

type HostedSlotIsolationOptions = Readonly<{
  root: string
  gameLabel: string
  pluginName: string
  hostEntry: string
}>

/** Verifies a host-rendered slot shell remains a lazy, game-independent chunk. */
export function hostedSlotIsolation({
  root,
  gameLabel,
  pluginName,
  hostEntry,
}: HostedSlotIsolationOptions): Plugin {
  const normalized = (value: string) => value.replaceAll('\\', '/').toLowerCase()
  const allowedHostEntry = normalized(resolve(root, hostEntry))
  const gamesRoot = normalized(resolve(root, 'games')) + '/'
  const manifestLoaderRoot = normalized(resolve(root, 'fortuneforge.client/src/games/slots/manifestLoaders')) + '/'
  const routeRoot = normalized(resolve(root, 'fortuneforge.client/src/app/routes')) + '/'
  const isHostEntry = (id: string) => id === allowedHostEntry || id.startsWith(`${allowedHostEntry}?`)

  return {
    name: pluginName,
    apply: 'build',
    generateBundle(_options, bundle) {
      const chunks = new Map(Object.values(bundle)
        .filter(output => output.type === 'chunk')
        .map(chunk => [chunk.fileName, chunk]))
      const modulesOf = (chunk: { modules: Record<string, unknown> }) => Object.keys(chunk.modules).map(normalized)
      const routeChunks = [...chunks.values()].filter(chunk => modulesOf(chunk).some(isHostEntry))
      if (routeChunks.length !== 1) {
        throw new Error(`Expected one lazy ${gameLabel} host chunk, found ${routeChunks.length}.`)
      }

      const closure = (start: { fileName: string }) => {
        const visited = new Set<string>()
        const visit = (name: string) => {
          if (visited.has(name)) return
          visited.add(name)
          for (const dependency of chunks.get(name)?.imports ?? []) visit(dependency)
        }
        visit(start.fileName)
        return [...visited].flatMap(name => chunks.has(name) ? [chunks.get(name)!] : [])
      }

      const routeChunk = routeChunks[0]
      const errors = new Set<string>()
      for (const chunk of closure(routeChunk)) for (const id of modulesOf(chunk)) {
        if (id.startsWith(gamesRoot)) errors.add(`${gameLabel} host chunk loads a game client package: ${id}`)
        if (id.startsWith(manifestLoaderRoot)) errors.add(`${gameLabel} host chunk loads another slot route: ${id}`)
        if (id.startsWith(routeRoot) && !isHostEntry(id)) errors.add(`${gameLabel} host chunk loads another application route: ${id}`)
      }
      for (const chunk of chunks.values()) {
        if (chunk.fileName === routeChunk.fileName) continue
        if (closure(chunk).some(candidate => candidate.fileName === routeChunk.fileName)) {
          errors.add(`Eager ${gameLabel} host import from ${chunk.fileName}.`)
        }
      }

      if (errors.size) throw new Error([...errors].join('\n'))
      console.log(`Verified 1 ${gameLabel} host chunk: independent static load graph and route-only loading.`)
    },
  }
}
