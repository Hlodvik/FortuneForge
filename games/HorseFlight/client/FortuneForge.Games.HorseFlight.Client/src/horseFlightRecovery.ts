import type { HorseFlightRecoveryMode, HorseFlightResult, HorseFlightStart } from './contracts'
import { currentRulesVersion, startHorse, stepHorse, type HorseRulesVersion, type HorseState } from './horseFlightSimulation'

export const maximumRecordedTicks = 30_000
export const maximumRecordedJumps = 1_500
export const maximumRecordedRightClicks = 20_000
export type HorseFlightRecovery = {
  version: 1; owner: string; mode: HorseFlightRecoveryMode; scope: string
  startKey: string; finishKey: string; run: HorseFlightStart | null; rulesVersion: HorseRulesVersion
  ticks: number; jumps: number[]; rights: number[]
  state: 'starting' | 'active' | 'saving' | 'saved' | 'debug'; result: HorseFlightResult | null
}
export const horseFlightRecoveryKey = (owner: string, mode: HorseFlightRecoveryMode) =>
  `fortuneforge:horse-flight:recorded:v1:${encodeURIComponent(owner)}:${mode}`

export function validRecoveryScope(scope: unknown, mode: HorseFlightRecoveryMode): scope is string {
  if (typeof scope !== 'string' || scope.length > 256) return false
  try {
    const parts: unknown = JSON.parse(scope)
    if (!Array.isArray(parts) || parts[0] !== mode || JSON.stringify(parts) !== scope) return false
    if (mode === 'free') return parts.length === 1
    return parts.length === 3 && parts.slice(1).every(value => typeof value === 'string' &&
      Number.isFinite(Date.parse(value)) && new Date(value).toISOString() === value) && Date.parse(parts[1]) < Date.parse(parts[2])
  } catch { return false }
}
export function newHorseFlightRecovery(owner: string, mode: HorseFlightRecoveryMode, scope: string,
  startKey: string, finishKey: string): HorseFlightRecovery {
  if (!owner || owner.length > 256 || !validRecoveryScope(scope, mode)) throw new Error('The flight recovery scope is unavailable.')
  return { version: 1, owner, mode, scope, startKey, finishKey, run: null, rulesVersion: currentRulesVersion,
    ticks: 0, jumps: [], rights: [], state: 'starting', result: null }
}
export function validIssuedFlight(run: HorseFlightStart): boolean {
  return typeof run?.runId === 'string' && /^[a-zA-Z0-9_-]{1,160}$/.test(run.runId) &&
    Number.isInteger(run.seed) && run.seed > 0 && run.seed <= 0xffff_ffff && run.tickMilliseconds === 20
}
export function reconstructHorseFlight(record: HorseFlightRecovery): HorseState | null {
  if (!record.run || record.state === 'debug') return null
  let state = startHorse(record.run.seed, record.rulesVersion), jump = 0, right = 0
  for (let tick = 0; tick < record.ticks; tick++) {
    if (state.phase !== 'running') throw new Error('The saved flight continues after it ended.')
    const wantsJump = record.jumps[jump] === tick, wantsRight = record.rights[right] === tick
    const next = stepHorse(state, wantsJump, wantsRight)
    if (wantsJump !== next.jumped || wantsRight !== (next.slid || next.fastFell)) throw new Error('The saved flight controls are invalid.')
    if (wantsJump) jump++
    if (wantsRight) right++
    state = next.state
  }
  return state
}
export function horseFlightReceiptMatches(record: HorseFlightRecovery, state: HorseState, result: HorseFlightResult): boolean {
  const same = (a: readonly number[], b: readonly number[]) => Array.isArray(a) && a.length === b.length && a.every((value, index) => value === b[index])
  return state.phase !== 'running' && result?.runId === record.run?.runId && result.totalTicks === record.ticks &&
    result.score === state.score && result.phase === state.phase && Number.isFinite(result.balance) && result.balance >= 0 &&
    Number.isSafeInteger(Math.round(result.balance * 100)) && Math.abs(result.balance * 100 - Math.round(result.balance * 100)) < 1e-6 &&
    same(result.jumpTicks, record.jumps) && same(result.rightClickTicks ?? [], record.rights)
}
export function readHorseFlightRecovery(owner: string, mode: HorseFlightRecoveryMode, scope: string): {
  record: HorseFlightRecovery | null; game: HorseState | null; error: string | null
} {
  try {
    const raw = localStorage.getItem(horseFlightRecoveryKey(owner, mode))
    if (!raw) return { record: null, game: null, error: null }
    if (raw.length > 300_000) throw new Error('Oversized flight record.')
    const value = JSON.parse(raw) as HorseFlightRecovery
    const canonicalTicks = (ticks: unknown, maximum: number): ticks is number[] => Array.isArray(ticks) && ticks.length <= maximum &&
      ticks.every((tick, index) => Number.isInteger(tick) && tick >= 0 && tick < value.ticks && (index === 0 || tick > ticks[index - 1]))
    if (value?.version !== 1 || value.owner !== owner || value.mode !== mode || !validRecoveryScope(value.scope, mode) ||
      !validKey(value.startKey) || !validKey(value.finishKey) || ![2, 3, 4].includes(value.rulesVersion) ||
      !Number.isInteger(value.ticks) || value.ticks < 0 || value.ticks > maximumRecordedTicks ||
      !canonicalTicks(value.jumps, maximumRecordedJumps) || !canonicalTicks(value.rights, maximumRecordedRightClicks) ||
      value.jumps.some(tick => value.rights.includes(tick)) || !['starting', 'active', 'saving', 'saved', 'debug'].includes(value.state) ||
      (value.run !== null && (!validIssuedFlight(value.run) ||
        (value.run.recoveryScope !== undefined && value.run.recoveryScope !== value.scope))) ||
      (!value.run && (value.ticks !== 0 || value.state !== 'starting')) ||
      (value.run !== null && value.state === 'starting')) throw new Error('Invalid flight record.')
    const game = reconstructHorseFlight(value)
    if (value.state === 'saved' ? !game || !value.result || !horseFlightReceiptMatches(value, game, value.result) : value.result !== null)
      throw new Error('Invalid flight receipt.')
    if (value.scope !== scope) return value.state === 'saved'
      ? { record: value, game: null, error: null }
      : { record: value, game: null,
        error: 'A previous competition window has an unresolved flight. It cannot be replayed or charged in this window.' }
    return { record: value, game, error: null }
  } catch { return { record: null, game: null, error: 'The saved flight could not be verified. Recovery requires review before another flight in this mode.' } }
}
export function writeHorseFlightRecovery(record: HorseFlightRecovery): boolean {
  try { localStorage.setItem(horseFlightRecoveryKey(record.owner, record.mode), JSON.stringify(record)); return true }
  catch { return false }
}
function validKey(value: unknown): value is string { return typeof value === 'string' && /^[a-zA-Z0-9_-]{16,128}$/.test(value) }
