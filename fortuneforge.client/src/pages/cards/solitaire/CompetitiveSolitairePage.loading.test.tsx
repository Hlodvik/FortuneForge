// @vitest-environment jsdom
import { act, type ComponentProps } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { resolveGameLoadingEntry } from '../../../features/game-loading/gameLoadingRegistry'
import { SolitaireRequestError } from '../../../games/cards/solitaire/solitaireApi'
import { createLocalSolitaireGame } from '../../../games/cards/solitaire/solitaireEngine'
import type { SolitaireSession } from '../../../games/cards/solitaire/solitaireTypes'

const reads = vi.hoisted(() => ({ session: vi.fn() }))
vi.mock('../../../games/cards/solitaire/solitaireApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../games/cards/solitaire/solitaireApi')>(),
  getSolitaireSession: reads.session,
}))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ useCardAudioClick: () => () => undefined }))

import { CompetitiveSolitairePage, SolitaireContent } from './CompetitiveSolitairePage'

const account: AccountSummary = {
  userId: 'solitaire-loading-test', playerName: 'Player', email: 'player@example.test', role: 'player',
  createdAtUtc: '2026-10-26T00:00:00Z', balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void
  const promise = new Promise<T>((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}
let host: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  reads.session.mockReset().mockResolvedValue({ kind: 'idle' })
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(async () => { root.render(<CompetitiveSolitairePage account={account} />) })
const assertNavigation = () => {
  expect(host.querySelector('[data-game-navbar] a[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
  expect(host.querySelector('[data-game-navbar] a.player-shell-header__games')?.getAttribute('href')).toBe('/home#home-lobby-games-title')
  expect(host.querySelectorAll('[data-game-navbar]')).toHaveLength(1)
}

describe('Solitaire initial table loading', () => {
  it('shows themed artwork in the existing shell only while the initial session read is pending', async () => {
    const session = deferred<SolitaireSession>()
    reads.session.mockReturnValueOnce(session.promise)
    await render()
    expect(host.querySelector('[data-game-loading="solitaire"] img')?.getAttribute('src')).toBe(resolveGameLoadingEntry('/cards/solitaire')!.image)
    expect(host.querySelector('[data-game-loading]')?.closest('.in-game-shell__body.solitaire-shell-body')).not.toBeNull()
    expect(host.querySelector('[data-game-loading]')?.parentElement?.className).toBe('solitaire-page')
    expect(host.querySelector('.solitaire-lobby')).toBeNull()
    assertNavigation()
    await act(async () => { session.resolve({ kind: 'idle' }) })
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.querySelector('.solitaire-lobby')).not.toBeNull()
    expect(host.textContent).toContain('Competitive · R5')
    expect(host.textContent).toContain('Play free')
    assertNavigation()
  })

  it('keeps offline details, free-play and retry controls visible rather than leaving artwork over an error', async () => {
    const initial = deferred<SolitaireSession>(), retry = deferred<SolitaireSession>()
    reads.session.mockReturnValueOnce(initial.promise).mockReturnValueOnce(retry.promise)
    await render()
    await act(async () => { initial.reject(new Error('Offline')) })
    const alert = host.querySelector('.solitaire-state[role="alert"]')!
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(alert.textContent).toContain('Competitive play is offline.')
    expect(alert.textContent).toContain('Competitive Solitaire could not connect. No buy-in was accepted.')
    expect(alert.textContent).toContain('Play free Solitaire')
    assertNavigation()
    await act(async () => [...alert.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === 'Try again')!.click())
    expect(reads.session).toHaveBeenCalledTimes(2)
    expect(host.querySelector('[data-game-loading="solitaire"]')).not.toBeNull()
    await act(async () => { retry.resolve({ kind: 'idle' }) })
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.querySelector('.solitaire-lobby')).not.toBeNull()
  })

  it('keeps disabled competitive play and its free-play exit rather than displaying a spinner', async () => {
    reads.session.mockRejectedValueOnce(new SolitaireRequestError('No buy-ins during maintenance.', 503, { code: 'competitive-solitaire-disabled' }))
    await render()
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.textContent).toContain('No buy-ins are being accepted.')
    expect(host.textContent).toContain('No buy-ins during maintenance.')
    expect(host.textContent).toContain('Play free Solitaire')
    assertNavigation()
  })

  it('keeps an active free board ahead of a pending competitive availability read', () => {
    const props: ComponentProps<typeof SolitaireContent> = {
      availability: { kind: 'loading' }, balanceCredits: 100, busy: false, pending: null,
      playerCount: 4, buyInCredits: 5, drawCount: 3, freeGame: createLocalSolitaireGame(42, 3),
      freePaused: false, freePauseBusy: false, freeComplete: false, freeAutoWinning: false,
      freeSubmitting: false, freeSubmissionError: null, freeSetupOpen: false,
      competitiveSetupMatchId: null, freeElapsedMilliseconds: 0, freeCanUndo: false,
      onPlayerCountChange: vi.fn(), onBuyInChange: vi.fn(), onDrawCountChange: vi.fn(),
      onJoin: vi.fn(), onCancel: vi.fn(), onCommand: vi.fn(), onCloseCompleted: vi.fn(),
      onNewCompetitive: vi.fn(), onChooseNewCompetitive: vi.fn(), onCancelCompetitiveSetup: vi.fn(),
      onClaim: vi.fn(), onStartFree: vi.fn(), onReplayFree: vi.fn(), onChooseNewFreeGame: vi.fn(),
      onCancelFreeSetup: vi.fn(), onFreeCommand: vi.fn(), onFreePause: vi.fn(), onFreeUndo: vi.fn(),
      onFreeSubmit: vi.fn(), onExitFree: vi.fn(), onRefresh: vi.fn(),
    }
    act(() => root.render(<SolitaireContent {...props} />))
    expect(host.querySelector('.solitaire-match--free .solitaire-board')).not.toBeNull()
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.textContent).toContain('Submit game')
  })
})
