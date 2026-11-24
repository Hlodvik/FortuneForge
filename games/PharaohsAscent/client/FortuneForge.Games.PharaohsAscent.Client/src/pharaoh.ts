import { PHARAOHS_ASCENT_VISUALS as art } from './visuals'
import { PHARAOH_ENGINE_GUIDE } from './engineGuide'

const guardianTarget = PHARAOH_ENGINE_GUIDE.collectionTarget

export const GUARDIANS = [
  { id: 'sync', name: 'Horus', title: 'The mirrored horizon', effect: 'One reel mirrors another during your Tomb Spins.', image: art.horus, color: '#eab18e' },
  { id: 'rows', name: 'Thoth', title: 'The hidden chamber', effect: 'Two extra rows open during your Tomb Spins.', image: art.thoth, color: '#9bcbea' },
  { id: 'paw', name: 'Anubis', title: 'The golden passage', effect: 'Two extra wilds appear during your Tomb Spins.', image: art.anubis, color: '#f1ca7e' },
  { id: 'rand', name: 'Wadjet', title: 'The serpent’s blessing', effect: 'An entire reel becomes wild during your Tomb Spins.', image: art.wadjet, color: '#8fddbf' },
] as const
export type GuardianId = typeof GUARDIANS[number]['id']
export type PharaohState = {
  kind: 'pharaoh-ascent'
  collections: { sealId: GuardianId; count: number; requiredCount: number; averageWagerPoints: number }[]
  freeSpinsRemaining: number
  freeSpinFeatureMode: string | null
  freeSpinWagerPoints: number | null
  energyBalance: number
}
export type ScarabTrail = { positions: { reel: number; row: number }[]; symbolCount: number; multiplier: number; amountPoints: number }
export type PharaohFeature = { kind: 'pharaoh-ascent'; scarabTrails: ScarabTrail[]; scarabTrailPoints: number }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const nonnegative = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0

export function decodePharaohState(value: unknown): PharaohState | null {
  if (!record(value) || value.kind !== 'pharaoh-ascent' || !Array.isArray(value.collections) ||
    !nonnegative(value.freeSpinsRemaining) || !nonnegative(value.energyBalance) ||
    !(value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string') ||
    !(value.freeSpinWagerPoints === null || nonnegative(value.freeSpinWagerPoints))) return null
  const ids = new Set<string>()
  for (const entry of value.collections) {
    if (!record(entry) || !GUARDIANS.some(guardian => guardian.id === entry.sealId) ||
      !nonnegative(entry.count) || entry.requiredCount !== guardianTarget ||
      !nonnegative(entry.averageWagerPoints) || ids.has(entry.sealId as string)) return null
    ids.add(entry.sealId as string)
  }
  return ids.size === GUARDIANS.length ? value as PharaohState : null
}

export function decodePharaohFeature(value: unknown): PharaohFeature | null {
  if (!record(value) || value.kind !== 'pharaoh-ascent' || !Array.isArray(value.scarabTrails) ||
    !nonnegative(value.scarabTrailPoints)) return null
  let total = 0
  const occupied = new Map<string, number>()
  for (const [groupIndex, trail] of value.scarabTrails.entries()) {
    if (!record(trail) || !Array.isArray(trail.positions) || !nonnegative(trail.symbolCount) ||
      trail.symbolCount < 3 || trail.positions.length !== trail.symbolCount ||
      trail.multiplier !== (trail.symbolCount === 3 ? 3 : trail.symbolCount === 4 ? 5 : 8) ||
      !nonnegative(trail.amountPoints)) return null
    const group = new Set<string>()
    for (const position of trail.positions) {
      if (!record(position) || !nonnegative(position.reel) || position.reel >= 5 || !nonnegative(position.row) || position.row >= 6) return null
      const key = position.reel + ':' + position.row
      if (occupied.has(key)) return null
      occupied.set(key, groupIndex)
      group.add(key)
    }
    const connected = new Set<string>()
    const pending = [trail.positions[0]]
    while (pending.length) {
      const position = pending.pop()!
      const key = position.reel + ':' + position.row
      if (connected.has(key)) continue
      connected.add(key)
      for (const [reel, row] of [[position.reel - 1, position.row], [position.reel + 1, position.row],
        [position.reel, position.row - 1], [position.reel, position.row + 1]]) {
        const neighbor = reel + ':' + row
        if (group.has(neighbor) && !connected.has(neighbor)) pending.push({ reel, row })
      }
    }
    if (connected.size !== group.size) return null
    total += trail.amountPoints
    if (!Number.isSafeInteger(total)) return null
  }
  for (const [key, groupIndex] of occupied) {
    const [reel, row] = key.split(':').map(Number)
    for (const neighbor of [`${reel + 1}:${row}`, `${reel}:${row + 1}`]) {
      if (occupied.has(neighbor) && occupied.get(neighbor) !== groupIndex) return null
    }
  }
  return total === value.scarabTrailPoints ? value as PharaohFeature : null
}
