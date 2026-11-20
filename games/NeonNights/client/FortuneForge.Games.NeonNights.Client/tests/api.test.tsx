import { expect, it } from 'vitest'
import { createNeonApi, isNeonSpinResult, isNeonState } from '../src/api'
import { NEON_CAR_SYMBOL_SET_ID, NEON_DISCO_SYMBOL_SET_ID, NEON_PREVIOUS_DISCO_SYMBOL_SET_ID, NEON_GAME_ID } from '../src/types'

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

it('restores a Disco wager independently from queued Car and accepts legacy rounds without one', async () => {
  const queued = { ...baseResult(), freeSpinsRemaining: 7, freeSpinWagerPoints: 300,
    neonDiscoSpinsRemaining: 12, neonDiscoWagerPoints: 150 }
  const api = createNeonApi(async () => Response.json(queued))
  await expect(api.state()).resolves.toEqual(queued)
  expect(isNeonState({ ...queued, neonDiscoWagerPoints: null })).toBe(true)
  const { neonDiscoWagerPoints: omitted, ...legacy } = queued
  expect(omitted).toBe(150)
  expect(isNeonState(legacy)).toBe(true)
  for (const invalid of [0, -1, 1.5, '150', Number.MAX_SAFE_INTEGER + 1])
    expect(isNeonState({ ...queued, neonDiscoWagerPoints: invalid })).toBe(false)
})

function wildDiscoResult() {
  const value = discoResult()
  value.reels[0][0] = value.neonDiscoCascade.initialReels[0][0] = 'DISCO_WILD'
  value.neonDiscoCascade.steps[0].payout.paylines[0].matches[0].match.wildPositions = [{ reel: 0, row: 0 }]
  return value
}

it('accepts connected Disco substitutions and validates the exact Wild positions', () => {
  expect(isNeonSpinResult(wildDiscoResult())).toBe(true)
  const missing = wildDiscoResult()
  missing.neonDiscoCascade.steps[0].payout.paylines[0].matches[0].match.wildPositions = []
  expect(isNeonSpinResult(missing)).toBe(false)
  const falseWild = wildDiscoResult()
  falseWild.neonDiscoCascade.steps[0].payout.paylines[0].matches[0].match.wildPositions.push({ reel: 1, row: 0 })
  expect(isNeonSpinResult(falseWild)).toBe(false)
  const repeated = wildDiscoResult()
  repeated.neonDiscoCascade.steps[0].payout.paylines[0].matches[0].match.wildPositions.push({ reel: 0, row: 0 })
  expect(isNeonSpinResult(repeated)).toBe(false)
})

it('keeps the previous seven-symbol Disco contract and rejects Wilds under that version', () => {
  const previous = discoResult()
  previous.symbolSetId = NEON_PREVIOUS_DISCO_SYMBOL_SET_ID
  expect(isNeonSpinResult(previous)).toBe(true)
  const previousWithWild = wildDiscoResult()
  previousWithWild.symbolSetId = NEON_PREVIOUS_DISCO_SYMBOL_SET_ID
  expect(isNeonSpinResult(previousWithWild)).toBe(false)
})

it('never pays Wild as a native symbol or uses its cell for two clusters', () => {
  const nativeWild = wildDiscoResult()
  const wildMatch = nativeWild.neonDiscoCascade.steps[0].payout.paylines[0].matches[0].match
  for (const cell of wildMatch.positions) {
    nativeWild.reels[cell.reel][cell.row] = nativeWild.neonDiscoCascade.initialReels[cell.reel][cell.row] = 'DISCO_WILD'
  }
  wildMatch.wildPositions = cleared()
  wildMatch.symbolId = 'DISCO_WILD'
  expect(isNeonSpinResult(nativeWild)).toBe(false)
  wildMatch.symbolId = '2'
  expect(isNeonSpinResult(nativeWild)).toBe(false)

  const duplicate = wildDiscoResult()
  const step = duplicate.neonDiscoCascade.steps[0]
  step.payout.paylines.push(structuredClone(step.payout.paylines[0]))
  step.payout.totalPoints = duplicate.payout.totalPoints = duplicate.neonDiscoCascade.totalPoints = 20
  expect(isNeonSpinResult(duplicate)).toBe(false)
})

it('accepts the connected remainder after a competing Wild cluster claims its shared cells', () => {
  const result = discoResult()
  for (const grid of [result.reels, result.neonDiscoCascade.initialReels]) {
    grid[1][0] = 'DISCO_WILD'
    grid[1][2] = '2'
    grid[3][0] = grid[3][1] = '4'
  }
  const queenCells = [{ reel: 1, row: 0 }, { reel: 2, row: 0 }, { reel: 3, row: 0 }, { reel: 3, row: 1 }]
  const jackCells = [{ reel: 0, row: 0 }, { reel: 0, row: 1 }, { reel: 1, row: 1 }, { reel: 1, row: 2 }]
  const step = result.neonDiscoCascade.steps[0]
  const jack = structuredClone(step.payout.paylines[0])
  jack.matches[0].match.positions = jackCells
  const queen = structuredClone(jack)
  queen.amountPoints = queen.matches[0].amountPoints = 20
  queen.matches[0].match = { symbolId: '4', matchLength: 4, positions: queenCells, wildPositions: [{ reel: 1, row: 0 }] }
  step.payout.paylines = [queen, jack]
  step.cleared = [...queenCells, ...jackCells]
  step.payout.totalPoints = result.payout.totalPoints = result.neonDiscoCascade.totalPoints = 30
  expect(isNeonSpinResult(result)).toBe(true)
})

