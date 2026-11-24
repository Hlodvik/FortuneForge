import { ART } from './art.js'
import { FRONTIER_RULES, FRONTIER_SYMBOLS, HAND_NAMES, OUTLAW_RULES } from './rules.js'
import type { FrontierAnte, FrontierHand } from './types.js'

const CARD_SUITS: Record<string, string> = { J: '♣', Q: '♥', K: '♦', A: '♠' }

export function HandRail({ ante, round, reels, selectedRows, held, busy, showComplete, formatPoints, onToggle }: {
  ante: FrontierAnte; round: FrontierHand | null; reels: string[][]; selectedRows: (number | null)[]
  held: boolean[]; busy: boolean; showComplete: boolean; formatPoints: (points: number) => string; onToggle: (index: number) => void
}) {
  const visible = round && (round.phase !== 'complete' || showComplete) ? round : null
  return <section className="frontier-hand" aria-label="Outlaw's Hand and saved ante">
    <div className="frontier-hand__heading"><h2>Outlaw’s Hand</h2><div className="frontier-ante" aria-label={`${ante.chips} of ${ante.requiredChips} saved ante chips`}>
      <div className="frontier-ante__chips" aria-hidden="true">{Array.from({ length: ante.requiredChips }, (_, index) => <i key={index} data-filled={index < ante.chips} />)}</div>
      <strong>{ante.chips}/{ante.requiredChips}</strong>
    </div></div>
    <div className="frontier-hand__cards" data-holdable={visible?.phase === 'hold'}>
      {Array.from({ length: FRONTIER_RULES.columns }, (_, index) => {
        const card = visible?.hand[index]
        const isHeld = visible?.phase === 'hold' ? held[index] : Boolean(card?.held)
        const selected = selectedRows[index]
        const symbol = visible?.phase === 'choose' && !isHeld ? selected === null ? null : reels[index]?.[selected] : card?.symbol
        const name = FRONTIER_SYMBOLS.find(item => item.id === symbol)?.label ?? symbol
        return <button type="button" key={index} className="frontier-hand-card" data-held={isHeld} data-empty={!symbol}
          disabled={busy || visible?.phase !== 'hold'} aria-pressed={visible?.phase === 'hold' ? isHeld : undefined}
          aria-label={`Hand position ${index + 1}${symbol ? `, ${name}` : ', awaiting a card'}${visible?.phase === 'hold' ? isHeld ? ', held. Release this card.' : ', replace on next deal. Hold this card.' : ''}`}
          onClick={() => onToggle(index)}>
          <span className="frontier-playing-card" data-red={symbol === 'Q' || symbol === 'K'}>
            {symbol ? <>
              {['top', 'bottom'].map(corner => <span key={corner} className={`frontier-hand-card__corner frontier-hand-card__corner--${corner}`} aria-hidden="true">
                <span>{symbol === 'WILD' ? 'W' : symbol}</span><span>{CARD_SUITS[symbol] ?? ''}</span>
              </span>)}
              <img className="frontier-hand-card__face" src={ART.symbols[symbol as keyof typeof ART.symbols]} alt="" draggable={false} />
            </> : <img className="frontier-hand-card__back" src={ART.cardBack} alt="" draggable={false} />}
          </span>
          {(visible?.phase === 'hold' || isHeld) && <span className="frontier-hand-card__label">{isHeld ? 'HELD' : 'HOLD'}</span>}
        </button>
      })}
    </div>
    <div className="frontier-hand__readout" aria-live="polite">
      {visible?.phase === 'complete' ? <><strong>{visible.category ? HAND_NAMES[visible.category] : 'Hand complete'}</strong><span>+{formatPoints(visible.awardPoints)}</span></>
        : visible ? <><strong>{visible.dealsRemaining} {visible.dealsRemaining === 1 ? 'deal' : 'deals'} left</strong><span>Hand stake {formatPoints(visible.wagerPoints)}</span></>
          : ante.ready ? <><strong>Your seat is ready</strong><span>Hand stake {formatPoints(ante.fundedWagerPoints)}</span></>
            : <><strong>Fill the ante</strong><span>{OUTLAW_RULES.ante.wantedForOneChip} WANTED +1 · {OUTLAW_RULES.ante.wantedForTwoChips}+ WANTED +2</span></>}
    </div>
  </section>
}
