import { getSlotSymbolDefinition, type SlotSymbolSet } from '../config/symbolSets'
import type { SlotCollectionFeature, SlotHelpDefinition, SlotSpecialRoundFeature } from '../config/slotFeatures'
import { getWinPresentationTier } from '../presentation/spinPresentation'
import type { PaylinePayout } from '../types/slots'
import { formatRand } from '../slotPagePresentation'

type SlotPlayGuideProps = {
  bestWin: PaylinePayout | null
  collections?: SlotCollectionFeature
  help: SlotHelpDefinition
  lastWin: number
  onOpenHelp: () => void
  selectedWager: number
  specialRound?: SlotSpecialRoundFeature
  symbolSet: SlotSymbolSet
  winningPaylineCount: number
}

export function SlotPlayGuide({
  bestWin,
  collections,
  help,
  lastWin,
  onOpenHelp,
  selectedWager,
  specialRound,
  symbolSet,
  winningPaylineCount,
}: SlotPlayGuideProps) {
  const winningMatch = bestWin?.matches[0]?.match
  const matchingSymbol = winningMatch
    ? getSlotSymbolDefinition(symbolSet, winningMatch.symbolId)
    : null
  const tier = getWinPresentationTier(lastWin, selectedWager)
  const chestTarget = collections?.entries[0]?.requiredCount

  return (
    <aside className="slots-page__play-guide" aria-live="polite" aria-label="Pirates' Fortune game guide">
      {lastWin > 0 && bestWin && winningMatch && matchingSymbol ? (
        <>
          <span className={`slots-page__play-guide-tier slots-page__play-guide-tier--${tier}`}>
            {tier === 'jackpot' ? 'Jackpot win' : tier === 'big' ? 'Big win' : 'Win'}
          </span>
          <strong className="slots-page__play-guide-amount">+{formatRand(lastWin)}</strong>
          <p>
            Line {bestWin.paylineId} paid for {winningMatch.matchLength} {matchingSymbol.label}
            {winningMatch.matchLength === 1 ? '' : 's'} in a row.
            {winningPaylineCount > 1 ? ` Plus ${winningPaylineCount - 1} more winning line${winningPaylineCount === 2 ? '' : 's'}.` : ''}
          </p>
        </>
      ) : (
        <>
          <span className="slots-page__play-guide-kicker">How to win</span>
          <strong>Chart a winning course</strong>
          <p>Match 3 or more of the same symbol from the first reel along one of {help.paylineCount} routes.</p>
        </>
      )}

      <ul>
        <li><b>Jolly Roger</b> wilds can complete a five-symbol route.</li>
        {chestTarget && specialRound && (
          <li><b>Treasure chests:</b> fill any chest with {chestTarget} matching gems for {specialRound.title}.</li>
        )}
      </ul>

      <button type="button" onClick={onOpenHelp}>
        View all {help.paylineCount} winning routes
      </button>
    </aside>
  )
}
