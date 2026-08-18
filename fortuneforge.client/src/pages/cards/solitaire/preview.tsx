import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createLocalSolitaireGame } from '../../../games/cards/solitaire/solitaireEngine'
import { SolitaireContent } from './CompetitiveSolitairePage'
import '../../../index.css'

const previewGame = createLocalSolitaireGame(2_026_08_16, 3)
const noop = () => undefined

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <div className="solitaire-page solitaire-thumbnail-preview">
      <SolitaireContent
        availability={{ kind: 'ready', session: { kind: 'idle' } }}
        balanceCredits={100}
        busy={false}
        pending={null}
        playerCount={4}
        buyInCredits={5}
        drawCount={3}
        freeGame={previewGame}
        freePaused={false}
        freeComplete={false}
        freeAutoWinning={false}
        freeSetupOpen={false}
        competitiveSetupMatchId={null}
        freeElapsedMilliseconds={83_000}
        freeCanUndo={true}
        onPlayerCountChange={noop}
        onBuyInChange={noop}
        onDrawCountChange={noop}
        onJoin={noop}
        onCancel={noop}
        onCommand={noop}
        onCloseCompleted={noop}
        onNewCompetitive={noop}
        onChooseNewCompetitive={noop}
        onCancelCompetitiveSetup={noop}
        onClaim={noop}
        onStartFree={noop}
        onReplayFree={noop}
        onChooseNewFreeGame={noop}
        onCancelFreeSetup={noop}
        onFreeCommand={noop}
        onFreePause={noop}
        onFreeUndo={noop}
        onFreeSubmit={noop}
        onExitFree={noop}
        onRefresh={noop}
      />
    </div>
  </StrictMode>,
)
