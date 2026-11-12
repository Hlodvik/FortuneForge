import { createElement, type ComponentType } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import appRoutesSource from '../AppRoutes.tsx?raw'
import { pageTitleForPath } from '../usePageTitle'

const state = vi.hoisted(() => ({ requestedPath: '', releaseId: '' }))

vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: (path: string) => {
    state.requestedPath = path
    return { account: { userId: 'player-1' }, error: null, isLoading: false, reload: () => undefined }
  },
}))
vi.mock('../../pages/cards/solitaire/CompetitiveSolitairePage', () => ({
  FreeSolitairePage: ({ account }: { account: { userId: string } }) => <main>Free Solitaire for {account.userId}</main>,
}))

vi.mock('../../features/game-releases/useReleasedGame', () => ({
  useReleasedGame: (releaseId: string, _title: string, Game: ComponentType<unknown>) => {
    state.releaseId = releaseId
    return { Game, pending: null }
  },
}))

import { AuthenticatedFreeSolitaireRoute } from './FreeSolitaireRoute'

describe('free Solitaire route', () => {
  it('opens the single-player page under its own authenticated URL while preserving the competitive URL', () => {
    const markup = renderToStaticMarkup(createElement(AuthenticatedFreeSolitaireRoute))
    expect(state.requestedPath).toBe('/cards/free-solitaire')
    expect(state.releaseId).toBe('free-solitaire')
    expect(markup).toContain('Free Solitaire for player-1')
    expect(appRoutesSource).toContain("pathname === '/cards/free-solitaire'")
    expect(appRoutesSource).toContain("pathname === '/cards/solitaire'")
    expect(pageTitleForPath('/cards/free-solitaire')).toBe('Solitaire — Fortune Forge')
    expect(pageTitleForPath('/cards/solitaire')).toBe('Competitive Solitaire — Fortune Forge')
  })
})
