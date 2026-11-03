import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { SlotSymbol } from '../../../features/slots/components/SlotSymbol'
import { createSlotRulesSet } from '../../../features/slots/config/slotExperienceSets'
import { selectWinSoundEvent } from '../../../features/slots/presentation/spinPresentation'
import type { SlotResponsiveImages } from '../../../features/slots/config/symbolSets'
import { JUNGLE_JACKPOT_DEFINITION } from '../jungleJackpot/definition'
import { NEON_NIGHTS_DEFINITION } from '../neonNights/definition'
import { createShowcaseSlotGame } from './createShowcaseSlotGame'
import { createThemedSymbolSet, type ThemedSymbolSetOptions } from './themedSymbolSet'

const requiredIds = ['2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BOLT', 'BANANA', 'PAW',
  'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const
const options: ThemedSymbolSetOptions = {
  id: 'test-symbols', serverSymbolSetId: 'legacy-symbols',
  symbols: Object.fromEntries(requiredIds.map(id => [id, { label: `Symbol ${id}`, image: `/${id}.webp` }])) as ThemedSymbolSetOptions['symbols'],
  valueToken: { label: 'token', image: '/token.webp' }, energyEarnLabel: '+1 energy',
  collectorFirstValue: 'collects tokens', collectorSecondValue: 'double tokens',
  collectionAwardLabels: { SEAL_SYNC: 'sync', SEAL_ROWS: 'rows', SEAL_PAW: 'paw', SEAL_RAND: 'rand' },
}
const responsive: SlotResponsiveImages = { 96: '/8-96.webp', 192: '/8-192.webp', 320: '/8-320.webp' }

describe('opt-in additional ordinary slot symbols', () => {
  it('keeps the original themed definitions and guide unchanged when no extra symbol is supplied', () => {
    const legacy = createThemedSymbolSet(options)
    expect(Object.keys(legacy.definitions)).toHaveLength(23)
    expect(legacy.definitions['8']).toBeUndefined()
    expect(legacy.guideEntries.map(entry => entry.symbol)).toEqual(requiredIds)
    expect(legacy.serverSymbolSetId).toBe('legacy-symbols')
  })

  it('registers only the supplied extra ordinary symbol with its responsive artwork', () => {
    const legacy = createThemedSymbolSet(options)
    const extended = createThemedSymbolSet({ ...options,
      additionalSymbols: { '8': { label: 'Cash briefcase', image: responsive[96], responsiveImages: responsive } },
    })
    expect(extended.definitions['8']).toEqual({ id: '8', label: 'Cash briefcase', image: responsive[96],
      animatedImage: responsive[96], responsiveImages: responsive })
    const { '8': extra, ...existing } = extended.definitions
    expect(extra).toBeDefined()
    expect(existing).toEqual(legacy.definitions)
    expect(extended.guideEntries).toEqual(legacy.guideEntries)
  })

  it('retains the legacy showcase server identity, idle reels and symbol count without opt-ins', () => {
    const game = createShowcaseSlotGame(JUNGLE_JACKPOT_DEFINITION)
    expect(game.experience.symbols.serverSymbolSetId).toBe('classic-symbols-v3')
    expect(game.experience.symbols.definitions['8']).toBeUndefined()
    expect(Object.keys(game.experience.symbols.definitions)).toHaveLength(23)
    expect(game.experience.rules.initialReels).toEqual(createSlotRulesSet(JUNGLE_JACKPOT_DEFINITION.serverGameId).initialReels)
    expect(game.experience.rules.initialReels.flat()).not.toContain('8')
  })

  it('forwards an explicit server identity, idle grid and custom guide only for the opted-in game', () => {
    const initialReels = [['8', '2', '3', '4']] as const
    const entries = [{ symbol: '8' as const, firstLabel: '3', firstValue: '4×', secondLabel: '5', secondValue: '14×' }]
    const game = createShowcaseSlotGame({ ...NEON_NIGHTS_DEFINITION,
      serverGameId: 'dragon-hoard-v1', serverSymbolSetId: 'midnight-vault-symbols-v1', initialReels,
      additionalSymbolSpecs: { '8': ['Cash briefcase', '▣', responsive[96]] },
      responsiveImages: { [responsive[96]]: responsive }, symbolGuide: { entries, note: 'Game-specific audited guide.' },
    })
    expect(game.experience.symbols.serverSymbolSetId).toBe('midnight-vault-symbols-v1')
    expect(game.experience.rules.initialReels).toEqual(initialReels)
    expect(game.experience.symbols.guideEntries).toEqual(entries)
    expect(game.experience.symbols.guideNote).toBe('Game-specific audited guide.')
    const markup = renderToStaticMarkup(createElement(SlotSymbol, { symbol: '8', symbolSet: game.experience.symbols, highlighted: true }))
    expect(markup).toContain('data-symbol="8"')
    expect(markup).toContain('Cash briefcase, winning symbol')
    expect(game.experience.symbols.definitions['8']?.responsiveImages).toEqual(responsive)
    expect(selectWinSoundEvent({ paylineId: 1, amountPoints: 8,
      matches: [{ amountPoints: 8, multiplier: 4,
        match: { paylineId: 1, symbolId: '8', matchLength: 3, positions: [], wildPositions: [] } }],
    }, 5)).toBe('single-three')
  })
})
