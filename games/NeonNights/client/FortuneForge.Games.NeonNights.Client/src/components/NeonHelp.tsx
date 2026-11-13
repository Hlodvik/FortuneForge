import { getSlotSymbolDefinition } from '@fortuneforge/slot-client-core'
import { neonHelp, neonSymbols } from '../manifest.js'
import { CAR_FIVE_MATCH_EXAMPLES, CAR_THREE_MATCH_EXAMPLES, FIVE_MATCH_PATTERNS, THREE_MATCH_PATTERNS } from '../paylinePatterns.js'

type PaylinePattern = { label: string; rows: readonly number[] }

function PaylineDiagram({ label, rows, visibleRows }: PaylinePattern & { visibleRows: number }) {
  const width = rows.length * 20
  const rowY = (row: number) => 8 + row * 54 / (visibleRows - 1)

  return (
    <svg
      className={`win-help__pictogram${rows.length === 5 ? ' win-help__pictogram--five' : ''}`}
      viewBox={`0 0 ${width} 70`}
      role="img"
      aria-label={label}
    >
      <title>{label}</title>
      <rect x="1" y="1" width={width - 2} height="68" rx="9" />
      {Array.from({ length: visibleRows * rows.length }, (_, index) => (
        <circle
          key={index}
          className="win-help__pictogram-dot"
          cx={10 + index % rows.length * 20}
          cy={rowY(Math.floor(index / rows.length))}
          r="2.4"
        />
      ))}
      <polyline points={rows.map((row, reel) => `${10 + reel * 20},${rowY(row)}`).join(' ')} />
      {rows.map((row, reel) => (
        <circle key={reel} className="win-help__pictogram-win" cx={10 + reel * 20} cy={rowY(row)} r="5" />
      ))}
    </svg>
  )
}

export function NeonHelp({ carPlay = false }: { carPlay?: boolean }) {
  const help = neonHelp
  const symbolSet = neonSymbols
  const wildDefinition = getSlotSymbolDefinition(symbolSet, 'ACE')
  const paylinePatternIds = help.paylinePatternIds ??
    Array.from({ length: help.paylineCount }, (_, index) => index + 1)
  const fiveMatchPatterns: readonly PaylinePattern[] = carPlay ? CAR_FIVE_MATCH_EXAMPLES : paylinePatternIds.map((patternId) => {
    const rows = FIVE_MATCH_PATTERNS[patternId - 1]
    if (!rows) {
      throw new Error(`Win-help payline pattern ${patternId} is not defined.`)
    }
    return { label: `Valid five-symbol payline ${patternId}`, rows }
  })
  const threeMatchPatterns = carPlay ? CAR_THREE_MATCH_EXAMPLES : THREE_MATCH_PATTERNS
  const visibleRows = carPlay ? 7 : 4

  return (
    <>
        <p className="win-help__intro">
          Matching symbols must connect across neighboring reels. {wildDefinition.label}{' '}
          substitutes for regular paying symbols in three- and five-symbol matches.
          A win requires at least one regular paying symbol.
        </p>

        <div className="win-help__rules">
          <article className="win-help__rule">
            <span className="win-help__rule-number">3</span>
            <div>
              <h3>Three symbols</h3>
              <p>
                Match across reels 1–3 in a straight row or a straight diagonal.
                {carPlay && ' All seven rows are in play. On a diagonal, each step moves one row in the same direction.'}
              </p>
              <div className="win-help__pictograms" aria-label={carPlay ? 'Examples of three-symbol paths' : 'Allowed three-symbol paths'}>
                {threeMatchPatterns.map(pattern => (
                  <PaylineDiagram key={pattern.label} {...pattern} visibleRows={visibleRows} />
                ))}
              </div>
            </div>
          </article>

          <article className="win-help__rule">
            <span className="win-help__rule-number">5</span>
            <div>
              <h3>Five symbols</h3>
              <p>
                {carPlay
                  ? 'Match a symbol across all five reels on a base-game route. Those shapes also repeat at lower positions wherever they fit in the expanded grid.'
                  : `A matching symbol across all five reels wins on any of the ${fiveMatchPatterns.length} routes shown.`}
              </p>
              <div
                className="win-help__five-pictograms"
                aria-label={carPlay ? 'Examples of five-symbol paylines' : 'All valid five-symbol paylines'}
              >
                {fiveMatchPatterns.map(pattern => (
                  <PaylineDiagram key={pattern.label} {...pattern} visibleRows={visibleRows} />
                ))}
              </div>
              <p className="win-help__note">
                Payline wins require three or five matching symbols.
              </p>
            </div>
          </article>

          {help.freeGames && (
          <article className="win-help__rule">
            <span className="win-help__rule-number">{help.freeGames.badge ?? 'FREE'}</span>
            <div>
              <h3>{help.freeGames.title ?? 'Free games'}</h3>
              <p>
                Land {help.freeGames.requiredSymbols} or more {help.freeGames.symbolLabel ?? 'FREE GAME'} symbols anywhere in the
                window to receive {help.freeGames.awardedSpins} {help.freeGames.awardLabel ?? 'free games'}.
                {' '}{help.freeGames.awardLabel ? 'Bonus rounds use the wager that triggered them.' : 'Free games use the wager that triggered them.'}
              </p>
            </div>
          </article>
          )}

          {help.extraSections?.map((section) => (
            <article className="win-help__rule" key={`${section.badge}-${section.title}`}>
              <span className="win-help__rule-number">{section.badge}</span>
              <div>
                <h3>{section.title}</h3>
                <p>{section.body}</p>
              </div>
            </article>
          ))}
        </div>

        <p className="win-help__fine-print">
          Repeated copies of the same short path pay once. When wilds create several possible
          symbol matches, the highest-paying valid match is used.
        </p>
    </>
  )
}
