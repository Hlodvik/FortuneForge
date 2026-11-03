import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { GOLD_FOUNDRY_GAME_ID } from '../goldFoundry/identity'
import type { SlotSymbolId } from '../../../features/slots/types/slots'
import { loadAllSlotGameManifests } from '..'
import { getSlotEngineGuideContract, withEngineGuide } from './engineGuide'

type EngineContract = {
  gameId: string
  profile: {
    scatterRequiredSymbols: number; scatterAwardedSpins: number
    collectionTarget: number; collectionAwardedSpins: number
    usesEnergy: boolean; usesCollections: boolean; usesDirectValueTokens: boolean
    paylinePatternIds: number[] | null
  }
  layout: { paylineCount: number }
  math: { payoutMultiplier: number; paylinePayoutSteps: number[] }
  symbols: { nativeWildMatchLengths: number[]; wildSubstitutionMatchLengths: number[] }
  paytable: { symbolId: SlotSymbolId; matchLength: number; multiplier: number }[]
}
const fixture = JSON.parse(readFileSync(new URL('./__fixtures__/engineGuide-contracts.json', import.meta.url), 'utf8')) as {
  sourceSha256: Record<string, string>; games: EngineContract[]
}
const manifests = await loadAllSlotGameManifests()
const audited = manifests.filter(game => !['rainbow-realm-fruits-v1', 'dragon-hoard-v1', GOLD_FOUNDRY_GAME_ID].includes(game.experience.rules.gameId))

it('retains the server sources that produced the independently exported guide contracts', () => {
  for (const [path, expected] of Object.entries(fixture.sourceSha256)) {
    const current = readFileSync(new URL(`../../../../../${path}`, import.meta.url))
    expect(createHash('sha256').update(current).digest('hex'), `Re-export actual engine evidence after changing ${path}`).toBe(expected)
  }
  expect(fixture.games).toHaveLength(20)
  expect(audited).toHaveLength(17)
})

describe.each(audited)('$catalog.title audited guide', game => {
  const experience = game.experience
  const engine = fixture.games.find(candidate => candidate.gameId === experience.rules.gameId)!
  const profile = engine.profile
  const entry = (symbol: SlotSymbolId) => experience.symbols.guideEntries.find(candidate => candidate.symbol === symbol)

  it('matches effective paths, scatter awards and collection thresholds from the real provider', () => {
    const pathIds = profile.paylinePatternIds ?? Array.from({ length: engine.layout.paylineCount }, (_, index) => index + 1)
    expect(experience.help.paylinePatternIds).toEqual(pathIds)
    expect(experience.help.paylineCount).toBe(engine.layout.paylineCount)
    expect(getSlotEngineGuideContract(engine.gameId).payoutMultiplier).toBe(engine.math.payoutMultiplier)
    if (profile.scatterAwardedSpins > 0) {
      expect(experience.help.freeGames).toMatchObject({ requiredSymbols: profile.scatterRequiredSymbols, awardedSpins: profile.scatterAwardedSpins })
      expect(entry('FREE')?.firstLabel).toBe(`${profile.scatterRequiredSymbols}+`)
      expect(Number.parseInt(entry('FREE')?.secondValue ?? '')).toBe(profile.scatterAwardedSpins)
    } else {
      expect(experience.help.freeGames).toBeUndefined()
      expect(entry('FREE')).toBeUndefined()
    }
    for (const collection of experience.features.collections?.entries ?? []) {
      expect(collection.requiredCount).toBe(profile.collectionTarget)
      if (game.id !== 'pirates-fortune') {
        expect(entry(collection.symbol)?.firstValue).toBe(`collect ${profile.collectionTarget}`)
        expect(Number.parseInt(entry(collection.symbol)?.secondValue ?? '')).toBe(profile.collectionAwardedSpins)
      }
    }
  })

  it('prices cash labels from the exported paytable and distinguishes native wild and special-point lengths', () => {
    for (const rule of engine.paytable) {
      const shown = rule.matchLength === 3 ? entry(rule.symbolId)?.firstValue : entry(rule.symbolId)?.secondValue
      expect(Number.parseFloat(shown ?? ''), `${rule.symbolId}/${rule.matchLength}`).toBeCloseTo(rule.multiplier * engine.math.payoutMultiplier, 6)
    }
    expect(engine.symbols.nativeWildMatchLengths).toEqual([3, 5])
    expect(engine.symbols.wildSubstitutionMatchLengths).toEqual([5])
    expect(entry('ACE')?.firstLabel).toBe('3')
    expect(entry('POWER')?.firstValue).not.toContain('+1')
    expect(experience.symbols.guideNote).toContain('Four-symbol wins award no special points.')
    expect(experience.help.fourMatchPayoutNote).toContain('pay the three-symbol rate')
    expect(experience.help.fiveMatchPayoutNote).toContain('use the same symbol multipliers')
    expect(engine.math.paylinePayoutSteps.every(step => step === 0)).toBe(true)
  })

  it('omits rewards for disabled features without changing the playable rules or artwork', () => {
    if (!profile.usesEnergy) {
      expect(experience.features.energy).toBeUndefined()
      expect(entry('BOLT')).toBeUndefined()
    }
    if (!profile.usesDirectValueTokens) {
      expect(experience.features.moneyGrab).toBeUndefined()
      expect(entry('PAW')).toBeUndefined()
      expect(experience.symbols.guideEntries.some(candidate => candidate.symbol.startsWith('RAND_'))).toBe(false)
    }
    if (!profile.usesCollections) {
      expect(experience.features.collections).toBeUndefined()
      expect(experience.symbols.guideEntries.some(candidate => candidate.symbol.startsWith('SEAL_'))).toBe(false)
    }
    const appliedAgain = withEngineGuide(experience)
    expect(appliedAgain.rules).toBe(experience.rules)
    expect(appliedAgain.sounds).toBe(experience.sounds)
    expect(appliedAgain.cabinet).toBe(experience.cabinet)
    expect(appliedAgain.symbols.definitions).toBe(experience.symbols.definitions)
  })
})

