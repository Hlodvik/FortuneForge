import { expect, it } from 'vitest'
import { isNeonSpinResult } from '../src/api'
import { NEON_DISCO_SYMBOL_SET_ID, NEON_GAME_ID } from '../src/types'

const board = () => [
  ['2', '2', '4', '5'], ['2', '2', '5', '7'],
  ['4', '5', '7', 'DISCO_MARTINI'], ['5', '7', 'DISCO_MARTINI', 'DISCO_BALL'],
  ['7', 'DISCO_MARTINI', 'DISCO_BALL', 'DISCO_SEVEN'],
]
const cleared = () => [{ reel: 0, row: 0 }, { reel: 0, row: 1 }, { reel: 1, row: 0 }, { reel: 1, row: 1 }]
function baseResult() {
  return {
    spinId: 'neon-api-test', gameId: NEON_GAME_ID, symbolSetId: 'neon-arcade-symbols-v3',
    wagerPoints: 50, pointValueInCents: 1, reels: Array.from({ length: 5 }, () => ['2', '4', '5', '7']),
    payout: { totalPoints: 0, paylines: [] }, bananaBonusPoints: 0,
    isFreeSpin: false, freeSpinsAwarded: 0, slotsCreditsBalance: 10,
    freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null, neonDiscoSpinsRemaining: 0, neonDiscoMultiplier: 1,
    neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0, activators: [] },
  }
}
function discoResult() {
  const next = board()
  next[0] = ['DISCO_SEVEN', 'DISCO_MARTINI', '4', '5']
  next[1] = ['DISCO_BALL', 'DISCO_SEVEN', '5', '7']
  return {
    ...baseResult(), symbolSetId: NEON_DISCO_SYMBOL_SET_ID, reels: board(), neonDiscoSpinsRemaining: 9, neonDiscoMultiplier: 2,
    payout: { totalPoints: 10, paylines: [] },
    neonDiscoCascade: { initialReels: board(), totalPoints: 10, steps: [{
      multiplier: 1, cleared: cleared(), reels: next,
      dropRows: [[2, 2, 0, 0], [2, 2, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]],
      payout: { totalPoints: 10, paylines: [{ paylineId: 0, amountPoints: 10, matches: [{
        amountPoints: 10, multiplier: .2,
        match: { symbolId: '2', matchLength: 4, positions: cleared(), wildPositions: [] },
      }] }] },
    }] },
  }
}

it('accepts base results and a four-cell Disco cluster with exclusive Disco refills', () => {
  expect(isNeonSpinResult(baseResult())).toBe(true)
  expect(isNeonSpinResult(discoResult())).toBe(true)
})

it('keeps Disco-only symbols out of base play and base feature symbols out of every Disco board', () => {
  const base = baseResult()
  base.reels[0][0] = 'DISCO_MARTINI'
  expect(isNeonSpinResult(base)).toBe(false)
  base.reels[0][0] = 'SEAL_RAND'
  expect(isNeonSpinResult(base)).toBe(false)
  expect(isNeonSpinResult({ ...baseResult(), neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0,
    activators: [{ kind: 'arcade-cabinet', symbolId: 'SEAL_RAND', positions: [{ reel: 0, row: 0 }] }],
  } })).toBe(false)
  for (const symbol of ['ACE', 'FREE', 'POWER', 'BANANA', '6', 'SEAL_RAND']) {
    const initial = discoResult()
    initial.reels[4][3] = symbol
    initial.neonDiscoCascade.initialReels[4][3] = symbol
    expect(isNeonSpinResult(initial)).toBe(false)
    const refill = discoResult()
    refill.neonDiscoCascade.steps[0].reels[4][3] = symbol
    expect(isNeonSpinResult(refill)).toBe(false)
  }
})

it('rejects diagonal-only groups and clear lists that do not match the paid cluster', () => {
  const diagonal = discoResult()
  const positions = [0, 1, 2, 3].map(index => ({ reel: index, row: index }))
  for (const grid of [diagonal.reels, diagonal.neonDiscoCascade.initialReels]) {
    for (const reel of grid) for (let row = 0; row < reel.length; row++) if (reel[row] === '2') reel[row] = 'DISCO_SEVEN'
    for (const cell of positions) grid[cell.reel][cell.row] = '2'
  }
  diagonal.neonDiscoCascade.steps[0].cleared = positions
  diagonal.neonDiscoCascade.steps[0].payout.paylines[0].matches[0].match.positions = positions
  expect(isNeonSpinResult(diagonal)).toBe(false)
  const wrongClear = discoResult()
  wrongClear.neonDiscoCascade.steps[0].cleared[0] = { reel: 4, row: 3 }
  expect(isNeonSpinResult(wrongClear)).toBe(false)
})

it('accepts a carried cascade tier and rejects skipped or invalid tiers', () => {
  const ladder = discoResult()
  const template = ladder.neonDiscoCascade.steps[0]
  ladder.neonDiscoMultiplier = 5
  ladder.neonDiscoCascade.steps = [2, 3, 5, 5].map(multiplier => ({ ...structuredClone(template), multiplier, reels: board() }))
  ladder.neonDiscoCascade.totalPoints = ladder.payout.totalPoints = 40
  expect(isNeonSpinResult(ladder)).toBe(true)
  ladder.neonDiscoCascade.steps[1].multiplier = 5
  expect(isNeonSpinResult(ladder)).toBe(false)
  ladder.neonDiscoCascade.steps[0].multiplier = 4
  expect(isNeonSpinResult(ladder)).toBe(false)
})

it('rejects base awards or missing cascade details in a Disco result', () => {
  const feature = discoResult()
  feature.freeSpinsAwarded = 7
  expect(isNeonSpinResult(feature)).toBe(false)
  expect(isNeonSpinResult({ ...discoResult(), neonDiscoCascade: null })).toBe(false)
})
