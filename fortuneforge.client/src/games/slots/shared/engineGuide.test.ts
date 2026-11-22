// Edited: 2026-11-18T19:29:04-06:00 | Computer: Vinnabok
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { GOLD_FOUNDRY_GAME_ID } from '@fortuneforge/games-gold-foundry/manifest'
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
  symbols: { wildSymbolId: string; nativeWildMatchLengths: number[]; wildSubstitutionMatchLengths: number[] }
  paytable: { symbolId: string; matchLength: number; multiplier: number }[]
}
const fixture = JSON.parse(readFileSync(new URL('./__fixtures__/engineGuide-contracts.json', import.meta.url), 'utf8')) as {
  sourceHashAlgorithm: string; sourceSha256: Record<string, string>; sourceProjects: string[]; games: EngineContract[]
}
const hashSource = (bytes: Uint8Array) => createHash('sha256').update(
  new TextDecoder('utf-8', { fatal: true }).decode(bytes).replace(/\r\n?/g, '\n'),
).digest('hex')
const manifests = await loadAllSlotGameManifests()
const audited = manifests.filter(game => !['rainbow-realm-fruits-v1', 'dragon-hoard-v1', 'neon-nights-v1', GOLD_FOUNDRY_GAME_ID].includes(game.experience.rules.gameId))

it('retains the server sources that produced the independently exported guide contracts', () => {
  expect(fixture.sourceHashAlgorithm).toBe('sha256-utf8-lf-v1')
  expect(fixture.sourceProjects).toHaveLength(20)
  expect(new Set(fixture.sourceProjects).size).toBe(20)
  for (const project of fixture.sourceProjects) {
    expect(project).toMatch(/^games\/[^/]+\/src\/[^/]+\/[^/]+\.csproj$/)
    expect(fixture.sourceSha256[project], `Retain the package that owns ${project}`).toMatch(/^[a-f0-9]{64}$/)
  }
  for (const [path, expected] of Object.entries(fixture.sourceSha256)) {
    const current = readFileSync(new URL(`../../../../../${path}`, import.meta.url))
    expect(hashSource(current), `Re-export actual engine evidence after changing ${path}`).toBe(expected)
  }
  expect(fixture.games).toHaveLength(19)
  expect(audited).toHaveLength(15)
})

it('retains source identity across checkout line endings while detecting changed text', () => {
  const canonical = hashSource(Buffer.from('alpha\nbeta\n'))
  expect(hashSource(Buffer.from('alpha\r\nbeta\r\n'))).toBe(canonical)
  expect(hashSource(Buffer.from('\uFEFFalpha\rbeta\r'))).toBe(canonical)
  expect(hashSource(Buffer.from('alpha\nchanged\n'))).not.toBe(canonical)
})

