import { useCallback, useEffect, useRef, useState } from 'react'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import {
  actOnBlackjackGame,
  BlackjackRequestError,
  createBlackjackRequestId,
  getBlackjackGame,
  requestBlackjackStatus,
  startBlackjackGame,
  toPlayingCard,
  type BlackjackAction,
  type BlackjackGame,
  type BlackjackStatus,
} from '../../../games/cards/blackjack/blackjackApi'
import '../../../games/cards/shared/playingCards.css'
import './blackjack.css'

type PendingRequest =
  | { kind: 'deal'; key: string; wager: number }
  | { kind: 'action'; key: string; action: BlackjackAction; gameId: string; version: number }

export function BlackjackPage({
  account,
  demoMode = false,
}: {
  account?: AccountSummary
  demoMode?: boolean
}) {
  const [status, setStatus] = useState<BlackjackStatus | null>(null)
  const [game, setGame] = useState<BlackjackGame | null>(null)
  const [wager, setWager] = useState(5)
  const [isBusy, setIsBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [availabilityRevision, setAvailabilityRevision] = useState(0)
  const pendingRequest = useRef<PendingRequest | null>(null)
  const savedGameKey = demoMode
    ? 'fortune-forge.blackjack-demo-game'
    : 'fortune-forge.blackjack-game'

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    setStatus(null)
    setError(null)

    void requestBlackjackStatus(demoMode, controller.signal)
      .then(async (nextStatus) => {
        if (!active) return
        setStatus(nextStatus)
        const savedGameId = readSessionValue(savedGameKey)
        if (!savedGameId) return
        try {
          const savedGame = await getBlackjackGame(savedGameId, demoMode)
          if (active) setGame(savedGame)
        } catch (requestError) {
          if (requestError instanceof BlackjackRequestError && requestError.status === 404) {
            removeSessionValue(savedGameKey)
            return
          }
          if (active) setError(messageFor(requestError))
        }
      })
      .catch((requestError: unknown) => {
        if (active && !(requestError instanceof DOMException && requestError.name === 'AbortError')) {
          setStatus(null)
          setError('Blackjack is unavailable right now. No wager was accepted.')
        }
      })

    return () => {
      active = false
      controller.abort()
    }
  }, [availabilityRevision, demoMode, savedGameKey])

  const deal = useCallback(async () => {
    if (!status?.available || isBusy || game?.status === 'active') return
    const request = pendingRequest.current?.kind === 'deal'
      ? pendingRequest.current
      : { kind: 'deal' as const, key: createBlackjackRequestId(), wager }
    pendingRequest.current = request
    setIsBusy(true)
    setError(null)
    try {
      const dealt = await startBlackjackGame(request.wager, request.key, demoMode)
      pendingRequest.current = null
      setGame(dealt)
      writeSessionValue(savedGameKey, dealt.gameId)
    } catch (requestError) {
      if (isDefiniteFailure(requestError)) pendingRequest.current = null
      setError(messageFor(requestError))
    } finally {
      setIsBusy(false)
    }
  }, [demoMode, game?.status, isBusy, savedGameKey, status?.available, wager])

  const act = useCallback(async (action: BlackjackAction) => {
    if (!game || game.status !== 'active' || isBusy) return
    const existing = pendingRequest.current
    if (existing?.kind === 'action'
      && (existing.gameId !== game.gameId || existing.version !== game.version || existing.action !== action)) {
      return
    }
    const request = existing?.kind === 'action'
      ? existing
      : {
          kind: 'action' as const,
          key: createBlackjackRequestId(),
          action,
          gameId: game.gameId,
          version: game.version,
        }
    pendingRequest.current = request
    setIsBusy(true)
    setError(null)
    try {
      const updated = await actOnBlackjackGame(game, action, request.key, demoMode)
      pendingRequest.current = null
      setGame(updated)
    } catch (requestError) {
      if (requestError instanceof BlackjackRequestError
        && requestError.code === 'blackjack-state-conflict') {
        try {
          const reconciled = await getBlackjackGame(game.gameId, demoMode)
          setGame(reconciled)
          pendingRequest.current = null
        } catch {
          // Preserve the original conflict below if reconciliation also fails.
        }
      } else if (isDefiniteFailure(requestError)) {
        pendingRequest.current = null
      }
      setError(messageFor(requestError))
    } finally {
      setIsBusy(false)
    }
  }, [demoMode, game, isBusy])

  const resetTable = useCallback(() => {
    if (game?.status === 'active' || isBusy) return
    pendingRequest.current = null
    setGame(null)
    setError(null)
    removeSessionValue(savedGameKey)
  }, [game?.status, isBusy, savedGameKey])

  const balance = game?.balance ?? account?.balances.slotsCredits ?? null
  const pending = pendingRequest.current
  const serviceReady = status?.available === true

  return (
    <div className="blackjack-page">
      <header className="blackjack-header">
        <a href={demoMode ? '/demo/cards' : '/cards'} aria-label="Back to card games">← Card room</a>
        <div>
          <span>{demoMode ? 'Demo table' : 'Fortune table'}</span>
          <strong>{balance === null ? 'Balance loading' : `R${balance.toFixed(2)}`}</strong>
        </div>
      </header>

      <main className="blackjack-main">
        <section className="blackjack-title" aria-labelledby="blackjack-title">
          <p>Fortune Forge presents</p>
          <h1 id="blackjack-title">Blackjack</h1>
          <span>Dealer stands on all 17s · Blackjack pays 3:2 · No split or insurance</span>
        </section>

        <section className="blackjack-table" aria-label="Blackjack table">
          <Hand
            label="Dealer"
            hand={game?.dealer ?? null}
            emptyLabel="Dealer waits for the deal"
          />

          <div className="blackjack-table__message" aria-live="polite">
            <strong>{game?.message ?? (serviceReady ? 'Place a wager to begin.' : 'Checking the table…')}</strong>
            {game?.status === 'completed' && game.payout > 0 && (
              <span>Payout R{game.payout.toFixed(2)}</span>
            )}
          </div>

          <Hand
            label="Your hand"
            hand={game?.player ?? null}
            emptyLabel="Your cards will appear here"
          />

          <div className="blackjack-controls">
            {game?.status === 'active' ? (
              <>
                <button
                  type="button"
                  disabled={!game.canHit || isBusy || (pending?.kind === 'action' && pending.action !== 'hit')}
                  onClick={() => void act('hit')}
                >
                  {pending?.kind === 'action' && pending.action === 'hit' ? 'Retry hit' : 'Hit'}
                </button>
                <button
                  type="button"
                  disabled={!game.canStand || isBusy || (pending?.kind === 'action' && pending.action !== 'stand')}
                  onClick={() => void act('stand')}
                >
                  {pending?.kind === 'action' && pending.action === 'stand' ? 'Retry stand' : 'Stand'}
                </button>
                <button
                  type="button"
                  disabled={!game.canDouble || isBusy || (pending?.kind === 'action' && pending.action !== 'double')}
                  onClick={() => void act('double')}
                >
                  {pending?.kind === 'action' && pending.action === 'double' ? 'Retry double' : 'Double'}
                </button>
              </>
            ) : (
              <>
                <label>
                  <span>Wager (Rand)</span>
                  <input
                    type="number"
                    min={status?.minimumWager ?? 0.5}
                    max={status?.maximumWager ?? 100}
                    step={status?.wagerIncrement ?? 0.5}
                    value={wager}
                    disabled={!serviceReady || isBusy}
                    onChange={(event) => setWager(Number(event.target.value))}
                  />
                </label>
                <button
                  className="blackjack-controls__deal"
                  type="button"
                  disabled={!serviceReady || isBusy || wager <= 0}
                  onClick={() => void deal()}
                >
                  {isBusy ? 'Dealing…' : pending?.kind === 'deal' ? 'Retry deal' : 'Deal'}
                </button>
                {game && (
                  <button type="button" disabled={isBusy} onClick={resetTable}>Clear table</button>
                )}
              </>
            )}
          </div>
        </section>

        {error && (
          <div className="blackjack-error" role="alert">
            <strong>{error}</strong>
            {!serviceReady && (
              <button type="button" onClick={() => setAvailabilityRevision((value) => value + 1)}>
                Check again
              </button>
            )}
          </div>
        )}

        <aside className="blackjack-rules" aria-label="Blackjack rules">
          <strong>Table rules</strong>
          <span>Wagers: R0.50–R100.00 in R0.50 steps</span>
          <span>Double is available only on your first two cards</span>
          <span>All cards and outcomes come from the Fortune Forge API</span>
        </aside>
      </main>
    </div>
  )
}

