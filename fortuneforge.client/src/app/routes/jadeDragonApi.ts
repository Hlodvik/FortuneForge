import { fetchWithAccountSession, getCurrentAccount } from '../../features/account/services/accountsApi'

const GAME_ID = 'empty-slots-v1'
const CASCADE_ENGINE_VERSION = 'jade-dragon-cascade-v2'
const WAGER_POINT_VALUE_IN_CENTS = 25
const REEL_COUNT = 5
const CASCADE_ROWS = 4
const MAX_CASCADE_STEPS = 30
const MAX_WIN_MULTIPLIER = 5000
const CASCADE_SYMBOLS = new Set(['dragon', 'phoenix', 'lotus', 'jade', 'coin', 'bamboo', 'knot', 'pearl'])
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const natural = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) >= 0
const nonnegative = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0
const legacySymbol = (value: unknown): value is string => typeof value === 'string' && /^S(0[1-9]|1[0-9]|2[0-3])$/.test(value)
const cascadeSymbol = (value: unknown): value is string => typeof value === 'string' && CASCADE_SYMBOLS.has(value)

function validGrid<T>(value: unknown, rows: number, validCell: (cell: unknown) => cell is T): value is T[][] {
  return Array.isArray(value) && value.length === REEL_COUNT &&
    value.every(reel => Array.isArray(reel) && reel.length === rows && reel.every(validCell))
}

type Position = { reel: number; row: number }

function validPosition(value: unknown, rows: number): value is Position {
  return isRecord(value) && natural(value.reel) && value.reel < REEL_COUNT && natural(value.row) && value.row < rows
}

const cellKey = ({ reel, row }: Position) => reel * CASCADE_ROWS + row

function sameGrid(first: string[][], second: string[][]) {
  return first.every((reel, column) => reel.every((id, row) => id === second[column][row]))
}

function awardedCells(value: unknown, reels: string[][]) {
  if (!Array.isArray(value) || value.length === 0 || value.length > REEL_COUNT * CASCADE_ROWS) return null
  const cells = new Set<number>()
  for (const award of value) {
    if (!isRecord(award) || !cascadeSymbol(award.symbol) || award.symbol === 'pearl' ||
      !natural(award.matchLength) || award.matchLength < 3 || award.matchLength > REEL_COUNT ||
      !natural(award.ways) || award.ways === 0 || !nonnegative(award.unroundedPoints) ||
      !Array.isArray(award.positions) || award.positions.length < award.matchLength ||
      award.positions.length > REEL_COUNT * CASCADE_ROWS) return null
    for (const position of award.positions) {
      if (!validPosition(position, CASCADE_ROWS) || reels[position.reel][position.row] !== award.symbol) return null
      const key = cellKey(position)
      if (cells.has(key)) return null
      cells.add(key)
    }
  }
  return cells
}

function validRefill(previous: string[][], next: string[][], drops: number[][], cleared: Set<number>) {
  return previous.every((reel, column) => {
    const survivors = reel.map((_, row) => row).filter(row => !cleared.has(cellKey({ reel: column, row })))
    const refillCount = CASCADE_ROWS - survivors.length
    return drops[column].every((drop, row) => {
      if (row < refillCount) return drop === refillCount
      const sourceRow = survivors[row - refillCount]
      return drop === row - sourceRow && next[column][row] === reel[sourceRow]
    })
  })
}

function validCascade(value: unknown, initial: string[][], payoutPoints: number, freeSpinsAwarded: number, payoutLimit: number) {
  if (!isRecord(value) || !validGrid(value.initialReels, CASCADE_ROWS, cascadeSymbol) || !sameGrid(value.initialReels, initial) ||
    !Array.isArray(value.steps) || value.steps.length > MAX_CASCADE_STEPS ||
    payoutPoints > payoutLimit || value.totalPoints !== payoutPoints || value.freeSpinsAwarded !== freeSpinsAwarded) return false

  let reels = initial
  let points = 0
  for (const [index, step] of value.steps.entries()) {
    if (!isRecord(step) || !validGrid(step.reels, CASCADE_ROWS, cascadeSymbol) ||
      !validGrid(step.dropRows, CASCADE_ROWS, natural) || !natural(step.amountPoints) ||
      !natural(step.multiplier) || step.multiplier === 0) return false

    const awarded = awardedCells(step.awards, reels)
    if (!awarded || !Array.isArray(step.cleared)) return false
    points += step.amountPoints
    if (points > payoutPoints) return false

    const lastAllowedStep = index + 1 === MAX_CASCADE_STEPS
    if (step.cleared.length === 0) {
      // The final cap or step-limit award retains its winning board for display.
      if (index !== value.steps.length - 1 || (points !== payoutLimit && !lastAllowedStep) ||
        !sameGrid(reels, step.reels) || !step.dropRows.every(reel => reel.every(drop => drop === 0))) return false
      continue
    }

    if (lastAllowedStep || step.cleared.length !== awarded.size) return false
    const cleared = new Set<number>()
    for (const position of step.cleared) {
      if (!validPosition(position, CASCADE_ROWS)) return false
      const key = cellKey(position)
      if (!awarded.has(key) || cleared.has(key)) return false
      cleared.add(key)
    }
    if (!validRefill(reels, step.reels, step.dropRows, cleared)) return false
    reels = step.reels
  }
  return points === payoutPoints
}

