import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { SlotCollectionFeature, SlotSpecialRoundFeature } from '../config/slotFeatures'
import { getSlotFeatureProgress } from '../presentation/slotFeatureProgress'
import { SlotFeatureStatus } from './SlotFeatureStatus'

const collections: SlotCollectionFeature = {
  ariaLabel: 'Relic tracks',
  entries: [
    { id: 'sync', label: 'Mirror relics', shortLabel: 'Mirror', symbol: 'SEAL_SYNC', requiredCount: 20 },
    { id: 'rows', label: 'Tower relics', shortLabel: 'Tower', symbol: 'SEAL_ROWS', requiredCount: 20 },
  ],
}

const specialRound: SlotSpecialRoundFeature = {
  id: 'test-feature',
  title: 'Relic Run',
  earnLabel: 'Collect relics.',
  earnHint: 'Complete either relic track.',
  activeModes: { sync: 'Mirror run', rows: 'Tower run' },
}

describe('slot feature status', () => {
  it('selects the track closest to completion and clamps invalid server values', () => {
    expect(getSlotFeatureProgress(collections, [
      { sealId: 'sync', count: 4, averageWagerPoints: 2, requiredCount: 20 },
      { sealId: 'rows', count: 17, averageWagerPoints: 2, requiredCount: 20 },
    ])).toMatchObject({
      current: 17,
      label: 'Tower relics',
      percent: 85,
      target: 20,
    })

    expect(getSlotFeatureProgress(collections, [
      { sealId: 'sync', count: 90, averageWagerPoints: 2, requiredCount: 20 },
    ])?.current).toBe(20)
  })

  it('renders trigger, collection, and reward information before the feature', () => {
    const markup = renderToStaticMarkup(createElement(SlotFeatureStatus, {
      collections,
      collectionStates: [
        { sealId: 'sync', count: 12, averageWagerPoints: 2, requiredCount: 20 },
        { sealId: 'rows', count: 4, averageWagerPoints: 2, requiredCount: 20 },
      ],
      freeSpinsRemaining: 0,
      help: { paylineCount: 20, freeGames: { requiredSymbols: 3, awardedSpins: 7 } },
      isActive: false,
      label: 'Relic Run',
      specialRound,
    }))

    expect(markup).toContain('data-feature-state="collecting"')
    expect(markup).toContain('Mirror relics: 12 of 20')
    expect(markup).toContain('Trigger')
    expect(markup).toContain('20 routes begin on reel 1')
    expect(markup).toContain('Reward')
    expect(markup).toContain('width:60%')
  })

  it('announces remaining spins while a feature is active', () => {
    const markup = renderToStaticMarkup(createElement(SlotFeatureStatus, {
      collections,
      collectionStates: [],
      freeSpinsRemaining: 4,
      help: { paylineCount: 20 },
      isActive: true,
      label: 'Mirror Run',
      specialRound,
    }))

    expect(markup).toContain('data-feature-state="active"')
    expect(markup).toContain('4 feature spins remaining')
    expect(markup).toContain('Press Spin to skip the pause')
  })
})
