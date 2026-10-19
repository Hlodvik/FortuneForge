import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import { createLocalSolitaireGame, projectRedactedDraw } from '../../../games/cards/solitaire/solitaireEngine'
import type {
  SolitaireAvailability,
  SolitaireHistoryItem,
  SolitaireMatchSession,
  SolitairePlayerStatus,
  SolitaireResultSession,
} from '../../../games/cards/solitaire/solitaireTypes'
import { SolitaireContent, SolitaireHistoryPanel } from './CompetitiveSolitairePage'

describe('Solitaire presentation', () => {
  it('projects a redacted competitive draw immediately without exposing stock identities', () => {
    const game = matchSession().game
    const next = projectRedactedDraw(game)

    expect(next.stock).toHaveLength(game.stock.length - 1)
    expect(next.waste).toEqual([{ isFaceUp: false }])
    expect(next.moves).toBe(game.moves + 1)
  })

  it('keeps free play available while competitive buy-ins are disabled', () => {
    const markup = render({
      kind: 'disabled',
      message: 'Competitive Solitaire is still being verified and cannot accept a buy-in yet.',
    })
    expect(markup).toContain('No buy-ins are being accepted.')
    expect(markup).toContain('Play free Solitaire')
    expect(markup).not.toContain('<select')
  })

  it('defaults the pre-game choice to draw 3 and offers draw 1', () => {
    const markup = render({ kind: 'ready', session: { kind: 'idle' } })
    expect(markup).toContain('<option value="3" selected="">Turn 3</option>')
    expect(markup).toContain('<option value="1">Turn 1</option>')
    expect(markup).toContain('Play free')
  })

  it('keeps legacy queued tickets recoverable without displaying a queue screen', () => {
    const markup = render({ kind: 'ready', session: {
      kind: 'queued', ticketId: 'ticket-1', playerCount: 4, buyInCredits: 5,
      prizePoolCredits: 20, winnerPayoutCredits: 18, position: 1,
      joinedAtUtc: '2026-10-16T12:00:00Z',
      players: [player('seat-1', 'Sam River', 1, 'queued', true)],
    } })

    expect(markup).toContain('Starting match…')
    expect(markup).toContain('Cancel')
    expect(markup).not.toContain('Queue position')
    expect(markup).not.toContain('Open seat')
  })

  it('shows ordered match participants and open seats in history without provisional winnings', () => {
    const item: SolitaireHistoryItem = {
      matchId: 'h'.repeat(64), playerCount: 4, buyInCredits: 5, prizePoolCredits: 20,
      status: 'playing', startedAtUtc: '2026-10-17T12:00:00Z', completedAtUtc: null,
      placement: 0, score: 430, elapsedSeconds: 600, payoutCredits: 0, netCredits: -5,
      opponents: [],
      players: [
        player('player-1', 'Ada', 1, 'finished', true),
        player('player-2', 'Kai', 2, 'playing', false),
        player('open-seat-3', 'Waiting for players...', 3, 'open', false),
        player('open-seat-4', 'Waiting for players...', 4, 'open', false),
      ],
    }
    const markup = renderToStaticMarkup(createElement(SolitaireHistoryPanel, {
      items: [item], loading: false, error: null, claimingMatchId: null,
      hasMore: true, onLoadMore: vi.fn(),
      onClose: vi.fn(), onRefresh: vi.fn(), onClaim: vi.fn(),
    }))

    expect(markup).toContain('Ada')
    expect(markup).toContain('Kai')
    expect(markup.match(/Waiting for players\.\.\./g)).toHaveLength(2)
    expect(markup).toContain('In progress')
    expect(markup).toContain('Older matches')
    expect(markup).not.toContain('No payout')
    expect(markup).not.toContain('Claim reward')
  })

  it('offers claiming from settled match history after another run has begun', () => {
    const item: SolitaireHistoryItem = {
      matchId: 'h'.repeat(64), playerCount: 4, buyInCredits: 5, prizePoolCredits: 20,
      status: 'settled', startedAtUtc: '2026-10-17T12:00:00Z', completedAtUtc: '2026-10-17T15:00:00Z',
      placement: 1, score: 800, elapsedSeconds: 600, payoutCredits: 18, netCredits: 13,
      claimStatus: 'unclaimed', canClaim: true, opponents: ['Kai'],
      players: [player('player-1', 'Ada', 1, 'finished', true), player('player-2', 'Kai', 2, 'finished', false),
        player('player-3', 'Noor', 3, 'finished', false), player('player-4', 'Rae', 4, 'finished', false)],
    }
    const markup = renderToStaticMarkup(createElement(SolitaireHistoryPanel, {
      items: [item], loading: false, error: null, claimingMatchId: null,
      onClose: vi.fn(), onRefresh: vi.fn(), onClaim: vi.fn(),
    }))

    expect(markup).toContain('Settled')
    expect(markup).toContain('R18.00 won')
    expect(markup).toContain('Claim reward')
  })

  it('orders status, board, pause, and submit without match/table headings', () => {
    const markup = render({ kind: 'ready', session: matchSession() })
    expect(markup.indexOf('solitaire-match__status')).toBeLessThan(markup.indexOf('solitaire-board'))
    expect(markup.indexOf('aria-label="Board controls"')).toBeLessThan(markup.indexOf('solitaire-board'))
    expect(markup.match(/aria-label="Undo"/g)).toHaveLength(1)
    expect(markup.match(/aria-label="Hint"/g)).toHaveLength(1)
    expect(markup.indexOf('solitaire-board')).toBeLessThan(markup.indexOf('Pause · 10:00 left'))
    expect(markup.indexOf('Pause · 10:00 left')).toBeLessThan(markup.indexOf('Submit game'))
    expect(markup).not.toMatch(/authoritative/i)
    expect(markup).not.toContain('Race ')
    expect(markup).not.toContain('Forfeit')
    expect(markup).toContain('solitaire-card-face__corner--bottom')
  })

  it('shows a frozen play clock and remaining cumulative budget while paused', () => {
    const match = matchSession()
    const markup = render({ kind: 'ready', session: {
      ...match,
      isPaused: true,
      pauseRemainingMilliseconds: 419_000,
    } })
    expect(markup).toContain('Paused')
    expect(markup).toContain('Resume · 6:59 left')
  })

  it('renders a clear terminal integrity result', () => {
    const match = matchSession()
    const markup = render({ kind: 'ready', session: {
      ...match,
      players: match.players.map((player) => player.isCurrentPlayer
        ? { ...player, status: 'integrity-failed' }
        : player),
    } })
    expect(markup).toContain('Game ended')
    expect(markup).toContain('Return')
    expect(markup).not.toContain('Submit game')
  })

  it('shows a persisted rollback warning with acknowledgement and support guidance', () => {
    const markup = render({ kind: 'ready', session: {
      ...matchSession(),
      integrityWarning: {
        warningId: 'warning-1234567890abcdef',
        reason: 'That action was not legal from the last verified board position.',
        purpose: 'This warning protects fair competitive play. The board was restored.',
        occurredAtUtc: '2026-08-17T12:00:00Z',
        acknowledged: false,
      },
    } })
    expect(markup).toContain('Move reversed')
    expect(markup).toContain('Acknowledge')
    expect(markup).toContain('Contact customer support if you think we got it wrong.')
    expect(markup).not.toContain('Submit game')
  })

  it('shows wager and game options before starting another competitive game', () => {
    const match = matchSession()
    const completed = {
      ...match,
      players: match.players.map((player) => player.isCurrentPlayer
        ? { ...player, status: 'finished' as const }
        : player),
    }
    const markup = render(
      { kind: 'ready', session: completed },
      { competitiveSetupMatchId: match.matchId, buyInCredits: 10 },
    )
    expect(markup).toContain('Choose your table')
    expect(markup).toContain('Wager')
    expect(markup).toContain('<option value="10" selected="">R10</option>')
    expect(markup).toContain('Start · R10')
  })

  it('offers an explicit one-time claim for a settled result', () => {
    const result: SolitaireResultSession = {
      kind: 'result',
      matchId: 'b'.repeat(64),
      playerCount: 4,
      buyInCredits: 5,
      prizePoolCredits: 20,
      winnerPayoutCredits: 18,
      platformFeeCredits: 2,
      startedAtUtc: '2026-08-14T00:00:00Z',
      completedAtUtc: '2026-08-14T00:05:00Z',
      claimStatus: 'unclaimed',
      canClaim: true,
      standings: [{
        rank: 1,
        playerId: 'player-1',
        displayName: 'Ada',
        score: 725,
        moves: 91,
        elapsedSeconds: 300,
        status: 'finished',
        payoutCredits: 18,
        isCurrentPlayer: true,
      }],
    }
    const markup = render({ kind: 'ready', session: result })
    expect(markup).toContain('R18.00 ready to claim')
    expect(markup).toContain('Reward earned')
    expect(markup).toContain('Claim your reward, then return to the card room.')
    expect(markup).toContain('Claim reward')
    expect(markup).not.toContain('Recent matches')
  })

  it('uses Accept when the current player did not win a reward', () => {
    const winner = {
      rank: 1, playerId: 'player-2', displayName: 'Kai', score: 800, moves: 80,
      elapsedSeconds: 280, status: 'finished' as const, payoutCredits: 18, isCurrentPlayer: false,
    }
    const current = {
      rank: 2, playerId: 'player-1', displayName: 'Ada', score: 725, moves: 91,
      elapsedSeconds: 300, status: 'finished' as const, payoutCredits: 0, isCurrentPlayer: true,
    }
    const markup = render({ kind: 'ready', session: {
      kind: 'result', matchId: 'c'.repeat(64), playerCount: 4, buyInCredits: 5,
      prizePoolCredits: 20, winnerPayoutCredits: 18, platformFeeCredits: 2,
      startedAtUtc: '2026-08-14T00:00:00Z', completedAtUtc: '2026-08-14T00:05:00Z',
      standings: [winner, current], claimStatus: 'unclaimed', canClaim: true,
    } })
    expect(markup).toContain('>Accept<')
    expect(markup).not.toContain('Claim result')
  })

  it('shows free-game score, elapsed time, new-game, and return actions', () => {
    const markup = render(
      { kind: 'ready', session: { kind: 'idle' } },
      { freeGame: createLocalSolitaireGame(42, 3), freeComplete: true },
    )
    expect(markup).toContain('Game complete')
    expect(markup).toContain('Deck cleared')
    expect(markup).toContain('Every card reached a foundation')
    expect(markup).toContain('Replay')
    expect(markup).toContain('New game')
    expect(markup).toContain('Return')
    expect(markup).not.toContain('No balance or competitive record was changed.')
    expect(markup.match(/New game/g)).toHaveLength(1)
  })

  it('shows the deck-completed celebration before the free result dialog', () => {
    const markup = render(
      { kind: 'ready', session: { kind: 'idle' } },
      { freeGame: createLocalSolitaireGame(42, 3), freeAutoWinning: true },
    )
    expect(markup).toContain('Deck completed!')
    expect(markup).toContain('Sending every card home.')
    expect(markup).not.toContain('Game complete')
  })

  it('keeps a failed authoritative submission retryable', () => {
    const markup = render(
      { kind: 'ready', session: { kind: 'idle' } },
      {
        freeGame: createLocalSolitaireGame(42, 3),
        freeSubmissionError: 'The run could not be saved.',
      },
    )
    expect(markup).toContain('The run could not be saved.')
    expect(markup).toContain('Retry submission')
    expect(markup).not.toContain('Game complete')
  })

  it('opens a new-game draw chooser and retains Turn 1', () => {
    const markup = render(
      { kind: 'ready', session: { kind: 'idle' } },
      {
        drawCount: 1,
        freeGame: createLocalSolitaireGame(42, 1),
        freeComplete: true,
        freeSetupOpen: true,
      },
    )
    expect(markup).toContain('Choose your draw')
    expect(markup).toContain('Turn 1')
    expect(markup).toContain('aria-pressed="true"')
    expect(markup).toContain('Start new game')
  })
})

