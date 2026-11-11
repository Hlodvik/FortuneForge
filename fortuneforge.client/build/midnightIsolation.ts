import type { Plugin } from 'vite'
import { standaloneGameIsolation } from './standaloneGameIsolation.js'

export function midnightIsolation(root: string): Plugin {
  return standaloneGameIsolation({
    root,
    gameLabel: 'Midnight Vault',
    pluginName: 'verify-midnight-module-loading',
    clientRoot: 'games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client',
    hostEntry: 'fortuneforge.client/src/app/routes/MidnightVaultHost.tsx',
  })
}