function validOutcomeGrid(
  { reels, jadeDragonCascade, freeSpinsAwarded, wagerPoints, payoutPointValueInCents }: Record<string, unknown>,
  payoutPoints: number,
) {
  if (jadeDragonCascade == null) {
    const rows = Array.isArray(reels) && Array.isArray(reels[0]) ? reels[0].length : 0
    return payoutPointValueInCents === undefined && (rows === 4 || rows === 6) && validGrid(reels, rows, legacySymbol)
  }
  if (payoutPointValueInCents !== 1 || !natural(wagerPoints) || wagerPoints === 0 || !natural(freeSpinsAwarded)) return false
  const payoutLimit = wagerPoints * WAGER_POINT_VALUE_IN_CENTS * MAX_WIN_MULTIPLIER
  return natural(payoutLimit) && validGrid(reels, CASCADE_ROWS, cascadeSymbol) &&
    validCascade(jadeDragonCascade, reels, payoutPoints, freeSpinsAwarded, payoutLimit)
}

export function validateJadeSpinRequest(value: unknown) {
  if (!isRecord(value) || !Number.isSafeInteger(value.wagerPoints) || Number(value.wagerPoints) < 2 ||
    Number(value.wagerPoints) > (value.useFreeSpin === true ? 2000 : 400) || Number(value.wagerPoints) % 2 !== 0 ||
    typeof value.useFreeSpin !== 'boolean' || typeof value.useSpecialBoost !== 'boolean' ||
    (value.useFreeSpin && value.useSpecialBoost) ||
    (value.jadeDragonEngineVersion !== undefined && value.jadeDragonEngineVersion !== CASCADE_ENGINE_VERSION)) {
    throw new Error('Invalid Jade Dragon spin request.')
  }
  return {
    gameId: GAME_ID, wagerPoints: Number(value.wagerPoints), useFreeSpin: value.useFreeSpin, useSpecialBoost: value.useSpecialBoost,
    ...(value.jadeDragonEngineVersion === CASCADE_ENGINE_VERSION ? { jadeDragonEngineVersion: CASCADE_ENGINE_VERSION } : {}),
  }
}

export function validateJadeOutcome(value: unknown): Record<string, unknown> & { reels: string[][]; slotsCreditsBalance: number } {
  if (!isRecord(value) || value.gameId !== GAME_ID || typeof value.spinId !== 'string' ||
    typeof value.slotsCreditsBalance !== 'number' || !Number.isFinite(value.slotsCreditsBalance) || value.slotsCreditsBalance < 0 ||
    value.pointValueInCents !== WAGER_POINT_VALUE_IN_CENTS || !isRecord(value.payout) || !natural(value.payout.totalPoints) ||
    !validOutcomeGrid(value, value.payout.totalPoints) ||
    (value.freeSpinsAwarded !== undefined && !natural(value.freeSpinsAwarded)) ||
    !natural(value.freeSpinsRemaining) || !natural(value.specialPointsBalance) || !Array.isArray(value.sealCollections) ||
    !(value.freeSpinWagerPoints === null || natural(value.freeSpinWagerPoints)) ||
    !(value.freeSpinFeatureMode === null || value.freeSpinFeatureMode === 'copy-expand-wild') ||
    !Array.isArray(value.payout.paylines) || !value.payout.paylines.every(line => isRecord(line) &&
      natural(line.amountPoints) && Array.isArray(line.matches) && line.matches.every(paid => isRecord(paid) &&
        isRecord(paid.match) && Array.isArray(paid.match.positions) && paid.match.positions.every(position =>
          validPosition(position, (value.reels as string[][])[0].length))))
  ) throw new Error('The server returned an invalid Jade Dragon result. Reload to recover the settled balance.')
  return value as Record<string, unknown> & { reels: string[][]; slotsCreditsBalance: number }
}

async function json(response: Response) {
  const value: unknown = await response.json()
  if (!response.ok) throw new Error(isRecord(value) && typeof value.error === 'string' ? value.error : `Game service returned HTTP ${response.status}.`)
  return value
}

export async function loadJadeSession() {
  const [account, state, rules] = await Promise.all([
    getCurrentAccount(),
    fetchWithAccountSession(`/api/slots/state?gameId=${GAME_ID}`, { method: 'GET' }).then(json),
    fetch('/api/slots/jade-dragon/presentation', { cache: 'no-store' }).then(json),
  ])
  if (!isRecord(state) || !natural(state.freeSpinsRemaining) ||
    !(state.freeSpinWagerPoints === null || natural(state.freeSpinWagerPoints)) ||
    !natural(state.specialPointsBalance) || !Array.isArray(state.sealCollections) ||
    !isRecord(rules) || rules.gameId !== GAME_ID || !Array.isArray(rules.symbols) ||
    !Number.isFinite(account.balances.slotsCredits) || account.balances.slotsCredits < 0) throw new Error('Game state could not be loaded. Reload to retry.')
  return { state, rules, balanceCents: Math.round(account.balances.slotsCredits * 100) }
}

export async function spinJade(payload: unknown) {
  const request = validateJadeSpinRequest(payload)
  // Send once. Never retry a financial POST after a timeout or ambiguous transport error.
  const result = validateJadeOutcome(await json(await fetchWithAccountSession('/api/slots/spins', {
    method: 'POST', body: JSON.stringify(request),
  })))
  if (result.wagerPoints !== request.wagerPoints || result.isFreeSpin !== request.useFreeSpin) throw new Error('The settled response does not match the requested round. Reload to recover.')
  return { ...result, balanceCents: Math.round(result.slotsCreditsBalance * 100) }
}
