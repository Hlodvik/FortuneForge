import { describe, expect, it } from 'vitest'
import { slotPointsToRand } from '../../../features/slots/slotPagePresentation'
import engineContracts from '../shared/__fixtures__/engineGuide-contracts.json'
import { NEON_NIGHTS_SLOT_GAME } from './manifest'

const experience = NEON_NIGHTS_SLOT_GAME.experience
const contract = engineContracts.games.find(game => game.gameId === 'neon-nights-v1')!
const activeSymbols = [
  '2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BANANA',
  'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND',
] as const

describe('Neon arcade presentation contract', () => {
  it('keeps the same cash wager range with cent precision and no collection or scatter awards', () => {
    expect(experience.symbols.serverSymbolSetId).toBe('neon-arcade-symbols-v2')
    expect(experience.rules.pointValueInCents).toBe(contract.wagering.pointValueInCents)
    expect(experience.rules.wagerOptions).toHaveLength(1_000)
    expect(slotPointsToRand(experience.rules.wagerOptions[0], experience.rules.pointValueInCents)).toBe(.5)
    expect(slotPointsToRand(experience.rules.wagerOptions.at(-1)!, experience.rules.pointValueInCents)).toBe(500)
    expect(experience.rules.wagerOptions.every((wager, index) => wager === (index + 1) * 50)).toBe(true)
    expect(experience.features.collections).toBeUndefined()
    expect(experience.features.energy).toBeUndefined()
    expect(experience.features.moneyGrab).toBeUndefined()
    expect(experience.help.freeGames).toBeUndefined()
    expect(experience.help.paylineCount).toBe(contract.layout.paylineCount)
    expect(experience.help.paylinePatternIds).toEqual(Array.from({ length: contract.layout.paylineCount }, (_, index) => index + 1))
    expect(contract.profile).toMatchObject({
      scatterAwardedSpins: 0, collectionAwardedSpins: 0, usesCollections: false,
    })
  })

  it('presents all fourteen active symbols and line awards from the server paytable', () => {
    expect([...new Set(experience.symbols.animationSymbolIds ?? [])].sort()).toEqual([...activeSymbols].sort())
    for (const symbol of activeSymbols) expect(experience.symbols.definitions[symbol]).toBeDefined()
    const idle = experience.rules.initialReels.flat()
    for (const symbol of ['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW']) {
      expect(idle).toContain(symbol)
      expect(contract.paytable.some(payout => payout.symbolId === symbol)).toBe(true)
    }
    for (const payout of contract.paytable) {
      const guide = experience.symbols.guideEntries.find(entry => entry.symbol === payout.symbolId)
      const shown = payout.matchLength === 3 ? guide?.firstValue : guide?.secondValue
      expect(shown, `${payout.symbolId}/${payout.matchLength}`).toBe(`${payout.multiplier}×`)
    }
    expect(idle).not.toContain('BOLT')
    expect(idle).not.toContain('PAW')
  })

  it('explains the BONUS boost and the Special Plays triggers', () => {
    const bonus = experience.symbols.guideEntries.find(entry => entry.symbol === 'FREE')
    expect(bonus).toMatchObject({ firstValue: '+25% to payline wins', secondValue: '2 BONUS = +50%' })
    for (const symbol of ['6', 'SEAL_RAND']) {
      expect(contract.paytable.some(payout => payout.symbolId === symbol)).toBe(false)
      expect(experience.symbols.guideEntries.find(entry => entry.symbol === symbol)).toMatchObject({
        firstLabel: 'Special Plays', firstValue: '7 bonus spins', secondValue: 'Land anywhere, including during Special Plays',
      })
    }
    expect(experience.symbols.guideEntries.find(entry => entry.symbol === 'POWER')).toMatchObject({
      firstLabel: 'Disco floor', firstValue: '10 paid spins',
    })
    const visibleCopy = [
      experience.features.specialRound?.earnLabel,
      experience.features.specialRound?.earnHint,
      experience.symbols.guideNote,
      ...(experience.help.extraSections?.flatMap(section => [section.title, section.body]) ?? []),
      ...experience.symbols.guideEntries.flatMap(entry => [entry.firstValue, entry.secondValue ?? '']),
    ].join(' ')
    expect(visibleCopy).not.toMatch(/collect 26|full ring|6 Midnight Mixes|7 enhanced mixes/i)
    expect(visibleCopy).toContain('does not multiply triple-sevens pattern prizes or award free spins')
  })
})
