import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CardRoomHeader } from './CardRoomHeader'

describe('CardRoomHeader', () => {
  it('uses icon navigation and keeps account identity at the top right', () => {
    const markup = renderToStaticMarkup(
      <CardRoomHeader
        playerName="RiverFox"
        balanceCredits={125.5}
        unseenCount={2}
        historyContent={<p>History content</p>}
      />,
    )

    expect(markup).toContain('aria-label="Fortune Forge home"')
    expect(markup).toContain('aria-label="Game history, 2 new"')
    expect(markup).toContain('RiverFox')
    expect(markup).toContain('aria-label="125.5 South African rand"')
    expect(markup).not.toContain('← Card room')
  })
})
