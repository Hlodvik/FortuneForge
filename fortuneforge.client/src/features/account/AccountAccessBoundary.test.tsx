// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
const mocks = vi.hoisted(() => ({ fetch: vi.fn(), logout: vi.fn(), account: vi.fn() }))
vi.mock('./services/accountsApi', () => ({ fetchWithAccountSession: mocks.fetch, logoutAccount: mocks.logout, getCurrentAccount: mocks.account }))
import { AccountAccessBoundary } from './AccountAccessBoundary'
import { AccountReviewPage } from '../../pages/account/AccountReviewPage'
import { clearAccountRestriction, publishAccountRestriction } from './accountRestrictions'

let root: Root
let host: HTMLDivElement
const review = (restricted = true) => new Response(JSON.stringify({ accountRestricted: restricted, reviewStatus: restricted ? 'pending' : 'clear', message: restricted ? 'Our team is reviewing your account.' : 'Your account review is complete.' }), { status: 200 })
const flaggedIncident = (resolved = false) => ({ id: 'incident-1', gameId: 'texas-holdem', severity: 'critical', reason: 'A submitted move used cards that were not in your dealt hand.', status: resolved ? 'resolved' : 'open', createdAtUtc: '2026-10-26T12:00:00Z', resolvedAtUtc: resolved ? '2026-10-26T13:00:00Z' : null, resolutionOutcome: resolved ? 'upheld' : null, resolutionReason: resolved ? 'The review confirmed an invalid card submission from this account.' : null })
const detailedReview = (resolved = false) => new Response(JSON.stringify({ accountRestricted: true, reviewStatus: resolved ? 'confirmed' : 'pending', message: resolved ? 'Your account restriction was upheld after review.' : 'Your account is awaiting an admin review.', incidents: [{ ...flaggedIncident(resolved), evidenceIds: ['PRIVATE EVIDENCE ID'], payloadJson: '{"cards":"PRIVATE CARDS"}' }], balance: 'PRIVATE BALANCE R123' }), { status: 200 })
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); clearAccountRestriction()
  mocks.fetch.mockReset().mockImplementation(async () => review()); mocks.account.mockReset(); mocks.logout.mockReset().mockResolvedValue(undefined)
  host = document.createElement('div'); host.className = 'app-shell'; document.body.append(host); root = createRoot(host)
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); clearAccountRestriction(); vi.unstubAllGlobals() })
async function mount() { await act(async () => root.render(<AccountAccessBoundary><p>PRIVATE BALANCE R999</p><button>Play game</button></AccountAccessBoundary>)) }