function Hand({
  label,
  hand,
  emptyLabel,
}: {
  label: string
  hand: BlackjackGame['player'] | null
  emptyLabel: string
}) {
  return (
    <section className="blackjack-hand" aria-label={label}>
      <div className="blackjack-hand__heading">
        <h2>{label}</h2>
        {hand?.score !== null && hand?.score !== undefined && (
          <span>{hand.soft ? 'Soft ' : ''}{hand.score}</span>
        )}
      </div>
      <div className="blackjack-hand__cards">
        {hand === null ? (
          <span className="blackjack-hand__empty">{emptyLabel}</span>
        ) : hand.cards.map((card, index) => (
          <div className="ff-card-slot" key={`${card.rank}-${card.suit}-${index}`}>
            <PlayingCard card={toPlayingCard(card, index)} faceDown={card.hidden} />
          </div>
        ))}
      </div>
    </section>
  )
}

function messageFor(error: unknown): string {
  if (error instanceof Error) return error.message
  return 'Blackjack could not complete the request. No new action was sent.'
}

function isDefiniteFailure(error: unknown): boolean {
  return error instanceof BlackjackRequestError && error.status < 500
}

function readSessionValue(key: string): string | null {
  try {
    return window.sessionStorage.getItem(key)
  } catch {
    return null
  }
}

function writeSessionValue(key: string, value: string): void {
  try {
    window.sessionStorage.setItem(key, value)
  } catch {
    // A hand remains playable in memory when session storage is unavailable.
  }
}

function removeSessionValue(key: string): void {
  try {
    window.sessionStorage.removeItem(key)
  } catch {
    // Nothing else is required when session storage is unavailable.
  }
}
