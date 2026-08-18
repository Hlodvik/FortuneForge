import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { BlackjackPage } from './BlackjackPage'

describe('BlackjackPage', () => {
  it('renders honest rules and keeps Deal disabled before availability succeeds', () => {
    const markup = renderToStaticMarkup(createElement(BlackjackPage, { demoMode: true }))

    expect(markup).toContain('<h1 id="blackjack-title">Blackjack</h1>')
    expect(markup).toContain('Dealer stands on all 17s')
    expect(markup).toContain('Blackjack pays 3:2')
    expect(markup).toContain('No split or insurance')
    expect(markup).toContain('All cards and outcomes come from the Fortune Forge API')
    expect(markup).toContain('disabled=""')
    expect(markup).toContain('Checking the table…')
  })
})
