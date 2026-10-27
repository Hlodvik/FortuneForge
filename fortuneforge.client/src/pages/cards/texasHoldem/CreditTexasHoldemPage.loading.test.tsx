// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { resolveGameLoadingEntry } from '../../../features/game-loading/gameLoadingRegistry'
import { CreditHoldemRequestError, type CreditHoldemSession, type CreditHoldemStatus } from '../../../games/cards/texasHoldem/creditHoldemApi'

const reads = vi.hoisted(() => ({ status: vi.fn(), session: vi.fn() }))
vi.mock('../../../games/cards/texasHoldem/creditHoldemApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../games/cards/texasHoldem/creditHoldemApi')>(),
  getCreditHoldemStatus: reads.status,
  getCreditHoldemSession: reads.session,
}))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ useCardAudioClick: () => () => undefined }))

import { CreditTexasHoldemPage } from './CreditTexasHoldemPage'

const account: AccountSummary = {
  userId: 'holdem-loading-test', playerName: 'Player', email: 'player@example.test', role: 'player',
  createdAtUtc: '2026-10-26T00:00:00Z', balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
const status: CreditHoldemStatus = {
  available: true, minimumStartPlayers: 3, maximumSeats: 5, minimumRealPlayers: 2,
  smallBlindCredits: .5, bigBlindCredits: 1, actionDeadlineSeconds: 30, matchDeadlineSeconds: 300,
}
const idle: CreditHoldemSession = { contractVersion: 'cards.texas-holdem.credit.v2', kind: 'idle', version: 1 }
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void
  const promise = new Promise<T>((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}
let host: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  reads.status.mockReset().mockResolvedValue(status); reads.session.mockReset().mockResolvedValue(idle)
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(async () => { root.render(<CreditTexasHoldemPage account={account} />) })
const assertNavigation = () => {
  expect(host.querySelector('[data-game-navbar] a[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
  expect(host.querySelector('[data-game-navbar] a.player-shell-header__games')?.getAttribute('href')).toBe('/home#home-lobby-games-title')
  expect(host.querySelectorAll('[data-game-navbar]')).toHaveLength(1)
}

describe('Hold’em initial table loading', () => {
  it('keeps themed artwork and navigation until both real reads finish, then immediately opens the buy-in lobby', async () => {
    const tableStatus = deferred<CreditHoldemStatus>(), session = deferred<CreditHoldemSession>()
    reads.status.mockReturnValueOnce(tableStatus.promise); reads.session.mockReturnValueOnce(session.promise)
    await render()
    expect(host.querySelector('[data-game-loading="texas-holdem"] img')?.getAttribute('src')).toBe(resolveGameLoadingEntry('/cards/texas-holdem')!.image)
    expect(host.querySelector('[data-game-loading]')?.closest('.in-game-shell__body.ff-holdem-shell-body')).not.toBeNull()
    expect(host.querySelector('[data-game-loading]')?.parentElement?.className).toBe('ff-holdem-page')
    expect(host.querySelector('.ff-holdem-lobby')).toBeNull()
    assertNavigation()
    await act(async () => { tableStatus.resolve(status) })
    expect(host.querySelector('[data-game-loading]')).not.toBeNull()
    await act(async () => { session.resolve(idle) })
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.querySelector('[aria-label="Choose a Texas Hold’em table"]')).not.toBeNull()
    expect(host.textContent).toContain('Find your seat at the table.')
    assertNavigation()
  })

  it('retains table errors and retry, using the loading screen only for the new pending retry', async () => {
    const initial = deferred<CreditHoldemStatus>(), retry = deferred<CreditHoldemStatus>()
    reads.status.mockReturnValueOnce(initial.promise).mockReturnValueOnce(retry.promise)
    await render()
    await act(async () => { initial.reject(new Error('Offline')) })
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.textContent).toContain('Table unavailable')
    expect(host.textContent).toContain('Hold’em could not reach the live table. No action was accepted.')
    assertNavigation()
    const retryButton = [...host.querySelectorAll<HTMLButtonElement>('button')].find(button => /try again/i.test(button.textContent ?? ''))!
    await act(async () => retryButton.click())
    expect(reads.status).toHaveBeenCalledTimes(2)
    expect(host.querySelector('[data-game-loading="texas-holdem"]')).not.toBeNull()
    await act(async () => { retry.resolve(status) })
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.querySelector('.ff-holdem-lobby')).not.toBeNull()
  })

  it('does not disguise a disabled table as perpetual loading', async () => {
    reads.status.mockRejectedValueOnce(new CreditHoldemRequestError('The table is disabled for review.', 503, { code: 'credit-texas-holdem-disabled' }))
    await render()
    expect(host.querySelector('[data-game-loading]')).toBeNull()
    expect(host.textContent).toContain('Credit Hold’em is coming soon')
    expect(host.textContent).toContain('The table is disabled for review.')
    assertNavigation()
  })
})
