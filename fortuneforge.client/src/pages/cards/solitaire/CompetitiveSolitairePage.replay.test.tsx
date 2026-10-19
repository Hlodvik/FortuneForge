// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { dismissSolitaireResult, getSolitaireSession } from '../../../games/cards/solitaire/solitaireApi'
import { createLocalSolitaireGame } from '../../../games/cards/solitaire/solitaireEngine'
import type { SolitaireMatchSession, SolitairePlayerStatus } from '../../../games/cards/solitaire/solitaireTypes'
import {
  completeSolitaireFreeRun,
  setSolitaireFreeRunPaused,
  startSolitaireFreeRun,
  type SolitaireFreeRun,
} from '../../../games/cards/solitaire/solitaireFreeRunApi'
import { CompetitiveSolitairePage, SolitaireHistoryPanel } from './CompetitiveSolitairePage'

vi.mock('../../../games/cards/solitaire/solitaireApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../../games/cards/solitaire/solitaireApi')>(),
  getSolitaireSession: vi.fn(),
  dismissSolitaireResult: vi.fn(),
}))
vi.mock('../../../games/cards/solitaire/solitaireFreeRunApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../../games/cards/solitaire/solitaireFreeRunApi')>(),
  startSolitaireFreeRun: vi.fn(),
  completeSolitaireFreeRun: vi.fn(),
  setSolitaireFreeRunPaused: vi.fn(),
}))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ useCardAudioClick: () => () => undefined }))

const startedAtUtc = '2026-10-16T12:00:00Z'
const firstRun: SolitaireFreeRun = {
  runId: 'solitaire_free_first', seed: 42, drawCount: 3, startedAtUtc, wasAlreadyStarted: false,
}
const replayRun: SolitaireFreeRun = {
  runId: 'solitaire_free_replay', seed: firstRun.seed, drawCount: 3, startedAtUtc, wasAlreadyStarted: false,
}
const newRun: SolitaireFreeRun = {
  runId: 'solitaire_free_new', seed: 99, drawCount: 3, startedAtUtc, wasAlreadyStarted: false,
}
const account: AccountSummary = {
  userId: 'solitaire-player', playerName: 'Player', email: 'player@example.test',
  role: 'Player', createdAtUtc: startedAtUtc,
  balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}