describe('account access presentation', () => {
  it('replaces all account and game content with review-only status on a full lock', async () => {
    await mount(); expect(host.textContent).toContain('PRIVATE BALANCE')
    await act(async () => publishAccountRestriction({ code: 'account_restricted', message: 'Review pending.' }))
    expect(host.textContent).not.toContain('PRIVATE BALANCE'); expect(host.textContent).not.toContain('Play game')
    expect(host.textContent).toContain('Our team is reviewing your account.')
    expect(mocks.account).not.toHaveBeenCalled()
    expect(mocks.fetch).toHaveBeenCalledWith('/api/account/review', expect.objectContaining({ method: 'GET', cache: 'no-store' }))
    expect(host.querySelector('a[href="/home"]')).toBeNull()
  })
  it('keeps balances hidden when review status fails and returns home only after an explicit clear result', async () => {
    mocks.fetch.mockResolvedValueOnce(new Response('{}', { status: 503 }))
    publishAccountRestriction({ code: 'account_restricted', message: 'Review pending.' })
    await mount(); expect(host.textContent).not.toContain('PRIVATE BALANCE'); expect(host.querySelector('[role="alert"]')).not.toBeNull()
    mocks.fetch.mockImplementation(async () => review(false))
    await act(async () => host.querySelector<HTMLButtonElement>('button')!.click())
    expect(host.textContent).toContain('Review complete'); expect(host.querySelector('a[href="/home"]')).not.toBeNull()
    expect(mocks.account).not.toHaveBeenCalled()
  })
  it('opens a focused dismissible notice only after a game or withdrawal action was rejected', async () => {
    await mount(); expect(document.querySelector('[role="alertdialog"]')).toBeNull()
    await act(async () => publishAccountRestriction({ code: 'game_restricted', message: 'This game is pending review.', gameId: 'mines' }))
    const dialog = document.querySelector<HTMLElement>('[role="alertdialog"]')!
    expect(dialog.textContent).toContain('This game is pending review.')
    expect(host.hasAttribute('inert')).toBe(true); expect(dialog.contains(document.activeElement)).toBe(true)
    await act(async () => dialog.querySelector<HTMLButtonElement>('button')!.click())
    expect(document.querySelector('[role="alertdialog"]')).toBeNull(); expect(host.hasAttribute('inert')).toBe(false)
    expect(host.textContent).toContain('PRIVATE BALANCE')
    await act(async () => publishAccountRestriction({ code: 'withdrawals_restricted', message: 'Withdrawals pending review.' }))
    expect(document.querySelector('[role="alertdialog"]')?.textContent).toContain('Withdrawals under review')
    expect(mocks.fetch).not.toHaveBeenCalled()
  })
  it('supports direct navigation to the review route without loading account details', async () => {
    await act(async () => root.render(<AccountReviewPage />))
    expect(host.textContent).toContain('Account review'); expect(mocks.account).not.toHaveBeenCalled()
    expect(mocks.fetch).toHaveBeenCalledTimes(1)
  })
  it('explains an open incident without exposing private evidence or treating it as a decision', async () => {
    mocks.fetch.mockResolvedValueOnce(detailedReview())
    publishAccountRestriction({ code: 'account_restricted', message: 'Review pending.' })
    await mount()
    const incident = host.querySelector('article')!
    expect(incident.getAttribute('aria-labelledby')).toBe(incident.querySelector('h3')?.id)
    expect(incident.textContent).toContain('Texas Hold’em')
    expect(incident.textContent).toContain('Critical')
    expect(incident.textContent).toContain(flaggedIncident().reason)
    expect(incident.textContent).toContain('Pending admin review')
    expect(incident.textContent).toContain('No decision has been made')
    expect(incident.querySelector('time')?.dateTime).toBe('2026-10-26T12:00:00Z')
    expect(incident.textContent).not.toContain('Restriction upheld')
    expect(host.textContent).not.toContain('PRIVATE')
    expect(host.querySelector('a[href="/home"]')).toBeNull()
    expect(mocks.account).not.toHaveBeenCalled()
  })
  it('shows an upheld admin decision and its date, then removes incident details only after a clear response', async () => {
    mocks.fetch.mockResolvedValueOnce(detailedReview(true))
    await act(async () => root.render(<AccountReviewPage />))
    expect(host.querySelector('h1')?.textContent).toBe('Review decision')
    expect(host.textContent).toContain('Restriction upheld')
    expect(host.textContent).toContain(flaggedIncident(true).resolutionReason)
    expect(host.querySelector('time[datetime="2026-10-26T13:00:00Z"]')).not.toBeNull()
    expect(host.textContent).not.toContain('Pending admin review')
    expect(host.querySelector('a[href="/home"]')).toBeNull()
    // Even if a response mistakenly includes a withdrawal-only incident, a clear
    // account must not learn about it before attempting a withdrawal.
    mocks.fetch.mockResolvedValueOnce(new Response(JSON.stringify({ accountRestricted: false, reviewStatus: 'clear', message: 'Your account review is complete.', incidents: [{ ...flaggedIncident(), severity: 'high', reason: 'WITHDRAWAL ONLY FLAG' }] }), { status: 200 }))
    await act(async () => host.querySelector<HTMLButtonElement>('button')!.click())
    expect(host.querySelector('h1')?.textContent).toBe('Review complete')
    expect(host.querySelector('article')).toBeNull()
    expect(host.textContent).not.toContain('WITHDRAWAL ONLY FLAG')
    expect(host.querySelector('a[href="/home"]')).not.toBeNull()
    expect(mocks.account).not.toHaveBeenCalled()
  })
  it('keeps restricted access closed for a contradictory review response', async () => {
    mocks.fetch.mockResolvedValueOnce(new Response(JSON.stringify({ accountRestricted: false, reviewStatus: 'confirmed', message: 'Inconsistent state.', incidents: [flaggedIncident(true)] }), { status: 200 }))
    publishAccountRestriction({ code: 'account_restricted', message: 'Review pending.' })
    await mount()
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded')
    expect(host.querySelector('a[href="/home"]')).toBeNull()
    expect(host.textContent).not.toContain('PRIVATE BALANCE')
    expect(mocks.account).not.toHaveBeenCalled()
  })
  it('supports an incomplete incident listing without claiming that every incident was confirmed', async () => {
    mocks.fetch.mockResolvedValueOnce(new Response(JSON.stringify({ accountRestricted: true, reviewStatus: 'restricted', message: 'Your account remains restricted while review details are being checked.', incidents: [flaggedIncident()] }), { status: 200 }))
    await act(async () => root.render(<AccountReviewPage />))
    expect(host.querySelector('[role="alert"]')).toBeNull()
    expect(host.querySelector('h1')?.textContent).toBe('Account review')
    expect(host.textContent).toContain('review details are being checked')
    expect(host.textContent).toContain('Pending admin review')
    expect(host.querySelector('a[href="/home"]')).toBeNull()
  })
})
