import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { TexasHoldemPage } from './TexasHoldemPage'

describe('TexasHoldemPage account neutrality', () => {
  it('uses practice stacks and public exits on the demo card-room route', () => {
    const markup = renderToStaticMarkup(createElement(TexasHoldemPage, {
      playerName: 'Ada',
    }))

    expect(markup).toContain('Practice chips · no account wagering')
    expect(markup).toContain('Account-neutral practice table')
    expect(markup).toContain('data-game-navbar="true"')
    expect(markup).toContain('href="/demo"')
    expect(markup).not.toContain('holdem-header"')
    expect(markup).not.toContain('holdem-brand')
    expect(markup).toContain('Ada')
    expect(markup).not.toContain('Account balance')
    expect(markup).not.toContain('ForgeCreditAmount')
  })
})
