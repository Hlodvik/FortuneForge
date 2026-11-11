import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import type { Plugin } from 'vite'
import { hostedSlotIsolation } from './hostedSlotIsolation.js'
import { standaloneGameIsolation } from './standaloneGameIsolation.js'

type SlotPackageEntry = Readonly<{
  folder: string
  routeId: string
  title?: string
  clientPackage?: string
  clientKind?: 'embedded-package'
  clientRoot?: string
  hostEntry?: string
}>

/** Build-time static-graph checks for package-backed slots using the generic host. */
export function slotPackageIsolation(root: string): Plugin[] {
  const inventory = JSON.parse(readFileSync(
    resolve(root, 'games/catalog/slot-packages.json'),
    'utf8',
  )) as { games?: SlotPackageEntry[] }
  const dedicatedHosts = new Set(['neon-nights', 'dragon-hoard', 'gold-foundry'])

  return [
    ...(inventory.games ?? [])
      .filter(({ routeId, clientPackage, clientKind }) => clientPackage !== undefined && clientKind !== 'embedded-package' && !dedicatedHosts.has(routeId))
      .map(({ folder, routeId }) => standaloneGameIsolation({
        root,
        gameLabel: folder,
        pluginName: `verify-${routeId}-module-loading`,
        clientRoot: `games/${folder}/client/FortuneForge.Games.${folder}.Client`,
        hostEntry: `fortuneforge.client/src/games/slots/manifestLoaders/${routeId}.ts`,
      })),
    ...(inventory.games ?? [])
      .filter((game) => game.clientKind === 'embedded-package')
      .map((game) => hostedSlotIsolation({
        root,
        gameLabel: game.title ?? game.folder,
        pluginName: `verify-${game.routeId}-host-loading`,
        hostEntry: game.hostEntry ?? '',
      })),
  ]
}
