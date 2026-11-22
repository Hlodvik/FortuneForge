export type GuardianId = 'jaguar' | 'eagle' | 'serpent'
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
  contractVersion: 1
  kind: 'sunstone-eclipse'
  mathVersion: 'sunstone-eclipse-v1'
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
}>

const guardianIds: readonly GuardianId[] = ['jaguar', 'eagle', 'serpent']
const guardianSymbols = ['7', '6', '5'] as const
const guardianPatterns = [[0, 1, 2, 1, 0], [3, 2, 1, 2, 3], [1, 2, 3, 2, 1]] as const
const object = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const integer = (value: unknown, minimum: number, maximum: number): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum && value <= maximum
const amount = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value) && value >= 0

function position(value: unknown, rows: number): value is SunwheelPosition & Record<string, unknown> {
  return object(value) && integer(value.reel, 0, 4) && integer(value.row, 0, rows - 1)
}

/** The host transports an opaque feature; only this game knows its contract. */
export function decodeSunwheelFeature(value: unknown): SunwheelFeature | null {
  if (!object(value) || value.contractVersion !== 1 || value.kind !== 'sunstone-eclipse' ||
      value.mathVersion !== 'sunstone-eclipse-v1' || (value.rows !== 4 && value.rows !== 6) ||
      !integer(value.dialStep, 0, value.rows - 1) || typeof value.isEclipseSpin !== 'boolean' ||
      !object(value.heart) || !position(value.heart, value.rows) ||
      value.heart.reel !== 2 || value.heart.row !== Math.floor(value.rows / 2) ||
      typeof value.heart.symbolId !== 'string' || typeof value.heart.lit !== 'boolean' ||
      value.heart.lit !== (value.heart.symbolId === 'POWER') ||
      !Array.isArray(value.rings) || value.rings.length !== 3 ||
      !integer(value.alignedRings, 0, 3) || !integer(value.alignmentMultiplier, 1, 3) ||
      typeof value.eclipseTriggered !== 'boolean' || !integer(value.eclipseAwardPoints, 0, Number.MAX_SAFE_INTEGER) ||
      !integer(value.totalAwardPoints, 0, Number.MAX_SAFE_INTEGER) || !amount(value.maxAwardMultiplier)) return null
  if (value.maxAwardMultiplier !== (value.isEclipseSpin ? 40 : 20)) return null

  for (const [index, ring] of value.rings.entries()) {
    if (!object(ring) || ring.id !== guardianIds[index] || ring.symbolId !== guardianSymbols[index] ||
        !integer(ring.rotationSteps, 0, value.rows - 1) || !integer(ring.matches, 0, 5) ||
        typeof ring.aligned !== 'boolean' || ring.aligned !== (ring.matches >= 3) ||
        !amount(ring.baseMultiplier) || !integer(ring.awardPoints, 0, Number.MAX_SAFE_INTEGER) ||
        (!ring.aligned && ring.awardPoints !== 0) ||
        (ring.aligned && ring.awardPoints === 0) ||
        !Array.isArray(ring.path) || ring.path.length !== 5 ||
        !Array.isArray(ring.matchedPositions) || ring.matchedPositions.length !== ring.matches) return null
    if (ring.rotationSteps !== (value.dialStep + index) % value.rows ||
        ring.baseMultiplier !== (ring.matches === 5 ? 2 : ring.matches === 4 ? 1 : ring.matches === 3 ? 0.5 : 0)) return null
    const ringRows = value.rows
    const path = ring.path
    const rotationSteps = ring.rotationSteps
    if (!path.every((cell, reel) => position(cell, ringRows) && cell.reel === reel &&
          cell.row === (guardianPatterns[index][reel] + rotationSteps) % ringRows) ||
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

export const SUNWHEEL_GUARDIANS = [
  { id: 'jaguar', title: 'Jaguar', symbol: '7', color: '#e9ad53', description: 'The golden guardian of earth' },
  { id: 'eagle', title: 'Eagle', symbol: '6', color: '#5ce2c8', description: 'The turquoise guardian of sky' },
  { id: 'serpent', title: 'Serpent', symbol: '5', color: '#e27e59', description: 'The feathered guardian of flame' },
] as const
