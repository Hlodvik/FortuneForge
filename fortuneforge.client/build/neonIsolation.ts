import { resolve } from 'node:path'
import type { Plugin } from 'vite'

/** Check emitted modules, rather than trusting source import spelling or chunk names. */
export function neonIsolation(root: string): Plugin {
  const normalized = (value: string) => value.replaceAll('\\', '/').toLowerCase()
  const neonRoot = normalized(resolve(root, 'games/NeonNights/client/FortuneForge.Games.NeonNights.Client')) + '/'
  const hostSource = normalized(resolve(root, 'fortuneforge.client/src')) + '/'
  const gamesRoot = normalized(resolve(root, 'games')) + '/'
  const sharedGamesRoot = normalized(resolve(root, 'games/shared')) + '/'
  return {
    name: 'verify-neon-module-loading',
    apply: 'build',
    generateBundle(_options, bundle) {
      const chunks = new Map(Object.values(bundle)
        .filter(output => output.type === 'chunk')
        .map(chunk => [chunk.fileName, chunk]))
      const modulesOf = (chunk: { modules: Record<string, unknown> }) => Object.keys(chunk.modules).map(normalized)
      const isNeon = (chunk: { modules: Record<string, unknown> }) => modulesOf(chunk).some(id => id.startsWith(neonRoot))
      const neonChunks = [...chunks.values()].filter(isNeon)
      if (neonChunks.length === 0) throw new Error('No standalone Neon client chunk was emitted.')
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
      const errors = new Set<string>()
      const pathToNeon = (name: string, seen = new Set<string>()): string[] | undefined => {
        if (seen.has(name)) return
        seen.add(name)
        const chunk = chunks.get(name)
        if (!chunk) return
        if (isNeon(chunk)) return [name]
        for (const dependency of chunk.imports) {
          const path = pathToNeon(dependency, seen)
          if (path) return [name, ...path]
        }
      }
      for (const entry of neonChunks) {
        for (const chunk of closure(entry)) for (const id of modulesOf(chunk)) {
          if (id.startsWith(hostSource)) errors.add(`Neon loads host application source: ${id}`)
          if (id.startsWith(gamesRoot) && !id.startsWith(neonRoot) && !id.startsWith(sharedGamesRoot)) {
            errors.add(`Neon loads another game: ${id}`)
          }
        }
      }
      for (const entry of chunks.values()) {
        if (isNeon(entry)) continue
        const modules = modulesOf(entry)
        const isGenericSlot = modules.some(id => id === `${hostSource}features/slots/slotspageview.tsx` ||
          id === `${hostSource}features/slots/useslotspagecontroller.ts`)
        const isOtherGame = entry.isDynamicEntry && modules.some(id =>
          id.startsWith(gamesRoot) && !id.startsWith(neonRoot) && !id.startsWith(sharedGamesRoot))
        if ((entry.isEntry || isGenericSlot || isOtherGame) && closure(entry).some(isNeon)) {
          errors.add(`Eager Neon import: ${pathToNeon(entry.fileName)?.join(' -> ')}`)
        }
      }
      if (errors.size) throw new Error([...errors].join('\n'))
      console.log(`Verified ${neonChunks.length} Neon chunks: independent static load graph; no eager Neon loading from lobby or other games.`)
    },
  }
}
