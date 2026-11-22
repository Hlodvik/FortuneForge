import { FIFTH_SUN_ECLIPSE_MATH as eclipseMath } from './eclipseMath.generated'

export type GuardianId = 'jaguar' | 'eagle' | 'serpent'
export type EclipseMode = 'solar' | 'lunar'
export type SunwheelPosition = Readonly<{ reel: number; row: number }>
export type SunwheelRing = Readonly<{
  id: GuardianId
  symbolId: string
  rotationSteps: number
  path: readonly SunwheelPosition[]
  matchedPositions: readonly SunwheelPosition[]
  matches: number
  aligned: boolean
  baseMultiplier: number
  awardPoints: number
}>
export type SunwheelFeature = Readonly<{
  contractVersion: 1 | 2 | 3
  kind: 'sunstone-eclipse' | 'fifth-sun'
  mathVersion: 'sunstone-eclipse-v1' | 'fifth-sun-wheel-v1' | 'fifth-sun-totality-v1'
  dialStep: number
  rows: number
  isEclipseSpin: boolean
  heart: SunwheelPosition & Readonly<{ symbolId: string; lit: boolean }>
  rings: readonly SunwheelRing[]
  alignedRings: number
  alignmentMultiplier: number
  eclipseTriggered: boolean
  eclipseAwardPoints: number
  totalAwardPoints: number
  maxAwardMultiplier: number
  eclipseMode?: EclipseMode | null
  alignments?: readonly SunwheelAlignment[]
  totalityAwardPoints?: number
  wheelAwardScale?: number
}>

export type SunwheelAlignment = Readonly<{
  dialStep: number
  rings: readonly SunwheelRing[]
  awakenedGuardianIds: readonly GuardianId[]
  alignmentMultiplier: number
  totalAwardPoints: number
}>

const guardianIds: readonly GuardianId[] = ['jaguar', 'eagle', 'serpent']
const historicalGuardianSymbols = ['7', '6', '5'] as const
const guardianSymbols = ['JAGUAR', 'EAGLE', 'SERPENT'] as const
const guardianPatterns = [[0, 1, 2, 1, 0], [3, 2, 1, 2, 3], [1, 2, 3, 2, 1]] as const
export const SUNWHEEL_PAYOUT_TIERS = { 3: 0.5, 4: 1, 5: 2 } as const
const object = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const integer = (value: unknown, minimum: number, maximum: number): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum && value <= maximum
const amount = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value) && value >= 0

function position(value: unknown, rows: number): value is SunwheelPosition & Record<string, unknown> {
  return object(value) && integer(value.reel, 0, 4) && integer(value.row, 0, rows - 1)
}

/** Shared by the real feature decoder and explicitly labelled guide illustrations. */
export function getSunwheelPath(guardianId: GuardianId, rows: number, dialStep: number): readonly SunwheelPosition[] {
  const index = guardianIds.indexOf(guardianId)
  if (index < 0 || (rows !== 4 && rows !== 6) || !integer(dialStep, 0, rows - 1))
    throw new RangeError('Unsupported Sunwheel geometry')
  const rotation = (dialStep + index) % rows
  return guardianPatterns[index].map((row, reel) => ({ reel, row: (row + rotation) % rows }))
}

