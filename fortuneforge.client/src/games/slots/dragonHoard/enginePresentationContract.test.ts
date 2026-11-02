import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import type { SlotSymbolId } from '../../../features/slots/types/slots'
import { DRAGON_HOARD_DEFINITION } from './definition'
import { DRAGON_HOARD_SLOT_GAME } from './manifest'

type EngineContract = {
  sourceSha256: Record<string, string>
  gameId: string
  profile: {
    scatterRequiredSymbols: number; scatterAwardedSpins: number
    collectionTarget: number; collectionAwardedSpins: number
    usesCollections: boolean; usesEnergy: boolean; usesDirectValueTokens: boolean
    scatterFeatureMode: string | null; collectionFeatureMode: string
    payoutMultiplier: number
  }
  layout: { reelCount: number; visibleRows: number; paylineCount: number }
  symbols: { symbolSetId: string; nativeWildMatchLengths: number[]; wildSubstitutionMatchLengths: number[] }
  symbolSet: { symbols: { id: SlotSymbolId }[] }
  wagering: { pointValueInCents: number; minimumWagerPoints: number; maximumWagerPoints: number; wagerIncrementPoints: number }
  math: { paytableId: string; payoutMultiplier: number; paylinePayoutSteps: number[] }
  paytable: { symbolId: SlotSymbolId; matchLength: number; multiplier: number }[]
}
// Captured through the real controller and provider, including the new paytable.
const engine = JSON.parse(readFileSync(new URL('./evidence/current-engine-contract.json', import.meta.url), 'utf8')) as EngineContract
const { profile } = engine
const experience = DRAGON_HOARD_SLOT_GAME.experience
function guide(symbol: SlotSymbolId) {
  const entry = experience.symbols.guideEntries.find(candidate => candidate.symbol === symbol)
  if (!entry) throw new Error(`Missing presentation guide for active engine symbol ${symbol}`)
  return entry
}

describe('Midnight Vault fifteen-symbol engine presentation contract', () => {
  it('uses current independently exported server evidence', () => {
    for (const [path, expected] of Object.entries(engine.sourceSha256)) {
      const source = readFileSync(new URL(`../../../../../${path}`, import.meta.url))
      expect(createHash('sha256').update(source).digest('hex'), `Re-export real engine evidence after changing ${path}`).toBe(expected)
    }
    expect(engine.symbolSet.symbols).toHaveLength(15)
    expect(experience.symbols.guideEntries.map(entry => entry.symbol).sort()).toEqual(engine.symbolSet.symbols.map(symbol => symbol.id).sort())
    expect(experience.symbols.serverSymbolSetId).toBe(engine.symbols.symbolSetId)
    expect(engine.math.paytableId).toBe('midnight-vault-paytable-v2')
  })

  it('shows the actual paths and wager units', () => {
    expect(experience.rules.gameId).toBe(engine.gameId)
    expect(experience.help.paylinePatternIds).toEqual(Array.from({ length: engine.layout.paylineCount }, (_, index) => index + 1))
    expect(experience.help.paylineCount).toBe(engine.layout.paylineCount)
    expect(engine.math.paylinePayoutSteps).toEqual(Array(engine.layout.paylineCount).fill(0))
    expect(experience.rules.pointValueInCents).toBe(engine.wagering.pointValueInCents)
    expect(experience.rules.wagerOptions).toEqual(Array.from({
      length: (engine.wagering.maximumWagerPoints - engine.wagering.minimumWagerPoints) / engine.wagering.wagerIncrementPoints + 1,
    }, (_, index) => engine.wagering.minimumWagerPoints + index * engine.wagering.wagerIncrementPoints))
  })

  it('advertises the actual scatter and common enhanced collection awards', () => {
    expect(experience.help.freeGames).toMatchObject({ requiredSymbols: profile.scatterRequiredSymbols, awardedSpins: profile.scatterAwardedSpins })
    expect(guide('FREE').firstLabel).toBe(`${profile.scatterRequiredSymbols}+`)
    expect(Number.parseInt(guide('FREE').secondValue ?? '', 10)).toBe(profile.scatterAwardedSpins)
    expect(profile.scatterFeatureMode).toBeNull()
    expect(profile.collectionFeatureMode).toBe('rows-paw')
    expect(Object.keys(experience.features.specialRound!.activeModes)).toEqual([profile.collectionFeatureMode])
    expect(experience.features.collections?.entries).toHaveLength(4)
    for (const collection of experience.features.collections!.entries) {
      expect(collection.requiredCount).toBe(profile.collectionTarget)
      expect(Number.parseInt(guide(collection.symbol).firstValue, 10)).toBe(profile.collectionTarget)
      expect(Number.parseInt(guide(collection.symbol).secondValue ?? '', 10)).toBe(profile.collectionAwardedSpins)
      expect(collection.rewardDescription).toMatch(/six rows.*extra Vault wilds/)
    }
  })

  it('does not advertise disabled token or energy features as active rewards', () => {
    expect(profile).toMatchObject({ usesCollections: true, usesEnergy: false, usesDirectValueTokens: false })
    expect(experience.features.energy).toBeUndefined()
    expect(experience.features.moneyGrab).toBeUndefined()
    expect(experience.symbols.guideEntries.map(entry => entry.symbol)).not.toContain('BOLT')
    expect(experience.symbols.guideEntries.map(entry => entry.symbol)).not.toContain('PAW')
    expect(experience.symbols.guideEntries.every(entry => !entry.symbol.startsWith('RAND_'))).toBe(true)
    expect(experience.help.extraSections?.map(section => section.body).join(' ')).not.toMatch(/gathers every|double the haul|energy meter/i)
  })

  it('prices all seven ordinary symbols and special cash values from the new paytable', () => {
    expect(engine.math.payoutMultiplier).toBe(profile.payoutMultiplier)
    for (const rule of engine.paytable) {
      const entry = guide(rule.symbolId)
      const shown = rule.matchLength === 3 ? entry.firstValue : entry.secondValue
      expect(Number.parseFloat(shown ?? ''), `${rule.symbolId}/${rule.matchLength}`).toBeCloseTo(rule.multiplier * engine.math.payoutMultiplier, 6)
      expect(entry.firstLabel).toBe(rule.symbolId === 'ACE' ? '3' : '3–4')
      expect(entry.secondLabel).toBe('5')
    }
    expect(engine.paytable.filter(rule => rule.symbolId === '8')).toHaveLength(2)
    expect(engine.symbols.nativeWildMatchLengths).toEqual([3, 5])
    expect(engine.symbols.wildSubstitutionMatchLengths).toEqual([5])
    expect(Number.parseFloat(guide('BANANA').secondValue!)).toBeCloseTo(3 * engine.math.payoutMultiplier, 6)
    expect(DRAGON_HOARD_DEFINITION.fourMatchPayoutNote).toContain('Four Vault wilds')
    expect(experience.help.extraSections?.map(section => section.body).join(' ')).toContain('Four-symbol wins award no special points')
  })
})
