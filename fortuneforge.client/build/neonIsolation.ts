import type { Plugin } from 'vite'
import { standaloneGameIsolation } from './standaloneGameIsolation.js'

export function neonIsolation(root: string): Plugin {
  return standaloneGameIsolation({
    root,
    gameLabel: 'Neon Nights',
    pluginName: 'verify-neon-module-loading',
    clientRoot: 'games/NeonNights/client/FortuneForge.Games.NeonNights.Client',
    hostEntry: 'fortuneforge.client/src/app/routes/NeonNightsBundled.ts',
  })
}
