// Edited: 2026-11-18T22:48:14-06:00 | Computer: Vinnabok
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { RouteStateLayout } from './RouteStateLayout'

describe('pending route navigation', () => {
  it.each(['/home', '/home/settings', '/home/history', '/balance', '/admin'])('keeps %s public until its account is verified', pathname => {
    const markup = renderToStaticMarkup(<RouteStateLayout pathname={pathname}><main>Loading…</main></RouteStateLayout>)
    expect(markup).toContain('site-navbar--public')
    expect(markup).toContain('href="/login"')
    expect(markup).not.toContain('site-navbar--account')
    expect(markup).not.toContain('home-lobby__sidebar')
  })

  it('preserves restricted navigation for account review', () => {
    const markup = renderToStaticMarkup(<RouteStateLayout pathname="/account/review"><main>Loading…</main></RouteStateLayout>)
    expect(markup).toContain('site-navbar--restricted')
    expect(markup).not.toContain('site-navbar--public')
  })
})
