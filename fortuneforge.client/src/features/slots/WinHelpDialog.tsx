import type { RefObject } from 'react'
import { FIVE_MATCH_PATTERNS, THREE_MATCH_PATTERNS } from './config/paylinePatterns'
import type { SlotHelpDefinition } from './config/slotFeatures'
import { getSlotSymbolDefinition, type SlotSymbolSet } from './config/symbolSets'

type WinHelpDialogProps = {
  isOpen: boolean
  closeButtonRef: RefObject<HTMLButtonElement | null>
  help: SlotHelpDefinition
  symbolSet: SlotSymbolSet
  onClose: () => void
}

export function WinHelpDialog({
  isOpen,
  closeButtonRef,
  help,
  symbolSet,
  onClose,
}: WinHelpDialogProps) {
  if (!isOpen) return null
  const wildDefinition = getSlotSymbolDefinition(symbolSet, 'ACE')

  return (
    <div
      className="win-help__backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <section
        className="win-help"
        role="dialog"
        aria-modal="true"
        aria-labelledby="win-help-title"
      >
        <button
          ref={closeButtonRef}
          className="win-help__close"
          type="button"
          aria-label="Close win guide"
          onClick={() => onClose()}
        >
          ×
        </button>

        <p className="win-help__eyebrow">Fortune guide</p>
        <h2 id="win-help-title">What counts as a win?</h2>
        <p className="win-help__intro">
          Matching symbols must connect across neighboring reels. {wildDefinition.label}
          is the highest-value symbol and can substitute on a full five-symbol payline.
        </p>

        <div className="win-help__rules">
          <article className="win-help__rule">
            <span className="win-help__rule-number">3</span>
            <div>
              <h3>Three symbols</h3>
              <p>
                The first win begins on reel 1 and crosses reels 1–3 in a straight row or one
                clean diagonal direction.
              </p>
              <div className="win-help__pictograms" aria-label="Allowed three-symbol paths">
                {THREE_MATCH_PATTERNS.map((pattern) => (
                  <svg
                    key={pattern.label}
                    className="win-help__pictogram"
                    viewBox="0 0 60 70"
                    role="img"
                    aria-label={pattern.label}
                  >
                    <title>{pattern.label}</title>
                    <rect x="1" y="1" width="58" height="68" rx="9" />
                    {Array.from({ length: 12 }, (_, index) => {
                      const column = index % 3
                      const row = Math.floor(index / 3)
                      return (
                        <circle
                          key={`${column}-${row}`}
                          className="win-help__pictogram-dot"
                          cx={10 + column * 20}
                          cy={8 + row * 18}
                          r="2.4"
                        />
                      )
                    })}
                    <polyline
                      points={pattern.rows
                        .map((row, column) => `${10 + column * 20},${8 + row * 18}`)
                        .join(' ')}
                    />
                    {pattern.rows.map((row, column) => (
                      <circle
                        key={`${column}-${row}-win`}
                        className="win-help__pictogram-win"
                        cx={10 + column * 20}
                        cy={8 + row * 18}
                        r="5"
                      />
                    ))}
                  </svg>
                ))}
              </div>
              <p className="win-help__note">Three-symbol wins begin on reel 1.</p>
            </div>
          </article>

          <article className="win-help__rule">
            <span className="win-help__rule-number">5</span>
            <div>
              <h3>Five symbols</h3>
              <p>
                A matching symbol across all five reels wins on any of the game’s {help.paylineCount} full
                payline patterns. {wildDefinition.label} symbols may substitute here, and the more central
                patterns pay more. Four-symbol runs do not pay.
              </p>
              <div
                className="win-help__five-pictograms"
                aria-label="All valid five-symbol paylines"
              >
                {FIVE_MATCH_PATTERNS.map((rows, patternIndex) => (
                  <svg
                    key={rows.join('-')}
                    className="win-help__pictogram win-help__pictogram--five"
                    viewBox="0 0 100 70"
                    role="img"
                    aria-label={`Valid five-symbol payline ${patternIndex + 1}`}
                  >
                    <title>{`Valid five-symbol payline ${patternIndex + 1}`}</title>
                    <rect x="1" y="1" width="98" height="68" rx="9" />
                    {Array.from({ length: 20 }, (_, index) => {
                      const column = index % 5
                      const row = Math.floor(index / 5)
                      return (
                        <circle
                          key={`${column}-${row}`}
                          className="win-help__pictogram-dot"
                          cx={10 + column * 20}
                          cy={8 + row * 18}
                          r="2.4"
                        />
                      )
                    })}
                    <polyline
                      points={rows
                        .map((row, column) => `${10 + column * 20},${8 + row * 18}`)
                        .join(' ')}
                    />
                    {rows.map((row, column) => (
                      <circle
                        key={`${column}-${row}-win`}
                        className="win-help__pictogram-win"
                        cx={10 + column * 20}
                        cy={8 + row * 18}
                        r="5"
                      />
                    ))}
                  </svg>
                ))}
              </div>
              <p className="win-help__note">
                Every winning route connects one matching symbol on each of the five reels.
              </p>
            </div>
          </article>

          {help.freeGames && (
          <article className="win-help__rule">
            <span className="win-help__rule-number">FREE</span>
            <div>
              <h3>Free games</h3>
              <p>
                Land {help.freeGames.requiredSymbols} or more FREE GAME symbols anywhere in the
                window to receive {help.freeGames.awardedSpins} free games. Free games use the
                wager that triggered them.
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
      </section>
    </div>
  )
}