function render(
  availability: SolitaireAvailability,
  overrides: Partial<Parameters<typeof SolitaireContent>[0]> = {},
): string {
  return renderToStaticMarkup(createElement(SolitaireContent, {
    availability,
    balanceCredits: 100,
    busy: false,
    pending: null,
    playerCount: 4,
    buyInCredits: 5,
    drawCount: 3,
    freeGame: null,
    freePaused: false,
    freePauseBusy: false,
    freeComplete: false,
    freeAutoWinning: false,
    freeSubmitting: false,
    freeSubmissionError: null,
    freeSetupOpen: false,
    competitiveSetupMatchId: null,
    freeElapsedMilliseconds: 0,
    freeCanUndo: false,
    onPlayerCountChange: vi.fn(),
    onBuyInChange: vi.fn(),
    onDrawCountChange: vi.fn(),
    onJoin: vi.fn(),
    onCancel: vi.fn(),
    onCommand: vi.fn(),
    onCloseCompleted: vi.fn(),
    onNewCompetitive: vi.fn(),
    onChooseNewCompetitive: vi.fn(),
    onCancelCompetitiveSetup: vi.fn(),
    onClaim: vi.fn(),
    onStartFree: vi.fn(),
    onReplayFree: vi.fn(),
    onChooseNewFreeGame: vi.fn(),
    onCancelFreeSetup: vi.fn(),
    onFreeCommand: vi.fn(),
    onFreePause: vi.fn(),
    onFreeUndo: vi.fn(),
    onFreeSubmit: vi.fn(),
    onExitFree: vi.fn(),
    onRefresh: vi.fn(),
    ...overrides,
  }))
}

