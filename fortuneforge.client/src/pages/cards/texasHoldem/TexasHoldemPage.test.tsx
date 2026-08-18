import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { TexasHoldemPage } from './TexasHoldemPage'

describe('TexasHoldemPage account neutrality', () => {
  it('uses practice stacks even on the authenticated card-room route', () => {
    const markup = renderToStaticMarkup(createElement(TexasHoldemPage, {
      demoMode: false,
      playerName: 'Ada',
      returnHref: '/cards',
    }))

    expect(markup).toContain('Practice chips · no account wagering')
    expect(markup).toContain('Account-neutral practice table')
    expect(markup).toContain('href="/cards"')
    expect(markup).toContain('Ada')
    expect(markup).not.toContain('Account balance')
    expect(markup).not.toContain('ForgeCreditAmount')
  })
})
