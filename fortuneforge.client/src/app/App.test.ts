import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => vi.unstubAllGlobals())

describe('app shell slot media isolation', () => {
  it('mounts the cloud video only for a slot route that requests it', () => {
    expect(renderApp('/slots/wukong')).not.toContain('app-shell__background-video')
    expect(renderApp('/slots/rainbow-realm')).not.toContain('app-shell__background-video')
    expect(renderApp('/cards/texas-holdem')).not.toContain('app-shell__background-video')
    expect(renderApp('/account')).not.toContain('app-shell__background-video')
    expect(renderApp('/')).not.toContain('app-shell__background-video')
    expect(renderApp('/')).toContain('app-shell--landing')
  })

  it('keeps Wukong and Nimbus artwork out of the home page', () => {
    const markup = renderApp('/')

    expect(markup).not.toContain('Wukong')
    expect(markup).not.toContain('Nimbus')
    expect(markup).not.toContain('mascot-companion')
  })

  it('keeps the Asteroids route root above the shell cloud backdrop', () => {
    const shellStyles = readFileSync(new URL('./styles/shell.css', import.meta.url), 'utf8')

    expect(shellStyles).toContain('.app-shell > .asteroids-competition-page,')
  })

  it('marks playable routes without constraining game libraries or account pages', () => {
    expect(renderApp('/slots/pirates-fortune/demo')).toContain('app-shell--game')
    expect(renderApp('/games/keno')).toContain('app-shell--game')
    expect(renderApp('/cards/blackjack')).toContain('app-shell--game')
    expect(renderApp('/demo/cards/blackjack')).toContain('app-shell--game')

    expect(renderApp('/games')).not.toContain('app-shell--game')
    expect(renderApp('/cards')).not.toContain('app-shell--game')
    expect(renderApp('/demo/cards')).not.toContain('app-shell--game')
    expect(renderApp('/home')).not.toContain('app-shell--game')
  })

})

function renderApp(pathname: string): string {
  vi.stubGlobal('window', { location: { pathname } })
  vi.stubGlobal('document', { visibilityState: 'visible' })
  return renderToStaticMarkup(createElement(App))
}
