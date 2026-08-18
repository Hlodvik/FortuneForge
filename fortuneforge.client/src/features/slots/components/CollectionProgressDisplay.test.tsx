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
})
