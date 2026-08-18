import blackjackPreview from '../../assets/cards/previews/blackjack-game.png'
import holdemPreview from '../../assets/cards/previews/holdem-game.png'
import solitairePreview from '../../assets/cards/previews/solitaire-game.png'
import { GameTypeMenu } from '../../components/GameTypeMenu'
import { PlayerHeader } from '../../components/PlayerHeader'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { useRecentSlotGame } from '../../games/slots/useRecentSlotGame'
import { useCardRoomHistory } from '../cards/useCardRoomHistory'
import '../index.css'

type CatalogGame = {
  category: string
  href?: string
  icon?: string
  image?: string
  name: string
  summary: string
}

const cardGames: readonly CatalogGame[] = [
  { name: 'Fortune Blackjack', category: 'Card game', href: '/cards/blackjack', image: blackjackPreview, summary: 'Continuous five-seat Blackjack.' },
  { name: 'Texas Hold’em', category: 'Card game', href: '/cards/texas-holdem', image: holdemPreview, summary: 'Live community-card tables.' },
  { name: 'Competitive Solitaire', category: 'Card game', href: '/cards/solitaire', image: solitairePreview, summary: 'Competitive and free Klondike.' },
  { name: 'Pinochle', category: 'Card game', icon: '♣', summary: 'Partnership meld and trick taking.' },
  { name: 'Spades', category: 'Card game', icon: '♠', summary: 'Partnership bidding and tricks.' },
  { name: 'Hearts', category: 'Card game', icon: '♥', summary: 'Avoid points and shoot the moon.' },
]

const casinoGames: readonly CatalogGame[] = [
  cardGames[0]!, cardGames[1]!,
  { name: "Wukong’s Journey", category: 'Slot machine', href: '/slots/wukong', icon: '☀', summary: 'Five celestial reels and seal collections.' },
  { name: 'Craps', category: 'Casino game', icon: '⚄', summary: 'A classic casino dice table.' },
]

const arcadeGames: readonly CatalogGame[] = [
  { name: 'Shoot ’Em Up!', category: 'Arcade game', icon: '✦', summary: 'Fast scrolling arcade action.' },
  { name: 'Asteroids', category: 'Arcade game', icon: '☄', summary: 'Clear the field and survive.' },
  { name: 'Snake', category: 'Arcade game', icon: '⌁', summary: 'Grow longer without crashing.' },
  { name: 'Falling Blocks', category: 'Arcade game', icon: '▦', summary: 'Arrange falling shapes into lines.' },
]

const diceGames: readonly CatalogGame[] = [
  { name: 'Craps', category: 'Dice game', icon: '⚄', summary: 'A classic casino dice table.' },
  { name: 'Balut', category: 'Dice game', icon: '⚂', summary: 'Build high-scoring dice combinations.' },
  { name: 'Liar’s Dice', category: 'Dice game', icon: '⚅', summary: 'Bid, bluff, and call the table.' },
]

const otherGames: readonly CatalogGame[] = [
  { name: 'Horse Flight', category: 'Other game', icon: '♞', summary: 'An airborne racing adventure.' },
]

export function OtherGamesPage({ account }: { account: AccountSummary }) {
  const cardHistory = useCardRoomHistory()
  const recentSlot = useRecentSlotGame(account.userId)
  const seen = new Set<string>()
  const recentlyPlayed: CatalogGame[] = []

  if (recentSlot.game !== null) {
    seen.add(recentSlot.game.playHref ?? recentSlot.game.title)
    recentlyPlayed.push({
      name: recentSlot.game.title,
      category: 'Slot machine',
      href: recentSlot.game.playHref ?? '/slots',
      image: recentSlot.game.image,
      summary: 'Your most recently played slot machine.',
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
    recentlyPlayed.push({ ...game, summary: activity.summary })
    if (recentlyPlayed.length === 4) break
  }

  const newGames: CatalogGame[] = []
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
          <GameSection games={[cardGames[0]!, casinoGames[2]!, cardGames[2]!, cardGames[1]!]} title="Popular" />
          <GameSection games={recentlyPlayed} title="Recently played" empty="Your latest games will appear here." />
          {newGames.length > 0 && <GameSection games={newGames} title="New games" />}
          <GameSection games={cardGames} title="Card games" />
          <GameSection games={casinoGames} title="Casino games" />
          <GameSection games={arcadeGames} title="Arcade games" />
          <GameSection games={diceGames} title="Dice games" />
          <GameSection games={otherGames} title="Etc." />
        </main>
      </div>
    </div>
  )
}

function GameSection({ title, games, empty }: { title: string; games: readonly CatalogGame[]; empty?: string }) {
  return (
    <section className="all-games-section" aria-labelledby={`all-games-${title.replaceAll(' ', '-').toLowerCase()}`}>
      <header><h2 id={`all-games-${title.replaceAll(' ', '-').toLowerCase()}`}>{title}</h2><span>{games.length > 0 ? `${games.length} games` : ''}</span></header>
      {games.length === 0 ? <p className="all-games-section__empty">{empty}</p> : <div className="all-games-row">
        {games.map((game, index) => <GameTile game={game} key={`${game.name}-${index}`} />)}
      </div>}
    </section>
  )
}

function GameTile({ game }: { game: CatalogGame }) {
  const content = <>
    {game.image ? <img src={game.image} alt="" draggable="false" /> : <span className="all-games-card__icon" aria-hidden="true">{game.icon ?? '◇'}</span>}
    <small>{game.category}</small><strong>{game.name}</strong><span>{game.summary}</span>
  </>
  return game.href
    ? <a className="all-games-card" href={game.href}>{content}</a>
    : <article className="all-games-card is-placeholder">{content}<b>In the forge</b></article>
}