function matchSession(): SolitaireMatchSession {
  return {
    kind: 'match',
    matchId: 'c'.repeat(64),
    playerCount: 4,
    buyInCredits: 5,
    prizePoolCredits: 20,
    winnerPayoutCredits: 18,
    startedAtUtc: '2026-08-14T00:00:00Z',
    deadlineAtUtc: '2026-08-14T00:10:00Z',
    version: 1,
    score: 0,
    moves: 0,
    remainingMilliseconds: 600_000,
    isPaused: false,
    pauseRemainingMilliseconds: 600_000,
    canUndo: false,
    game: {
      stock: [{ isFaceUp: false }],
      waste: [],
      foundations: [[], [], [], []],
      tableau: [[{ isFaceUp: true, id: 'hearts-1', suit: 'hearts', rank: 1 }], [], [], [], [], [], []],
      drawCount: 3,
      score: 0,
      moves: 0,
      message: 'Your move',
    },
    players: [
      player('player-1', 'Ada', 1, 'playing', true),
      player('open-seat-2', 'Open seat', 2, 'open', false),
      player('open-seat-3', 'Open seat', 3, 'open', false),
      player('open-seat-4', 'Open seat', 4, 'open', false),
    ],
  }
}

function player(
  playerId: string,
  displayName: string,
  seat: number,
  status: SolitairePlayerStatus,
  isCurrentPlayer: boolean,
) {
  return { playerId, displayName, seat, joinedAtUtc: '2026-08-14T00:00:00Z', status, isCurrentPlayer }
}
