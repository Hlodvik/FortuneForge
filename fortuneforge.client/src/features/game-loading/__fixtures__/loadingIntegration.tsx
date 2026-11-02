import { createRoot } from 'react-dom/client'
import { InGameShell } from '../../../components/InGameShell'
import { AuthenticatedRouteState } from '../../../app/routes/AuthenticatedRouteState'
import { GameLoadingScreen } from '../GameLoadingScreen'
import { GameLoadingRouteProvider } from '../GameLoadingRouteContext'
import { GAME_LOADING_PRESENTATIONS } from '../gameLoadingRegistry'
import '../../../index.css'
import '../../../app/styles/index.css'
import '../../../pages/cards/blackjack/blackjack.css'
import '../../../pages/cards/threeCardPoker/threeCardPoker.css'
import '../../../pages/cards/houseHoldem/houseHoldem.css'
import '../../../pages/cards/solitaire/solitaire.css'
import '@fortuneforge/games-texas-holdem/styles.css'

// Review-only entry point, excluded from the app's imports and production routes.
// Match the real host chrome/CSS while inspecting pending views without wallet calls.
const params = new URLSearchParams(window.location.search)
const selected = GAME_LOADING_PRESENTATIONS.find(entry => entry.id === params.get('game'))
const entry = params.get('missing') === '1' && selected
  ? { ...selected, image: '/__unavailable-loading-review-art__.webp' }
  : selected
const shell = params.get('shell') === '1'
const legacyClasses = ['blackjack-page', 'blackjack-table-page',
  entry?.id !== 'blackjack' ? 'three-card-poker-page' : '',
  entry?.id === 'house-holdem' ? 'house-holdem-page' : ''].filter(Boolean).join(' ')
const holdem = entry?.id === 'texas-holdem'
const solitaire = entry?.id === 'solitaire'
const pageClasses = holdem ? 'ff-holdem-page' : solitaire ? 'solitaire-page' : legacyClasses
const bodyClasses = holdem ? 'ff-holdem-shell-body' : solitaire ? 'solitaire-shell-body' : 'blackjack-shell-body'

createRoot(document.getElementById('root')!).render(
  <div className="app-shell app-shell--game">
    <GameLoadingRouteProvider entry={entry}>
      {shell ? (
        <InGameShell title={entry?.title} className={holdem || solitaire ? undefined : 'blackjack-game-shell three-card-poker-shell'} bodyClassName={bodyClasses}>
          <div className={pageClasses}><GameLoadingScreen entry={entry} /></div>
        </InGameShell>
      ) : <AuthenticatedRouteState error={null} loadingLabel="Opening game…" errorTitle="Unable to open game" onRetry={() => undefined} />}
    </GameLoadingRouteProvider>
  </div>,
)
