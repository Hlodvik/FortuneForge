import { createRoot } from 'react-dom/client'
import { GameLoadingScreen } from '../GameLoadingScreen'
import { LOADING_PRESENTATIONS_A } from '../loadingPresentationsA'

// Standalone review fixture only; production route selection remains caller-owned.
const params = new URLSearchParams(window.location.search)
const selected = LOADING_PRESENTATIONS_A.find((entry) => entry.id === params.get('game'))
const entry = params.get('missing') === '1' && selected
  ? { ...selected, image: '/__unavailable-loading-review-art__.webp' }
  : selected
const screen = <GameLoadingScreen entry={entry} />

createRoot(document.getElementById('root')!).render(
  params.get('shell') === '1'
    ? <div className="loading-review-shell"><header className="loading-review-header">Review shell</header><div className="in-game-shell__body">{screen}</div></div>
    : screen,
)