it('accepts only the first eligible Car Plays Nitro and its adjacent expansion', () => {
  const initialReels = Array.from({ length: 5 }, () => ['2', '4', '5', '7', '2', '4', '5'])
  initialReels[1][2] = 'NITRO'
  initialReels[2][4] = 'NITRO'
  const reels = initialReels.map((reel, index) => index === 2 ? reel.map(() => 'ACE') : [...reel])
  const evidence = { trigger: { reel: 1, row: 2 }, eligibleReel: 2, expandedReel: 2, initialReels }
  const paired = { ...baseResult(), symbolSetId: NEON_CAR_SYMBOL_SET_ID, isFreeSpin: true, reels, neonNitroWild: evidence }

  expect(isNeonSpinResult(paired)).toBe(true)
  expect(isNeonSpinResult({ ...paired, isFreeSpin: false })).toBe(false)
  expect(isNeonSpinResult({ ...paired, neonNitroWild: { ...evidence, eligibleReel: 3 } })).toBe(false)
  expect(isNeonSpinResult({ ...paired, neonNitroWild: { ...evidence, trigger: { reel: 2, row: 4 } } })).toBe(false)
  expect(isNeonSpinResult({ ...paired, reels: initialReels })).toBe(false)

  const miss = initialReels.map(reel => [...reel])
  miss[2][4] = '5'
  miss[3][0] = 'NITRO'
  miss[4][0] = 'NITRO'
  expect(isNeonSpinResult({ ...paired, reels: miss, neonNitroWild: { ...evidence, initialReels: miss, expandedReel: null } })).toBe(true)
  expect(isNeonSpinResult({ ...paired, reels: miss, neonNitroWild: null })).toBe(false)
  miss[1][2] = '5'
  miss[3][0] = '2'
  expect(isNeonSpinResult({ ...paired, reels: miss, neonNitroWild: null })).toBe(true)
})

it('validates the third reel expansion and a persisted last-two-reel Nitro choice', async () => {
  const initial = Array.from({ length: 5 }, () => ['2', '4', '5', '7', '2', '4', '5'])
  initial[1][2] = 'NITRO'
  initial[2][4] = 'NITRO'
  const settled = initial.map((reel, column) => column === 3 ? reel.map(() => 'ACE') : reel)
  expect(isNeonSpinResult({ ...baseResult(), symbolSetId: NEON_CAR_SYMBOL_SET_ID, isFreeSpin: true, reels: settled,
    neonNitroWild: { trigger: { reel: 1, row: 2 }, secondNitro: { reel: 2, row: 4 }, eligibleReel: 2, expandedReel: 3, initialReels: initial } })).toBe(true)

  const lastPair = Array.from({ length: 5 }, () => ['2', '4', '5', '7', '2', '4', '5'])
  lastPair[3][2] = 'NITRO'
  lastPair[4][1] = 'NITRO'
  const evidence = { trigger: { reel: 3, row: 2 }, secondNitro: { reel: 4, row: 1 }, eligibleReel: 4, initialReels: lastPair }
  const pendingNeonNitroChoice = { sourceKind: 'spin', spinId: 'pending-test', wagerPoints: 50, freeSpinsRemaining: 0,
    initialReels: lastPair, eligibleReels: [0, 1, 2, 3, 4], neonNitroWild: { ...evidence, expandedReel: null, requiresChoice: true } }
  const pending = { pendingNeonNitroChoice }
  const result = { ...baseResult(), spinId: pendingNeonNitroChoice.spinId, symbolSetId: NEON_CAR_SYMBOL_SET_ID, isFreeSpin: true,
    reels: lastPair.map((reel, column) => column === 0 ? reel.map(() => 'ACE') : reel),
    neonNitroWild: { ...evidence, expandedReel: 0, requiresChoice: false } }
  const api = createNeonApi(async path => path.endsWith('/nitro-choice') ? Response.json(result) : Response.json(pending, { status: 202 }))
  expect(isNeonState({ ...baseResult(), ...pending })).toBe(true)
  expect(isNeonState({ ...baseResult(), pendingNeonNitroChoice: { ...pendingNeonNitroChoice, eligibleReels: [0, 1, 2, 3, 3] } })).toBe(false)
  const received = await api.spin({ wagerPoints: 50, useFreeSpin: true, state: { ...baseResult(), neonDiscoMultiplier: 1 } }, false)
  if ('reels' in received) throw new Error('Expected a pending choice')
  await expect(api.chooseNitro(received.pendingNeonNitroChoice, 0)).resolves.toEqual(result)
  await expect(api.chooseNitro(received.pendingNeonNitroChoice, 1)).rejects.toThrow('invalid Nitro result')
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
