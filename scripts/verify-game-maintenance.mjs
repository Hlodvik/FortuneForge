import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'

export function readGameMaintenance(root) {
  const path = resolve(root, 'config/game-maintenance.json')
  if (!existsSync(path)) return new Set()
  const value = JSON.parse(readFileSync(path, 'utf8'))
  if (value.contractVersion !== 1 || !Array.isArray(value.runtimeGameIds) ||
    !value.runtimeGameIds.every(id => typeof id === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(id)) ||
    new Set(value.runtimeGameIds).size !== value.runtimeGameIds.length) throw Error('Invalid game maintenance policy.')
  return new Set(value.runtimeGameIds)
}

/** Maintenance is a disabled deployment state, never a completed game migration. */
export function verifyGameMaintenanceBoundary(root, runtimeGameId) {
  const errors = []
  const read = path => existsSync(resolve(root, path)) ? readFileSync(resolve(root, path), 'utf8') : ''
  const checks = [
    ['route does not block maintenance games', read('fortuneforge.client/src/app/routes/SlotGameRoute.tsx').includes('if (isGameUnderMaintenance(props.definition)) return <GameMaintenanceState')],
    ['catalogue must display maintenance and disable play', /maintenance \? null/.test(read('fortuneforge.client/src/pages/slots/SlotsLibraryPage.tsx')) && read('fortuneforge.client/src/pages/slots/SlotsLibraryPage.tsx').includes('Under Maintenance')],
    ['backend does not embed the shared availability policy', read('FortuneForge.Server/FortuneForge.Server.csproj').includes('config\\game-maintenance.json')],
    ['package admission does not apply maintenance after configuration overrides', read('FortuneForge.Server/GameWorkers/Packages/GamePackageRuntime.cs').includes('GameMaintenance.ApplyAdmissionSettings(gameId, result)')],
    ['legacy admission has no maintenance guard', read('FortuneForge.Server/Controllers/SlotsController.cs').includes('GameMaintenance.Contains(request.GameId) && preparedWorker is null')],
  ]
  for (const [message, passed] of checks) if (!passed) errors.push(message)
  const configuration = JSON.parse(read('config/backend-host.json') || '{}')
  if (!Object.keys(configuration.packages?.[runtimeGameId] ?? {}).some(key => key.endsWith(':AllowNewPaidRounds')))
    errors.push('maintenance game has no package-owned new-round admission switch')
  return errors
}
