import type { Plugin } from 'vite'
import { standaloneGameIsolation } from './standaloneGameIsolation.js'

export function goldIsolation(root: string): Plugin {
  return standaloneGameIsolation({
    root,
    gameLabel: 'Gold Foundry',
    pluginName: 'verify-gold-foundry-module-loading',
    clientRoot: 'games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client',
    hostEntry: 'fortuneforge.client/src/app/routes/GoldFoundryHost.tsx',
  })
}
