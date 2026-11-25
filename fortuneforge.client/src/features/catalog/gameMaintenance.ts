import document from '../../../../config/game-maintenance.json?raw'

const configuration = JSON.parse(document) as { contractVersion: number; runtimeGameIds: string[] }
if (configuration.contractVersion !== 1 || !Array.isArray(configuration.runtimeGameIds) ||
  !configuration.runtimeGameIds.every(id => typeof id === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(id))) throw Error('Invalid game maintenance configuration.')
const maintenance = new Set(configuration.runtimeGameIds)
export const isGameUnderMaintenance = (game: { serverGameIds: readonly string[] }) => game.serverGameIds.some(id => maintenance.has(id))
