import blackjackPreview from '../../assets/cards/previews/blackjack-game.png'
import holdemPreview from '../../assets/cards/previews/holdem-game.png'
import solitairePreview from '../../assets/cards/previews/solitaire-game.png'
import { GameTypeMenu } from '../../components/GameTypeMenu'
import { PlayerHeader } from '../../components/PlayerHeader'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { SLOT_GAME_CATALOG, type SlotGameCatalogEntry } from '../../games/slots/catalog'
import { useRecentSlotGame } from '../../games/slots/useRecentSlotGame'
import { useCardRoomHistory } from '../cards/useCardRoomHistory'
import '../index.css'

type CatalogGame = {
  category: string
  href?: string
  icon?: string
  image?: string
  imagePresentation?: 'contain' | 'cover'
  imageScale?: 'standard' | 'compact'
  name: string
  summary: string
  tone?: 'asteroids' | 'baccarat' | 'flappy' | 'horse' | 'keno' | 'poker' | 'sic-bo' | 'war'
}

type RecentCatalogGame = {
  game: CatalogGame
  playedAtUtc: string
}

const cardGames: readonly CatalogGame[] = [
  { name: 'Fortune Blackjack', category: 'Card game', href: '/cards/blackjack', image: blackjackPreview, summary: 'Continuous five-seat Blackjack.' },
  { name: 'Texas Hold’em', category: 'Card game', href: '/cards/texas-holdem', image: holdemPreview, summary: 'Live community-card tables.' },
  { name: 'Competitive Solitaire', category: 'Card game', href: '/cards/solitaire', image: solitairePreview, summary: 'Classic Klondike with timed competitive deals.' },
  { name: 'Video Poker', category: 'Casino card game', href: '/games/video-poker', icon: '♤', summary: 'Build the strongest five-card poker hand.', tone: 'poker' },
  { name: 'Baccarat', category: 'Casino card game', href: '/games/baccarat', icon: '♦', summary: 'Choose player, banker, or tie on a fast card game.', tone: 'baccarat' },
  { name: 'Casino War', category: 'Casino card game', href: '/games/casino-war', icon: '⚔', summary: 'Compare cards against the dealer in a simple showdown.', tone: 'war' },
  { name: 'Hearts', category: 'Card game', href: '/cards/hearts', icon: '♥', summary: 'Avoid points, take tricks wisely, and shoot the moon.' },
]

const slotGames: readonly CatalogGame[] = SLOT_GAME_CATALOG.map(catalogGameFromSlot)

function catalogGameFromSlot(game: SlotGameCatalogEntry): CatalogGame {
  return {
    name: game.shortTitle,
    category: 'Slot machine',
    href: game.playHref ?? undefined,
    image: game.image,
    imagePresentation: game.imagePresentation,
    imageScale: game.imageScale,
    summary: game.description,
  }
}

const casinoGames: readonly CatalogGame[] = [
  slotGames[0]!,
  cardGames[0]!,
  cardGames[3]!,
  cardGames[4]!,
  cardGames[5]!,
  { name: 'Keno', category: 'Casino game', href: '/games/keno', icon: '◎', summary: 'Pick numbers and match the drawn board.', tone: 'keno' },
  { name: 'Sic Bo', category: 'Dice game', href: '/games/sic-bo', icon: '⚂', summary: 'Predict three-dice outcomes across a betting layout.', tone: 'sic-bo' },
  { name: 'Roulette', category: 'Casino game', href: '/games/roulette', icon: '◉', summary: 'Choose straight, split, street, corner, and outside bets on a single-zero wheel.' },
  { name: 'Craps', category: 'Casino game', href: '/games/craps', icon: '⚄', summary: 'Ride the come-out roll, establish the point, and press the Pass Line.' },
]

const arcadeGames: readonly CatalogGame[] = [
  { name: 'Asteroids', category: 'Arcade game', href: '/games/asteroids', icon: '☄', summary: 'Pilot a seeded run and chase the competition leaderboard.', tone: 'asteroids' },
  { name: 'Flappy', category: 'Arcade game', href: '/games/flappy', icon: '⌁', summary: 'Thread a flier through a fast-moving obstacle course.', tone: 'flappy' },
  { name: 'Horse Flight', category: 'Platform running game', href: '/games/horse-flight', icon: '♞', summary: 'An endless platform runner with timed jumps and obstacles.', tone: 'horse' },
  { name: '2048', category: 'Puzzle game', href: '/games/2048', icon: '▦', summary: 'Merge matching tiles and build the golden 2048.' },
  { name: 'Drop Merge', category: 'Number arcade', href: '/games/drop-merge', icon: '▩', summary: 'Drop number tiles, build chains, and make a huge tile.' },
  { name: 'Snake', category: 'Arcade game', href: '/games/snake', icon: '⌁', summary: 'Grow longer, collect fruit, and stay clear of the walls.' },
]