describe.each(audited.map(game => [game.id, game] as const))('%s audited guide', (_id, game) => {
  const experience = game.experience
  const engine = fixture.games.find(candidate => candidate.gameId === experience.rules.gameId)!
  const profile = engine.profile
  const entry = (symbol: string) => experience.symbols.guideEntries.find(candidate => candidate.symbol === symbol)

  it('matches effective paths, scatter awards and collection thresholds from the real provider', () => {
    const pathIds = profile.paylinePatternIds ?? Array.from({ length: engine.layout.paylineCount }, (_, index) => index + 1)
    expect(experience.help.paylinePatternIds).toEqual(pathIds)
    expect(experience.help.paylineCount).toBe(engine.layout.paylineCount)
    expect(getSlotEngineGuideContract(engine.gameId)).toMatchObject({
      payoutMultiplier: engine.math.payoutMultiplier,
      paylinePatternIds: pathIds,
      scatterRequiredSymbols: profile.scatterRequiredSymbols,
      scatterAwardedSpins: profile.scatterAwardedSpins,
      collectionTarget: profile.collectionTarget,
      collectionAwardedSpins: profile.collectionAwardedSpins,
      usesCollections: profile.usesCollections,
      usesEnergy: profile.usesEnergy,
      usesDirectValueTokens: profile.usesDirectValueTokens,
    })
    if (profile.scatterAwardedSpins > 0) {
      expect(experience.help.freeGames).toMatchObject({ requiredSymbols: profile.scatterRequiredSymbols, awardedSpins: profile.scatterAwardedSpins })
      if (game.id === 'robot-revolution') {
        expect(entry('ECLIPSE')?.firstLabel).toBe(`${profile.scatterRequiredSymbols} scatters`)
        expect(Number.parseInt(entry('ECLIPSE')?.firstValue ?? '')).toBe(profile.scatterAwardedSpins)
      } else {
        expect(entry('FREE')?.firstLabel).toBe(`${profile.scatterRequiredSymbols}+`)
        expect(Number.parseInt(entry('FREE')?.secondValue ?? '')).toBe(profile.scatterAwardedSpins)
      }
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
    if (game.id === 'arcane-archives') {
      expect(engine.symbols.wildSymbolId).toBe('')
      expect(engine.symbols.nativeWildMatchLengths).toEqual([])
      expect(engine.symbols.wildSubstitutionMatchLengths).toEqual([])
      expect(experience.help.wildSubstitution).toBe(false)
      expect(experience.symbols.guideEntries.filter(candidate => candidate.secondValue).map(candidate => candidate.symbol)).toEqual(
        [...new Set(engine.paytable.map(rule => rule.symbolId))],
      )
      expect(experience.symbols.guideNote).toBeUndefined()
      expect(experience.help.fiveMatchPayoutNote).toContain('same symbol values')
      expect(experience.help.fourMatchPayoutNote).toBe('Only runs of exactly three or five matching symbols pay.')
      expect(experience.symbols.guideEntries.filter(candidate => candidate.secondValue)
        .map(candidate => candidate.firstLabel)).toEqual(['3', '3', '3', '3', '3'])
    } else if (game.id === 'robot-revolution') {
      expect(engine.symbols.wildSymbolId).toBe('WILD')
      expect(engine.symbols.nativeWildMatchLengths).toEqual([3, 5])
      expect(engine.symbols.wildSubstitutionMatchLengths).toEqual([3, 5])
      expect(entry('WILD')?.firstLabel).toBe('3')
      const fiveMatchRules = engine.paytable.filter(rule => rule.matchLength === 5)
      const sunstone = fiveMatchRules.find(rule => rule.symbolId === 'SUNSTONE')!
      expect(sunstone.multiplier).toBeGreaterThan(Math.max(...fiveMatchRules
        .filter(rule => rule.symbolId !== 'SUNSTONE').map(rule => rule.multiplier)))
      expect(experience.symbols.guideEntries.filter(candidate => candidate.symbol.startsWith('GLYPH_'))).toHaveLength(5)
      expect(experience.symbols.guideNote).toContain('three- and five-symbol')
      expect(experience.help.fourMatchPayoutNote).toMatch(/four Wilds/i)
    } else {
      expect(engine.symbols.nativeWildMatchLengths).toEqual([3, 5])
      expect(engine.symbols.wildSubstitutionMatchLengths).toEqual([5])
      expect(entry('ACE')?.firstLabel).toBe('3')
      expect(entry('POWER')?.firstValue).not.toContain('+1')
      expect(experience.symbols.guideNote).toContain('Four-symbol wins award no special points.')
      expect(experience.help.fiveMatchPayoutNote).toContain('use the same symbol multipliers')
      expect(experience.help.fourMatchPayoutNote).toContain('pay the three-symbol rate')
    }
    expect(engine.math.paylinePayoutSteps.every(step => step === 0)).toBe(true)
  })

  it('omits rewards for disabled features without changing the playable rules or artwork', () => {
    if (!profile.usesEnergy) {
      expect(experience.features.energy).toBeUndefined()
      if (game.id === 'arcane-archives') {
        expect(entry('BOLT')?.companionSymbol).toBe('WAND')
      } else {
        expect(Boolean(entry('BOLT'))).toBe(experience.features.lightningCollection?.collectorSymbol === 'BOLT')
      }
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
    if (game.id === 'robot-revolution') expect(appliedAgain).toBe(experience)
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

it('preserves the separately maintained Arcane, Rainbow Realm, Midnight Vault and Neon experiences', () => {
  for (const id of ['arcane-archives', 'rainbow-realm', 'dragon-hoard', 'neon-nights']) {
    const experience = manifests.find(game => game.id === id)!.experience
    expect(withEngineGuide(experience)).toBe(experience)
  }
})
