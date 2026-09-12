import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CardOutcomeSummary } from './CardOutcomeSummary'

describe('CardOutcomeSummary', () => {
  it('presents a complete, polite result with the next action', () => {
    const markup = renderToStaticMarkup(createElement(CardOutcomeSummary, {
      tone: 'positive',
      eyebrow: 'Winning hand',
      title: 'You win with a straight.',
      detail: 'Payout R80.00',
      nextAction: 'Deal the next hand when ready.',
    }))

    expect(markup).toContain('class="ff-card-outcome ff-card-outcome--positive"')
    expect(markup).toContain('role="status"')
    expect(markup).toContain('aria-atomic="true"')
    expect(markup).toContain('Payout R80.00')
    expect(markup).toContain('<b>Next:</b> Deal the next hand when ready.')
  })
})
