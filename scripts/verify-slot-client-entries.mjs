import { existsSync, readFileSync, realpathSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { verifyPaidFrameBoundary } from './verify-paid-frame-boundary.mjs'
import { readGameMaintenance, verifyGameMaintenanceBoundary } from './verify-game-maintenance.mjs'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')

export function inspectSlotClientEntry({ id, frontend, entrySource, registered, frameBoundaryVerified = false }) {
  const errors = []
  if (frontend.manifestExport !== undefined || /\brenderSlot\b/.test(entrySource))
    errors.push(`${id}: still publishes a manifest for the deleted generic slot renderer.`)
  if (frontend.kind === 'frame' && !frameBoundaryVerified)
    errors.push(`${id}: embedded presentation needs a verified game-owned API boundary; frame assets alone are insufficient.`)
  else if (frontend.kind !== 'frame' && !/\bexport\s+(?:default\b|\{[^}]*\bas\s+default\b)/s.test(entrySource))
    errors.push(`${id}: browser release has no default game component entry.`)
  if (!registered) errors.push(`${id}: no game-owned client is mounted by the host route.`)
  return errors
}

/** Entry integration check, not a certification of rules, security or deployment. */
export function verifySlotClientEntries(root = repositoryRoot) {
  const inventory = JSON.parse(readFileSync(resolve(root, 'games/catalog/slot-packages.json'), 'utf8'))
  const routeSource = readFileSync(resolve(root, 'fortuneforge.client/src/app/routes/SlotGameRoute.tsx'), 'utf8')
  const registered = new Set([...routeSource.matchAll(/^\s*'([^']+)'\s*:\s*lazy\(/gm)].map(match => match[1]))
  const errors = []
  const clients = []
  const maintenance = readGameMaintenance(root)
  for (const id of maintenance) if (!inventory.games.some(game => game.serverGameId === id)) errors.push(`Maintenance policy references unknown slot runtime ${id}.`)
  for (const game of inventory.games) {
    const id = game.routeId
    if (maintenance.has(game.serverGameId)) {
      const issues = verifyGameMaintenanceBoundary(root, game.serverGameId)
      errors.push(...issues.map(issue => `${id}: ${issue}`))
      clients.push({ id, entry: null, status: issues.length ? 'incomplete-maintenance' : 'maintenance' })
      continue
    }
    const releaseFile = resolve(root, 'games', game.folder, 'game-release.json')
    if (!existsSync(releaseFile)) { errors.push(`${id}: missing release configuration.`); continue }
    const { frontend } = JSON.parse(readFileSync(releaseFile, 'utf8'))
    if (!frontend?.directory || !frontend.entry) { errors.push(`${id}: missing browser release entry.`); continue }
    const clientRoot = resolve(dirname(releaseFile), frontend.directory)
    const entry = resolve(clientRoot, frontend.entry)
    const entryPath = relative(clientRoot, entry)
    if (entryPath.startsWith('..') || !existsSync(entry)) { errors.push(`${id}: missing or invalid browser entry ${frontend.entry}.`); continue }
    const frameMounted = frontend.kind === 'frame' && typeof game.hostEntry === 'string' && existsSync(resolve(root, game.hostEntry))
    const frameBoundary = frontend.kind === 'frame' ? verifyPaidFrameBoundary({ root, clientRoot, entry, apiEntry: frontend.apiEntry, hostEntry: game.hostEntry }) : null
    const issues = [...(frameBoundary?.errors.map(issue => `${id}: ${issue}`) ?? []), ...inspectSlotClientEntry({ id, frontend,
      entrySource: readFileSync(entry, 'utf8'), registered: registered.has(id) || frameMounted, frameBoundaryVerified: frameBoundary?.errors.length === 0 })]
    errors.push(...issues)
    clients.push({ id, entry: relative(root, entry).replaceAll('\\', '/'), status: issues.length ? 'incomplete' : frontend.kind === 'frame' ? 'paid-frame-entry' : 'native-entry' })
  }
  for (const retired of ['pages/slots/SlotsPage.tsx', 'features/slots/SlotsPageView.tsx', 'features/slots/useSlotsPageController.ts', 'features/slots/services/slotsApi.ts']) {
    if (existsSync(resolve(root, 'fortuneforge.client/src', retired))) errors.push(`Deleted generic runtime was restored: ${retired}`)
  }
  return { games: inventory.games.length, clients, errors }
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  const report = verifySlotClientEntries()
  console.log(`Slot client entries: ${report.clients.filter(client => client.status === 'native-entry').length} native, ${report.clients.filter(client => client.status === 'paid-frame-entry').length} paid frame, ${report.clients.filter(client => client.status === 'maintenance').length} disabled for maintenance; ${report.games} inventoried. Entry/boundary evidence is not full gameplay certification.`)
  if (report.errors.length) {
    report.errors.forEach(error => console.error(error))
    process.exitCode = 1
  }
}