/** The host transports an opaque feature; only this game knows its contract. */
export function decodeSunwheelFeature(value: unknown): SunwheelFeature | null {
  if (!object(value)) return null
  if (value.contractVersion === 3) return decodeTotalityFeature(value)
  const historical = value.contractVersion === 1 && value.kind === 'sunstone-eclipse' && value.mathVersion === 'sunstone-eclipse-v1'
  const current = value.contractVersion === 2 && value.kind === 'fifth-sun' && value.mathVersion === 'fifth-sun-wheel-v1'
  if ((!historical && !current) || (value.rows !== 4 && value.rows !== 6) ||
      !integer(value.dialStep, 0, value.rows - 1) || typeof value.isEclipseSpin !== 'boolean' ||
      !object(value.heart) || !position(value.heart, value.rows) ||
      value.heart.reel !== 2 || value.heart.row !== Math.floor(value.rows / 2) ||
      typeof value.heart.symbolId !== 'string' || typeof value.heart.lit !== 'boolean' ||
      value.heart.lit !== (value.heart.symbolId === (historical ? 'POWER' : 'SUNSTONE')) ||
      !Array.isArray(value.rings) || value.rings.length !== 3 ||
      !integer(value.alignedRings, 0, 3) || !integer(value.alignmentMultiplier, 1, 3) ||
      typeof value.eclipseTriggered !== 'boolean' || !integer(value.eclipseAwardPoints, 0, Number.MAX_SAFE_INTEGER) ||
      !integer(value.totalAwardPoints, 0, Number.MAX_SAFE_INTEGER) || !amount(value.maxAwardMultiplier)) return null
  if (value.maxAwardMultiplier !== (value.isEclipseSpin ? 40 : 20)) return null

  for (const [index, ring] of value.rings.entries()) {
    if (!object(ring) || ring.id !== guardianIds[index] || ring.symbolId !== (historical ? historicalGuardianSymbols : guardianSymbols)[index] ||
        !integer(ring.rotationSteps, 0, value.rows - 1) || !integer(ring.matches, 0, 5) ||
        typeof ring.aligned !== 'boolean' || ring.aligned !== (ring.matches >= 3) ||
        !amount(ring.baseMultiplier) || !integer(ring.awardPoints, 0, Number.MAX_SAFE_INTEGER) ||
        (!ring.aligned && ring.awardPoints !== 0) ||
        (ring.aligned && ring.awardPoints === 0) ||
        !Array.isArray(ring.path) || ring.path.length !== 5 ||
        !Array.isArray(ring.matchedPositions) || ring.matchedPositions.length !== ring.matches) return null
    if (ring.rotationSteps !== (value.dialStep + index) % value.rows ||
        ring.baseMultiplier !== (SUNWHEEL_PAYOUT_TIERS[ring.matches as 3 | 4 | 5] ?? 0)) return null
    const ringRows = value.rows
    const path = ring.path
    const expected = getSunwheelPath(guardianIds[index], ringRows, value.dialStep)
    if (!path.every((cell, reel) => position(cell, ringRows) && cell.reel === reel &&
          cell.row === expected[reel].row) ||
        !ring.matchedPositions.every(cell => position(cell, ringRows) &&
          path.some(pathCell => object(pathCell) && pathCell.reel === cell.reel && pathCell.row === cell.row))) return null
    if (new Set(ring.matchedPositions.map(cell => `${cell.reel}:${cell.row}`)).size !== ring.matches) return null
  }
  const rings = value.rings as SunwheelRing[]
  if (rings.filter(ring => ring.aligned).length !== value.alignedRings ||
      value.alignmentMultiplier !== Math.max(1, value.alignedRings) ||
      value.eclipseTriggered !== (value.alignedRings === 3 && value.heart.lit) ||
      (!value.eclipseTriggered && value.eclipseAwardPoints !== 0) ||
      value.eclipseAwardPoints + rings.reduce((sum, ring) => sum + ring.awardPoints, 0) !== value.totalAwardPoints) return null
  return value as unknown as SunwheelFeature
}

