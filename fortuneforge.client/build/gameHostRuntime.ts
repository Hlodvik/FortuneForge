import { createRequire } from 'node:module'
import type { Plugin, ResolvedConfig } from 'vite'
import { gameHostRuntime } from '../../scripts/game-release-runtime.mjs'
import { productionGameReleaseBaseUrl } from '../src/features/game-releases/productionGameRelease.js'

const require = createRequire(import.meta.url)
const virtualPrefix = 'virtual:fortuneforge-game-runtime/'

export function gameHostRuntimePlugin(): Plugin {
  let config: ResolvedConfig
  const modules = Object.entries(gameHostRuntime)
  const enabled = () => Boolean(config.env.VITE_GAME_RELEASE_BASE_URL?.trim() ||
    (config.command === 'build' && config.mode === 'production' && productionGameReleaseBaseUrl))

  return {
    name: 'fortuneforge-game-host-runtime',
    configResolved(resolved) { config = resolved },
    buildStart() {
      if (!enabled() || config.command !== 'build') return
      for (const [specifier] of modules) {
        this.emitFile({
          type: 'chunk', id: virtualPrefix + specifier,
          name: 'game-host-' + specifier.replaceAll('/', '-'), preserveSignature: 'strict',
        })
      }
    },
    resolveId(id) {
      if (enabled() && id.startsWith(virtualPrefix) && Object.hasOwn(gameHostRuntime, id.slice(virtualPrefix.length))) {
        return '\0' + id
      }
    },
    load(id) {
      if (!id.startsWith('\0' + virtualPrefix)) return
      const specifier = id.slice(virtualPrefix.length + 1)
      const names = Object.keys(require(specifier)).filter(name => name !== 'default' && !name.startsWith('__'))
      return [
        'import runtime from ' + JSON.stringify(specifier),
        'export default runtime',
        'export const { ' + names.join(', ') + ' } = runtime',
      ].join('\n')
    },
    transformIndexHtml: {
      order: 'post',
      handler(_html, { bundle }) {
        if (!enabled()) return
        const imports = Object.fromEntries(modules.map(([specifier, alias]) => {
          const id = virtualPrefix + specifier
          if (!bundle) return [alias, config.base + '@id/__x00__' + id]
          const chunk = Object.values(bundle).find(output => output.type === 'chunk' && output.facadeModuleId === '\0' + id)
          if (!chunk) throw new Error('Game host runtime chunk missing for ' + specifier)
          return [alias, config.base + chunk.fileName]
        }))
        return [{ tag: 'script', attrs: { type: 'importmap' }, children: JSON.stringify({ imports }), injectTo: 'head-prepend' }]
      },
    },
  }
}
