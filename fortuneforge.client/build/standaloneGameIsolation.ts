import { resolve } from 'node:path'
import type { Plugin } from 'vite'

type StandaloneGameIsolationOptions = Readonly<{
  root: string
  gameLabel: string
  pluginName: string
  clientRoot: string
  hostEntry: string
}>

/** Checks the emitted static graph, not source spelling or chunk filenames. */
export function standaloneGameIsolation({
  root,
  gameLabel,
  pluginName,
  clientRoot,
  hostEntry,
}: StandaloneGameIsolationOptions): Plugin {
  const normalized = (value: string) => value.replaceAll('\\', '/').toLowerCase()
  const gameClientRoot = normalized(resolve(root, clientRoot)) + '/'
  const allowedHostEntry = normalized(resolve(root, hostEntry))
  const hostSource = normalized(resolve(root, 'fortuneforge.client/src')) + '/'
  const gamesRoot = normalized(resolve(root, 'games')) + '/'
  const sharedGamesRoot = normalized(resolve(root, 'games/shared')) + '/'
  const isAllowedHostModule = (id: string) => id === allowedHostEntry || id.startsWith(`${allowedHostEntry}?`)

  return {
    name: pluginName,
    apply: 'build',
    generateBundle(_options, bundle) {
      const chunks = new Map(Object.values(bundle)
        .filter(output => output.type === 'chunk')
        .map(chunk => [chunk.fileName, chunk]))
      const modulesOf = (chunk: { modules: Record<string, unknown> }) => Object.keys(chunk.modules).map(normalized)
      const isGame = (chunk: { modules: Record<string, unknown> }) => modulesOf(chunk)
        .some(id => id.startsWith(gameClientRoot))
      const isForeignGameModule = (id: string) => id.startsWith(gamesRoot) &&
        !id.startsWith(gameClientRoot) && !id.startsWith(sharedGamesRoot)
      const gameChunks = [...chunks.values()].filter(isGame)

      if (gameChunks.length === 0) throw new Error(`No standalone ${gameLabel} client chunk was emitted.`)

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
      const pathToGame = (start: string): string[] | undefined => {
        const queue: string[][] = [[start]]
        const visited = new Set<string>()
        while (queue.length) {
          const path = queue.shift()!
          const name = path.at(-1)!
          if (visited.has(name)) continue
          visited.add(name)
          const chunk = chunks.get(name)
          if (!chunk) continue
          if (isGame(chunk)) return path
          for (const dependency of chunk.imports) queue.push([...path, dependency])
        }
      }

      const errors = new Set<string>()
      for (const entry of gameChunks) {
        for (const chunk of closure(entry)) for (const id of modulesOf(chunk)) {
          if (id.startsWith(hostSource)) errors.add(`${gameLabel} loads host application source: ${id}`)
          if (isForeignGameModule(id)) errors.add(`${gameLabel} loads another game: ${id}`)
        }
      }

      for (const entry of chunks.values()) {
        if (isGame(entry)) continue
        const reachable = closure(entry)
        if (!reachable.some(isGame)) continue

        const modules = modulesOf(entry)
        const isAllowedHost = modules.some(isAllowedHostModule)
        if (!isAllowedHost) {
          errors.add(`Eager ${gameLabel} import: ${pathToGame(entry.fileName)?.join(' -> ') ?? entry.fileName}`)
          continue
        }

        // The one permitted host adapter may compose account/navigation chrome,
        // but opening it must never pull a second game's package into memory.
        for (const chunk of reachable) for (const id of modulesOf(chunk)) {
          if (isForeignGameModule(id)) errors.add(`${gameLabel} host adapter loads another game: ${id}`)
        }
      }

      if (errors.size) throw new Error([...errors].join('\n'))
      console.log(`Verified ${gameChunks.length} ${gameLabel} chunks: independent static load graph and route-only loading.`)
    },
  }
}
