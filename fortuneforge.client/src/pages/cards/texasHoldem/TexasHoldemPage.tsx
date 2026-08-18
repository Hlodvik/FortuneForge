import { useState } from 'react'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import { freshCardSeed } from '../../../games/cards/shared/cards'
import {
  createHoldemGame,
  holdemBetSize,
  playHoldemAction,
  type HoldemAction,
  type HoldemGame,
} from '../../../games/cards/texasHoldem/holdemEngine'
import '../../../games/cards/shared/playingCards.css'
import './texasHoldem.css'

export type TexasHoldemPageProps = Readonly<{
  playerName?: string
  returnHref?: string
  demoMode?: boolean
}>

type HandHistoryItem = Readonly<{
  handNumber: number
  summary: string
  pot: number
}>

const stageLabels = {
  preflop: 'Pre-flop',
  flop: 'Flop',
  turn: 'Turn',
  river: 'River',
} as const

export function TexasHoldemPage({
  playerName = 'You',
  returnHref = '/demo/cards',
  demoMode = true,
}: TexasHoldemPageProps) {
  const [game, setGame] = useState<HoldemGame>(() => createHoldemGame({ seed: freshCardSeed() }))
  const [handNumber, setHandNumber] = useState(1)
  const [history, setHistory] = useState<HandHistoryItem[]>([])
  const [showHistory, setShowHistory] = useState(false)
  const [showRules, setShowRules] = useState(false)

  const act = (action: HoldemAction) => {
    const next = playHoldemAction(game, action)
    if (game.status === 'playing' && next.status === 'complete' && next.result) {
      setHistory((items) => [{
        handNumber,
        summary: next.result!.summary,
        pot: next.result!.potWon,
      }, ...items].slice(0, 8))
    }
    setGame(next)
  }

  const dealNextHand = () => {
    setHandNumber((value) => value + 1)
    setGame(createHoldemGame({
      seed: freshCardSeed(),
      playerChips: game.playerChips,
      opponentChips: game.opponentChips,
      dealer: game.dealer === 'player' ? 'opponent' : 'player',
    }))
  }

  const callLabel = game.toCall > 0 ? `Call R${game.toCall}` : 'Check'
  const betSize = holdemBetSize(game.stage)
  const betLabel = game.stage === 'preflop' ? `Raise R${betSize}` : `Bet R${betSize}`
  const potDisplay = game.status === 'complete' ? game.result?.potWon ?? 0 : game.pot

  return (
    <div className="holdem-page">
      <header className="holdem-header">
        <a className="holdem-brand" href="/" aria-label="Fortune Forge home">
          <span aria-hidden="true">♠</span>
          <span><small>Fortune Forge</small><strong>Texas Hold&apos;em</strong></span>
        </a>
        <div className="holdem-header__actions">
          <span className="holdem-practice-badge">Practice chips · no account wagering</span>
          <button type="button" onClick={() => setShowRules((value) => !value)} aria-expanded={showRules}>
            How to play
          </button>
          <button type="button" onClick={() => setShowHistory((value) => !value)} aria-expanded={showHistory}>
            Hand history
          </button>
          <a href={returnHref}>Card room</a>
        </div>
      </header>

      <main className="holdem-main">
        <section className="holdem-table-shell" aria-label="Heads-up Texas Hold'em practice table">
          <div className="holdem-table">
            <div className="holdem-seat holdem-seat--opponent">
              <span className="holdem-avatar" aria-hidden="true">FF</span>
              <span className="holdem-seat__copy"><strong>Forge Dealer</strong><small>R{game.opponentChips}</small></span>
              {game.dealer === 'opponent' && <span className="holdem-dealer-button" title="Dealer button">D</span>}
            </div>

            <div className="holdem-hole-cards holdem-hole-cards--opponent" aria-label="Opponent cards">
              {game.opponentHole.map((card) => (
                <span className="ff-card-slot" key={card.id}>
                  <PlayingCard card={card} faceDown={game.status !== 'complete'} />
                </span>
              ))}
            </div>

            <div className="holdem-pot" aria-label={`Pot R${potDisplay}`}>
              <span aria-hidden="true">●</span>
              <small>Pot</small>
              <strong>R{potDisplay}</strong>
            </div>

            <div className="holdem-community" aria-label="Community cards">
              {Array.from({ length: 5 }, (_, index) => {
                const card = game.community[index]
                return card ? (
                  <span className="ff-card-slot" key={card.id}><PlayingCard card={card} /></span>
                ) : (
                  <span className="ff-card-slot holdem-community__empty" key={index} aria-label={`Empty community card position ${index + 1}`}>♠</span>
                )
              })}
            </div>

            <div className="holdem-status" aria-live="polite">
              <span>{game.status === 'complete' ? 'Hand complete' : stageLabels[game.stage]}</span>
              <strong>{game.message}</strong>
              {game.result?.playerHand && game.result.opponentHand && (
                <small>Your {game.result.playerHand.name} · Dealer {game.result.opponentHand.name}</small>
              )}
            </div>

            <div className="holdem-hole-cards holdem-hole-cards--player" aria-label={`${playerName}'s hole cards`}>
              {game.playerHole.map((card) => (
                <span className="ff-card-slot" key={card.id}><PlayingCard card={card} /></span>
              ))}
            </div>

            <div className="holdem-seat holdem-seat--player">
              <span className="holdem-avatar holdem-avatar--player" aria-hidden="true">{playerName.slice(0, 2).toUpperCase()}</span>
              <span className="holdem-seat__copy"><strong>{playerName}</strong><small>R{game.playerChips}</small></span>
              {game.dealer === 'player' && <span className="holdem-dealer-button" title="Dealer button">D</span>}
            </div>
          </div>

          <div className="holdem-controls" aria-label="Poker actions">
            <span className="holdem-hand-number">Hand #{handNumber}</span>
            {game.status === 'playing' ? (
              <>
                <button className="holdem-action holdem-action--fold" type="button" onClick={() => act('fold')}>Fold</button>
                <button className="holdem-action" type="button" onClick={() => act('check-call')}>{callLabel}</button>
                <button
                  className="holdem-action holdem-action--primary"
                  type="button"
                  disabled={game.playerChips <= game.toCall}
                  onClick={() => act('bet-raise')}
                >
                  {betLabel}
                </button>
              </>
            ) : (
              <button className="holdem-action holdem-action--primary" type="button" onClick={dealNextHand}>
                Deal next hand
              </button>
            )}
          </div>
        </section>

        {(showRules || showHistory) && (
          <aside className="holdem-drawer">
            {showRules && (
              <section>
                <span className="holdem-eyebrow">Table rules</span>
                <h2>Heads-up fixed-bet Hold&apos;em</h2>
                <p>Make the best five-card hand from your two hole cards and the five shared cards. This practice table uses R10/R20 blinds, R40 bets before the turn, and R80 bets on the turn and river.</p>
                <ol>
                  <li>Straight Flush</li><li>Four of a Kind</li><li>Full House</li><li>Flush</li><li>Straight</li><li>Three of a Kind</li><li>Two Pair</li><li>One Pair</li><li>High Card</li>
                </ol>
              </section>
            )}
            {showHistory && (
              <section>
                <span className="holdem-eyebrow">This session</span>
                <h2>Hand history</h2>
                {history.length === 0 ? <p>No completed hands yet.</p> : (
                  <ul className="holdem-history">
                    {history.map((item) => (
                      <li key={item.handNumber}><strong>#{item.handNumber}</strong><span>{item.summary}</span><small>Pot R{item.pot}</small></li>
                    ))}
                  </ul>
                )}
              </section>
            )}
          </aside>
        )}
      </main>

      <footer className="holdem-footer">
        <span>{demoMode ? 'Demo practice table' : 'Account-neutral practice table'}</span>
        <span>Practice chips reset automatically if either stack falls below the big blind.</span>
      </footer>
    </div>
  )
}
