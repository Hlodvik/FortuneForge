import type { CSSProperties } from 'react'
import throwOneUrl from './assets/dice/dice-throw-1.png'
import throwTwoUrl from './assets/dice/dice-throw-2.png'
import throwThreeUrl from './assets/dice/dice-throw-3.png'
import throwFourUrl from './assets/dice/dice-throw-4.png'
import './diceThrow.css'

export type DieFace = 1 | 2 | 3 | 4 | 5 | 6

export type DiceLanding = Readonly<{
  /** Horizontal landing point, as a percentage of the dice stage. */
  x: number
  /** Vertical landing point, as a percentage of the dice stage. */
  y: number
  /** Retained for API compatibility; sprite frames contain their own rotation. */
  rotation?: number
}>

export type DiceThrowProps = Readonly<{
  values: readonly (number | null)[]
  /** Change this when a new result arrives to replay the sprite sheet. */
  rollKey?: string | number
  /** Marks the request phase while the authoritative result is loading. */
  rolling?: boolean
  landingPositions?: readonly DiceLanding[]
  className?: string
  label?: string
}>

type DiceStyle = CSSProperties & Record<`--ff-die-${string}`, string>

const trajectorySprites = [throwOneUrl, throwTwoUrl, throwThreeUrl, throwFourUrl] as const
const animationSpeed = 1.8
const trajectoryDurations = [820, 960, 880, 1080] as const
const trajectoryDelays = [0, 70, 130, 35] as const
const trajectoryLandings = [
  { x: 35.94, y: 40.15 },
  { x: 64.06, y: 43.94 },
  { x: 37.76, y: 65.15 },
  { x: 63.02, y: 66.67 },
] as const

export function DiceThrow({
  values,
  rollKey = 'result',
  rolling = false,
  landingPositions,
  className = '',
  label,
}: DiceThrowProps) {
  const rollSeed = seedFromRollKey(rollKey)
  const defaultLandings = landingLayout(values.length).map((landing, index) => varyLanding(landing, rollSeed, index))
  const landings = landingPositions ?? defaultLandings
  const resultLabel = label ?? diceLabel(values)

  return (
    <div
      className={`ff-dice-throw ${rolling ? 'is-rolling' : 'has-result'} ${className}`.trim()}
      role="img"
      aria-label={resultLabel}
      data-dice-count={values.length}
    >
      {values.map((rawValue, index) => {
        const value = normalizeFace(rawValue)
        const landing = landings[index] ?? defaultLandings[index]
        const trajectory = positiveModulo(rollSeed + index, 4)
        const lap = Math.floor(index / 4)
        const duration = Math.round((trajectoryDurations[trajectory] + lap * 120) / animationSpeed)
        const delay = Math.round((trajectoryDelays[trajectory] + lap * 55) / animationSpeed)
        const anchor = trajectoryLandings[trajectory]
        const landingX = clamp(landing?.x ?? anchor.x, 7, 93)
        const landingY = clamp(landing?.y ?? anchor.y, 14, 86)
        const style: DiceStyle = {
          '--ff-die-duration': `${duration}ms`,
          '--ff-die-delay': `${delay}ms`,
          '--ff-die-landing-x': `${landingX}%`,
          '--ff-die-landing-y': `${landingY}%`,
          '--ff-die-shift-x': `${landingX - anchor.x}%`,
          '--ff-die-shift-y': `${landingY - anchor.y}%`,
          '--ff-die-face-position': `${((value ?? 1) - 1) * 20}%`,
          '--ff-die-sprite-image': `url("${trajectorySprites[trajectory]}")`,
        }

        return (
          <span
            className={`ff-die-sprite-frame ff-die-sprite-frame--${trajectory} ${value === null ? 'is-idle' : ''}`}
            style={style}
            key={`${rollKey}-${index}-${value ?? 'idle'}`}
            data-trajectory={trajectory}
            data-face={value ?? undefined}
          >
            <span className="ff-die-throw-sprite" aria-hidden="true" />
          </span>
        )
      })}
    </div>
  )
}

export function landingLayout(count: number): readonly DiceLanding[] {
  if (count <= 1) return [{ x: 50, y: 52 }]
  if (count === 2) return [{ x: 31, y: 39 }, { x: 69, y: 61 }]
  if (count === 3) return [{ x: 21, y: 36 }, { x: 50, y: 68 }, { x: 78, y: 32 }]
  if (count === 4) return [{ x: 18, y: 32 }, { x: 41, y: 69 }, { x: 66, y: 38 }, { x: 84, y: 70 }]
  if (count === 5) return [{ x: 13, y: 32 }, { x: 31, y: 70 }, { x: 50, y: 34 }, { x: 69, y: 69 }, { x: 87, y: 31 }]
  return Array.from({ length: count }, (_, index) => {
    const column = index % 3
    const row = Math.floor(index / 3)
    return { x: 18 + column * 32 + (row % 2) * 5, y: 30 + row * 39 }
  })
}

function normalizeFace(value: number | null): DieFace | null {
  return Number.isInteger(value) && value !== null && value >= 1 && value <= 6 ? value as DieFace : null
}

function seedFromRollKey(rollKey: string | number): number {
  if (typeof rollKey === 'number' && Number.isFinite(rollKey)) return Math.trunc(rollKey)
  let hash = 0
  for (const character of String(rollKey)) hash = Math.imul(hash, 31) + character.charCodeAt(0) | 0
  return hash
}

function varyLanding(landing: DiceLanding, seed: number, index: number): DiceLanding {
  if (seed === 0) return landing
  return {
    x: clamp(landing.x + seededUnit(seed, index * 2) * 12 - 6, 9, 91),
    y: clamp(landing.y + seededUnit(seed, index * 2 + 1) * 10 - 5, 17, 83),
  }
}

function seededUnit(seed: number, salt: number): number {
  let value = Math.imul(seed ^ 0x45d9f3b, 0x45d9f3b) + Math.imul(salt + 1, 0x27d4eb2d)
  value = Math.imul(value ^ value >>> 16, 0x45d9f3b)
  return ((value ^ value >>> 16) >>> 0) / 0xffffffff
}

function positiveModulo(value: number, divisor: number): number {
  return ((value % divisor) + divisor) % divisor
}

function diceLabel(values: readonly (number | null)[]): string {
  const faces = values.map(normalizeFace)
  return faces.every((value) => value !== null) ? `Dice showing ${faces.join(', ')}` : `${values.length} dice ready to roll`
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value))
}