let host: HTMLDivElement
let root: Root | null

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.mocked(getSolitaireSession).mockReset().mockResolvedValue({ kind: 'idle' })
  vi.mocked(dismissSolitaireResult).mockReset().mockResolvedValue({ session: { kind: 'idle' }, balanceCredits: 100 })
  vi.mocked(startSolitaireFreeRun).mockReset()
    .mockResolvedValueOnce(firstRun).mockResolvedValueOnce(replayRun).mockResolvedValueOnce(newRun)
  vi.mocked(completeSolitaireFreeRun).mockReset().mockImplementation(async (runId) => ({
    runId, seed: firstRun.seed, drawCount: 3, score: 0, moves: 0,
    elapsedMilliseconds: 1_000, terminal: 'submitted', completedAtUtc: startedAtUtc,
    wasAlreadyCompleted: false,
  }))
  vi.mocked(setSolitaireFreeRunPaused).mockReset().mockImplementation(async (runId, pause) => ({
    runId, isPaused: pause, elapsedMilliseconds: 2_000,
  }))
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(async () => {
  if (root !== null) await act(async () => root!.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

function button(label: string): HTMLButtonElement {
  const found = [...host.querySelectorAll<HTMLButtonElement>('button')]
    .find((candidate) => candidate.textContent?.trim() === label)
  if (found === undefined) throw new Error(`Missing button ${label}`)
  return found
}

async function click(label: string): Promise<void> {
  await act(async () => button(label).click())
}

function tableauLayout(): string[] {
  return [...host.querySelectorAll<HTMLElement>('.solitaire-board [data-solitaire-card-id]')]
    .map((card) => card.dataset.solitaireCardId ?? '')
}

describe('free Solitaire Replay', () => {
  it('creates a new run from the original layout while New game starts independently', async () => {
    await act(async () => root!.render(<CompetitiveSolitairePage account={account} />))
    await click('Play free')
    const originalLayout = tableauLayout()
    expect(originalLayout.length).toBeGreaterThan(0)
    expect(vi.mocked(startSolitaireFreeRun).mock.calls[0]).toEqual([3, expect.any(String), undefined])

    await click('Submit game')
    await click('Replay')
    expect(vi.mocked(startSolitaireFreeRun).mock.calls[1]).toEqual([3, expect.any(String), firstRun.runId])
    expect(replayRun.runId).not.toBe(firstRun.runId)
    expect(tableauLayout()).toEqual(originalLayout)

    await click('Submit game')
    await click('New game')
    await click('Start new game')
    expect(vi.mocked(startSolitaireFreeRun).mock.calls[2]).toEqual([3, expect.any(String), undefined])
    expect(tableauLayout()).not.toEqual(originalLayout)
  })

  it('pauses and resumes through the owned run before submitting', async () => {
    await act(async () => root!.render(<CompetitiveSolitairePage account={account} />))
    await click('Play free')

    await click('Pause')
    expect(vi.mocked(setSolitaireFreeRunPaused).mock.calls[0]).toEqual([firstRun.runId, true])
    expect(button('Resume').disabled).toBe(false)
    expect(host.querySelector('.solitaire-match__status')?.textContent).toContain('0:02')

    await click('Resume')
    expect(vi.mocked(setSolitaireFreeRunPaused).mock.calls[1]).toEqual([firstRun.runId, false])
    expect(button('Pause').disabled).toBe(false)

    await click('Submit game')
    expect(host.querySelector('#free-result-title')?.textContent).toContain('0 points')
  })
})

function competitiveMatch(status: SolitairePlayerStatus): SolitaireMatchSession {
  return {
    kind: 'match', matchId: 'c'.repeat(64), playerCount: 4, buyInCredits: 5,
    prizePoolCredits: 5, winnerPayoutCredits: 5,
    startedAtUtc, deadlineAtUtc: '2026-10-16T12:10:00Z', version: 1,
    score: 0, moves: 0, remainingMilliseconds: 600_000,
    isPaused: false, pauseRemainingMilliseconds: 600_000, canUndo: false,
    game: createLocalSolitaireGame(42, 3),
    players: [
      { playerId: account.userId, displayName: account.playerName, seat: 1, joinedAtUtc: startedAtUtc,
        status, isCurrentPlayer: true, score: status === 'finished' ? 0 : undefined, elapsedSeconds: 60 },
      ...[2, 3, 4].map((seat) => ({ playerId: `open-${seat}`, displayName: 'Waiting for players...',
        seat, joinedAtUtc: startedAtUtc, status: 'open' as const, isCurrentPlayer: false })),
    ],
  }
}

describe('competitive Solitaire navigation', () => {
  it('persists Return from a finished run before returning to the lobby', async () => {
    vi.mocked(getSolitaireSession).mockResolvedValueOnce(competitiveMatch('finished'))
    await act(async () => root!.render(<CompetitiveSolitairePage account={account} />))

    await click('Return')

    expect(dismissSolitaireResult).toHaveBeenCalledWith('c'.repeat(64), expect.any(String))
    expect(host.querySelector('.solitaire-lobby')).not.toBeNull()
  })

  it('keeps a finished run visible when its server-backed Return fails', async () => {
    vi.mocked(getSolitaireSession).mockResolvedValueOnce(competitiveMatch('finished'))
    vi.mocked(dismissSolitaireResult).mockRejectedValueOnce(new Error('Could not close the game'))
    await act(async () => root!.render(<CompetitiveSolitairePage account={account} />))

    await click('Return')

    expect(host.querySelector('#competitive-result-title')).not.toBeNull()
    expect(host.querySelector('.solitaire-lobby')).toBeNull()
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Could not close the game')
  })

  it('keeps match history unavailable while the paid play clock is running', async () => {
    vi.mocked(getSolitaireSession).mockResolvedValueOnce(competitiveMatch('playing'))
    await act(async () => root!.render(<CompetitiveSolitairePage account={account} />))

    expect([...host.querySelectorAll('button')].some((item) => item.textContent === 'Match history')).toBe(false)
  })

  it('traps history focus without resetting it on rerenders and restores focus on close', async () => {
    const trigger = document.createElement('button')
    trigger.textContent = 'Open history'
    document.body.append(trigger)
    trigger.focus()
    const close = vi.fn()
    const renderHistory = (onClose: () => void) => <SolitaireHistoryPanel
      items={[]} loading={false} error={null} claimingMatchId={null}
      onClose={onClose} onRefresh={vi.fn()} onClaim={vi.fn()} />

    await act(async () => root!.render(renderHistory(close)))
    const closeButton = host.querySelector<HTMLButtonElement>('[aria-label="Close match history"]')!
    const refreshButton = button('Refresh')
    expect(document.activeElement).toBe(closeButton)

    refreshButton.focus()
    await act(async () => root!.render(renderHistory(() => close())))
    expect(document.activeElement).toBe(refreshButton)

    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true })))
    expect(document.activeElement).toBe(closeButton)
    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true })))
    expect(document.activeElement).toBe(refreshButton)

    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })))
    expect(close).toHaveBeenCalledOnce()
    await act(async () => root!.render(<></>))
    expect(document.activeElement).toBe(trigger)
    trigger.remove()
  })
})
