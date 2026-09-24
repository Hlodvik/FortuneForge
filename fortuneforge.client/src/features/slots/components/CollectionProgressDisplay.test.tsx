import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CollectionProgressDisplay } from './CollectionProgressDisplay'

describe('CollectionProgressDisplay', () => {
  it('renders Wukong collection progress as a lightweight celestial orbit', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'sync', count: 20, averageWagerPoints: 50, requiredCount: 40 }}
        definition={{ id: 'sync', label: 'Synced reels', shortLabel: 'Sync', symbol: 'SEAL_SYNC', requiredCount: 40 }}
        image="/seal.png"
        isImpacting={false}
        itemLabel="seals"
        presentation="celestial-orbit"
      />,
    )

    expect(markup).toContain('slots-page__collection-orbit')
    expect(markup.match(/class="is-lit"/g)).toHaveLength(5)
    expect(markup).toContain('--collection-progress:50%')
    expect(markup).not.toContain('slots-page__collection-piece')
  })

  it('selects the authored Pirate chest art for each collection-progress band', () => {
    const fillImages = Array.from({ length: 4 }, (_, index) => `/ruby-level-${index + 1}.png`)
    const cases = [
      [0, '/ruby-empty.png'],
      [1, '/ruby-level-1.png'],
      [3, '/ruby-level-1.png'],
      [4, '/ruby-level-2.png'],
      [7, '/ruby-level-2.png'],
      [8, '/ruby-level-3.png'],
      [11, '/ruby-level-3.png'],
      [12, '/ruby-level-4.png'],
      [15, '/ruby-level-4.png'],
      [30, '/ruby-level-4.png'],
    ] as const

    for (const [count, expectedImage] of cases) {
      const markup = renderToStaticMarkup(
        <CollectionProgressDisplay
          collection={{ sealId: 'sync', count, averageWagerPoints: 50, requiredCount: 15 }}
          definition={{ id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 15 }}
          image="/ruby.png"
          isImpacting
          itemLabel="gems"
          containerImage="/ruby-empty.png"
          containerFillImages={fillImages}
          presentation="gem-hoard"
        />,
      )

      expect(markup).toContain('slots-page__treasure-chest--impact')
      expect(markup).toContain(`src="${expectedImage}"`)
      expect(markup).not.toContain('slots-page__treasure-chest-gems')
      expect(markup).toContain('draggable="false"')
    }
  })

  it('can hide the numeric progress beside a Pirate chest without hiding its accessible progress value', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'sync', count: 9, averageWagerPoints: 50, requiredCount: 15 }}
        definition={{
          id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 15,
          rewardDescription: 'Fill this chest to launch 10 free games. During every free game, one reel is copied to match the winning setup.',
        }}
        image="/ruby.png"
        isImpacting={false}
        itemLabel="gems"
        containerImage="/ruby-empty.png"
        presentation="gem-hoard"
        showCount={false}
      />,
    )

    expect(markup).not.toContain('slots-page__collection-count')
    expect(markup).toContain('aria-valuenow="9"')
    expect(markup).not.toContain('title=')
    expect(markup).toContain('Collection reward')
    expect(markup).toContain('Land Ruby gems anywhere on the reels to bank them.')
    expect(markup).toContain('Fill this chest to launch 10 free games.')
    expect(markup).toContain('During every free game, one reel is copied to match the winning setup.')
    expect(markup).toContain('tabindex="0"')
  })

  it('uses compact cabinet copy without replacing the full accessible collection label', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'paw', count: 4, averageWagerPoints: 50, requiredCount: 15 }}
        definition={{
          id: 'paw',
          label: 'Stronger purse hauls',
          displayLabel: 'Purse Hauls',
          shortLabel: 'Orange',
          symbol: 'SEAL_PAW',
          requiredCount: 15,
        }}
        image="/orange.png"
        isImpacting={false}
        itemLabel="gems"
        presentation="gem-hoard"
      />,
    )

    expect(markup).toContain('aria-label="Stronger purse hauls:')
    expect(markup).toContain('aria-hidden="true">Purse Hauls</strong>')
  })

  it('keeps a completed chest visually full while showing special-game gems separately', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'sync', count: 3, averageWagerPoints: 50, requiredCount: 15 }}
        definition={{ id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 15 }}
        image="/ruby.png"
        isCelebrating
        isImpacting={false}
        itemLabel="gems"
        containerImage="/ruby-empty.png"
        containerFillImages={Array.from({ length: 4 }, (_, index) => `/ruby-level-${index + 1}.png`)}
        displayCount={15}
        presentation="gem-hoard"
        showCount={false}
        statusDetail="3 gems banked"
      />,
    )

    expect(markup).toContain('slots-page__seal-collection--celebrating')
    expect(markup).toContain('src="/ruby-level-4.png"')
    expect(markup).not.toContain('slots-page__treasure-chest-gems')
    expect(markup).toContain('3 gems banked')
    expect(markup).toContain('aria-valuenow="3"')
  })
})