function decodeTotalityFeature(value: Record<string, unknown>): SunwheelFeature | null {
  const mode = value.eclipseMode ?? null
  const active = mode === 'solar' || mode === 'lunar'
  const maximumAward = active ? 54 * eclipseMath.wheelAwardScale + 2 * eclipseMath.totalityAwardMultiplier : 20
  if (value.kind !== 'fifth-sun' || value.mathVersion !== eclipseMath.mathVersion ||
      (mode !== null && !active) || value.rows !== 4 || value.isEclipseSpin !== active ||
      !integer(value.dialStep, 0, 3) || !object(value.heart) || !position(value.heart, 4) ||
      value.heart.reel !== 2 || value.heart.row !== 2 || typeof value.heart.symbolId !== 'string' ||
      value.heart.lit !== (value.heart.symbolId === 'SUNSTONE') ||
      !Array.isArray(value.alignments) || value.alignments.length !== (active ? 3 : 1) ||
      !Array.isArray(value.rings) || value.rings.length !== 3 ||
      !integer(value.alignedRings, 0, 3) || !integer(value.alignmentMultiplier, 1, 3) ||
      typeof value.eclipseTriggered !== 'boolean' ||
      !integer(value.totalityAwardPoints, 0, Number.MAX_SAFE_INTEGER) ||
      value.wheelAwardScale !== (active ? eclipseMath.wheelAwardScale : 1) ||
      value.eclipseAwardPoints !== value.totalityAwardPoints ||
      !integer(value.totalAwardPoints, 0, Number.MAX_SAFE_INTEGER) ||
      !amount(value.maxAwardMultiplier) || Math.abs(value.maxAwardMultiplier - maximumAward) > 0.000000001) return null

  const awakened = new Set<GuardianId>()
  let award = 0
  for (const [step, alignment] of value.alignments.entries()) {
    if (!object(alignment) || !integer(alignment.dialStep, 0, 3) ||
        alignment.dialStep !== (value.dialStep + (mode === 'lunar' ? -step : step) + 4) % 4 ||
        !Array.isArray(alignment.rings) || alignment.rings.length !== 3 ||
        !Array.isArray(alignment.awakenedGuardianIds) ||
        !integer(alignment.totalAwardPoints, 0, Number.MAX_SAFE_INTEGER)) return null
    for (const [index, ring] of alignment.rings.entries()) {
      if (!object(ring) || ring.id !== guardianIds[index] || ring.symbolId !== guardianSymbols[index] ||
          ring.rotationSteps !== (alignment.dialStep + index) % 4 || !integer(ring.matches, 0, 5) ||
          ring.aligned !== (ring.matches >= 3) ||
          ring.baseMultiplier !== (SUNWHEEL_PAYOUT_TIERS[ring.matches as 3 | 4 | 5] ?? 0) ||
          !integer(ring.awardPoints, 0, Number.MAX_SAFE_INTEGER) || (!ring.aligned && ring.awardPoints !== 0) ||
          !Array.isArray(ring.path) || ring.path.length !== 5 ||
          !Array.isArray(ring.matchedPositions) || ring.matchedPositions.length !== ring.matches) return null
      const expected = getSunwheelPath(guardianIds[index], 4, alignment.dialStep)
      const path = ring.path
      if (!path.every((cell, reel) => position(cell, 4) && cell.reel === reel && cell.row === expected[reel].row) ||
          !ring.matchedPositions.every(cell => position(cell, 4) &&
            path.some(pathCell => object(pathCell) && pathCell.reel === cell.reel && pathCell.row === cell.row)) ||
          new Set(ring.matchedPositions.map(cell => `${cell.reel}:${cell.row}`)).size !== ring.matches) return null
      if (ring.aligned) awakened.add(guardianIds[index])
    }
    const reportedAwakenings = alignment.awakenedGuardianIds
    if (alignment.alignmentMultiplier !== Math.max(1, awakened.size) ||
        reportedAwakenings.length !== awakened.size || new Set(reportedAwakenings).size !== awakened.size ||
        !reportedAwakenings.every(id => guardianIds.includes(id as GuardianId) && awakened.has(id as GuardianId)) ||
        alignment.totalAwardPoints !== alignment.rings.reduce((sum, ring) => sum + (ring as SunwheelRing).awardPoints, 0)) return null
    award += alignment.totalAwardPoints
  }
  const last = value.alignments.at(-1) as unknown as SunwheelAlignment
  if (value.alignedRings !== awakened.size || value.alignmentMultiplier !== last.alignmentMultiplier ||
      !value.rings.every((ring, index) => sameRing(ring, last.rings[index])) ||
      value.eclipseTriggered !== (awakened.size === 3 && (active || value.heart.lit)) ||
      (!value.eclipseTriggered && value.totalityAwardPoints !== 0) ||
      value.totalAwardPoints !== award + value.totalityAwardPoints) return null
  return value as unknown as SunwheelFeature
}

function sameRing(value: unknown, expected: SunwheelRing): boolean {
  if (!object(value)) return false
  const fields: readonly (keyof SunwheelRing)[] = ['id', 'symbolId', 'rotationSteps', 'matches', 'aligned', 'baseMultiplier', 'awardPoints']
  if (!fields.every(key => value[key] === expected[key])) return false
  return (['path', 'matchedPositions'] as const).every(key => {
    const positions = value[key]
    return Array.isArray(positions) && positions.length === expected[key].length && positions.every((cell, index) =>
      object(cell) && cell.reel === expected[key][index].reel && cell.row === expected[key][index].row)
  })
}

export const SUNWHEEL_GUARDIANS = [
  { id: 'jaguar', title: 'Jaguar', symbol: 'JAGUAR', color: '#e9ad53', description: 'The golden guardian of earth' },
  { id: 'eagle', title: 'Eagle', symbol: 'EAGLE', color: '#ead5a0', description: 'The guardian of sky' },
  { id: 'serpent', title: 'Serpent', symbol: 'SERPENT', color: '#b1c38b', description: 'The feathered guardian of flame' },
] as const