it('matches the Gold Foundry guide to exported line prices', () => {
  const experience = manifests.find(game => game.experience.rules.gameId === GOLD_FOUNDRY_GAME_ID)!.experience
  const engine = fixture.games.find(game => game.gameId === GOLD_FOUNDRY_GAME_ID)!
  const { help, symbols } = experience

  expect(help.paylineCount).toBe(engine.layout.paylineCount)
  expect(help.paylinePatternIds).toEqual(engine.profile.paylinePatternIds)
  expect(engine.profile.scatterAwardedSpins).toBe(0)
  expect(engine.profile.usesCollections).toBe(false)
  expect(engine.symbols.nativeWildMatchLengths).toEqual([3, 4, 5])
  expect(engine.symbols.wildSubstitutionMatchLengths).toEqual([5])

  const displayedRates = symbols.guideEntries.filter(entry => entry.firstLabel === '3 / 4').flatMap(entry => {
    const [three, four] = entry.firstValue.split(' / ').map(value => Number.parseFloat(value))
    return [
      { symbolId: entry.symbol, matchLength: 3, rate: three },
      { symbolId: entry.symbol, matchLength: 4, rate: four },
      { symbolId: entry.symbol, matchLength: 5, rate: Number.parseFloat(entry.secondValue ?? '') },
    ]
  })
  expect(displayedRates).toHaveLength(engine.paytable.length)
  for (const rule of engine.paytable) {
    const displayedRate = displayedRates.find(entry =>
      entry.symbolId === rule.symbolId && entry.matchLength === rule.matchLength)
    expect(displayedRate?.rate).toBeCloseTo(rule.multiplier * engine.math.payoutMultiplier, 6)
  }
})

it('preserves the separately maintained Rainbow Realm and Midnight Vault experiences', () => {
  for (const id of ['rainbow-realm', 'dragon-hoard']) {
    const experience = manifests.find(game => game.id === id)!.experience
    expect(withEngineGuide(experience)).toBe(experience)
  }
})