const diceGames: readonly CatalogGame[] = [
  { name: 'Craps', category: 'Dice game', href: '/games/craps', icon: '⚄', summary: 'Ride the come-out roll, establish the point, and press the Pass Line.' },
  { name: 'Sic Bo', category: 'Dice game', href: '/games/sic-bo', icon: '⚂', summary: 'Predict three-dice outcomes across a betting layout.', tone: 'sic-bo' },
  { name: 'Liar’s Dice', category: 'Dice game', href: '/games/liars-dice', icon: '⚅', summary: 'Bid, bluff, and call the table in a game of nerve.' },
]

const newGames: readonly CatalogGame[] = [
  cardGames[3]!,
  cardGames[4]!,
  cardGames[5]!,
  casinoGames[5]!,
  diceGames[1]!,
  arcadeGames[1]!,
  arcadeGames[2]!,
  arcadeGames[3]!,
  arcadeGames[4]!,
]

export function OtherGamesPage({ account }: { account: AccountSummary }) {
  const cardHistory = useCardRoomHistory()
  const recentSlot = useRecentSlotGame(account.userId)
  const seen = new Set<string>()
  const recentEntries: RecentCatalogGame[] = []

  for (const slotGame of recentSlot.games) {
    const href = slotGame.game.playHref ?? '/slots'
    if (seen.has(href)) continue
    seen.add(href)
    recentEntries.push({
      playedAtUtc: slotGame.playedAtUtc,
      game: {
        ...catalogGameFromSlot(slotGame.game),
        href,
        summary: 'Previously played slot machine.',
      },
    })
  }
  for (const activity of cardHistory.activities) {
    const game = activity.game === 'blackjack'
      ? cardGames[0]!
      : activity.game === 'texas-holdem'
        ? cardGames[1]!
        : cardGames[2]!
    if (game === undefined || game.href === undefined || seen.has(game.href)) continue
    seen.add(game.href)
    recentEntries.push({
      game: { ...game, summary: activity.summary },
      playedAtUtc: activity.completedAtUtc ?? activity.startedAtUtc,
    })
  }
  const recentlyPlayed = recentEntries
    .sort((left, right) => Date.parse(right.playedAtUtc) - Date.parse(left.playedAtUtc))
    .map(({ game }) => game)

  return (
    <div className="player-page compact-game-page all-games-page">
      <PlayerHeader account={account} />
      <div className="game-hub-layout">
        <GameTypeMenu active="all" />
        <main className="game-hub-content game-picker-main">
          <section className="game-picker-heading game-picker-heading--compact">
            <p className="account-eyebrow">All games</p>
            <h1>Find your next game</h1>
            <p>Browse by what is popular, what you played recently, or the kind of game you want.</p>
          </section>
          <GameSection games={[casinoGames[0]!, cardGames[3]!, arcadeGames[0]!, cardGames[4]!]} title="Popular" />
          {recentlyPlayed.length > 0 && <GameSection games={recentlyPlayed} title="Recently played" recent />}
          <GameSection games={newGames} title="New games" />
          <GameSection games={slotGames} title="Slot machines" />
          <GameSection games={cardGames} title="Card games" />
          <GameSection games={casinoGames} title="Casino games" />
          <GameSection games={arcadeGames} title="Arcade games" />
          <GameSection games={diceGames} title="Dice games" />
        </main>
      </div>
    </div>
  )
}

function GameSection({
  title,
  games,
  recent = false,
}: {
  title: string
  games: readonly CatalogGame[]
  recent?: boolean
}) {
  return (
    <section className="all-games-section" aria-labelledby={`all-games-${title.replaceAll(' ', '-').toLowerCase()}`}>
      <header><h2 id={`all-games-${title.replaceAll(' ', '-').toLowerCase()}`}>{title}</h2><span>{games.length > 0 ? `${games.length} games` : ''}</span></header>
      <div className={`all-games-row${recent ? ' all-games-row--recent' : ''}`}>
        {games.map((game, index) => <GameTile game={game} key={`${game.name}-${index}`} />)}
      </div>
    </section>
  )
}

function GameTile({ game }: { game: CatalogGame }) {
  const content = <>
    {game.image ? <img className={`all-games-card__image all-games-card__image--${game.imagePresentation ?? 'cover'}${game.imageScale === 'compact' ? ' all-games-card__image--compact' : ''}`} src={game.image} alt="" draggable="false" /> : <span className={`all-games-card__icon${game.tone ? ` all-games-card__icon--${game.tone}` : ''}`} aria-hidden="true">{game.icon ?? '◇'}</span>}
    <small>{game.category}</small><strong>{game.name}</strong><span>{game.summary}</span>
  </>
  return game.href
    ? <a className="all-games-card" href={game.href}>{content}</a>
    : <article className="all-games-card is-placeholder">{content}</article>
}
