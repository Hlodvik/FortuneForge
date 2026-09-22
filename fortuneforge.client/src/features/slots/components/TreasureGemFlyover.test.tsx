import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { TreasureGemFlyover } from './TreasureGemFlyover'

describe('TreasureGemFlyover', () => {
  it('sends a staggered cascade of matching gems into the selected chest', () => {
    const markup = renderToStaticMarkup(
      <TreasureGemFlyover
        image="/ruby.png"
        flyover={{
          id: 12,
          collectionId: 'sync',
          left: 50,
          top: 100,
          width: 80,
          height: 80,
          travelX: 20,
          travelY: -240,
          durationMs: 720,
          chestDropHeight: 34,
        }}
      />,
    )

    expect(markup.match(/slots-page__seal-flyover--gem-hoard/g)).toHaveLength(7)
    expect(markup.match(/src="\/ruby.png"/g)).toHaveLength(7)
    expect(markup).toContain('animation-duration:468ms')
    expect(markup).toContain('animation-delay:252ms')
    expect(markup).toContain('--treasure-gem-drop-height:34px')
  })
})
