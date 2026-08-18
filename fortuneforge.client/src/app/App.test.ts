import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => vi.unstubAllGlobals())

describe('app shell slot media isolation', () => {
  it('mounts the cloud video only for a slot route that requests it', () => {
    expect(renderApp('/slots/wukong')).toContain('app-shell__background-video')
    expect(renderApp('/slots/rainbow-realm')).not.toContain('app-shell__background-video')
    expect(renderApp('/cards/texas-holdem')).not.toContain('app-shell__background-video')
    expect(renderApp('/account')).not.toContain('app-shell__background-video')
    expect(renderApp('/')).not.toContain('app-shell__background-video')
  })
})

function renderApp(pathname: string): string {
  vi.stubGlobal('window', { location: { pathname } })
  vi.stubGlobal('document', { visibilityState: 'visible' })
  return renderToStaticMarkup(createElement(App))
}
