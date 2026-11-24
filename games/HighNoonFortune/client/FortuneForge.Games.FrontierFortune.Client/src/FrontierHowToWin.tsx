import { useEffect, useId, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { ART } from './art.js'
import { FRONTIER_RULES, FRONTIER_SYMBOLS, HAND_NAMES, OUTLAW_RULES, PAYLINES, PAYTABLE } from './rules.js'
import './howToWin.css'

export type FrontierHowToWinProps = {
  open: boolean
  onClose: () => void
  formatPoints: (points: number) => string
}

const chapters = [
  { id: 'reels', label: 'Reels', title: 'Follow the paying trail.' },
  { id: 'ante', label: 'Ante', title: 'Earn your seat at the table.' },
  { id: 'hand', label: 'The Hand', title: 'Pick ’em. Hold ’em. Show ’em.' },
  { id: 'awards', label: 'Awards', title: 'Know what your symbols pay.' },
] as const
type Chapter = typeof chapters[number]['id']
type ArtSymbol = keyof typeof ART.symbols
const matchLengths = Object.keys(PAYTABLE.J).map(Number).sort((a, b) => a - b)
const payingSymbols = FRONTIER_SYMBOLS.filter(symbol => Object.hasOwn(PAYTABLE, symbol.id))
const ordinaryCards = OUTLAW_RULES.hand.symbols.filter(symbol => symbol.id !== FRONTIER_RULES.wildSymbol).map(symbol => symbol.id as ArtSymbol)
const handIllustrations: Record<string, ArtSymbol[]> = {
  pair: [ordinaryCards[0], ordinaryCards[0], ordinaryCards[1], ordinaryCards[2], ordinaryCards[3]],
  'two-pair': [ordinaryCards[0], ordinaryCards[0], ordinaryCards[1], ordinaryCards[1], ordinaryCards[2]],
  trips: [ordinaryCards[0], ordinaryCards[0], ordinaryCards[0], ordinaryCards[1], ordinaryCards[2]],
  'full-house': [ordinaryCards[0], ordinaryCards[0], ordinaryCards[0], ordinaryCards[1], ordinaryCards[1]],
  four: [ordinaryCards[0], ordinaryCards[0], ordinaryCards[0], ordinaryCards[0], ordinaryCards[1]],
  five: Array<ArtSymbol>(FRONTIER_RULES.columns).fill(ordinaryCards[0]),
  grand: Array<ArtSymbol>(FRONTIER_RULES.columns).fill(FRONTIER_RULES.wildSymbol as ArtSymbol),
}

function SymbolArt({ symbol, className = '' }: { symbol: ArtSymbol; className?: string }) {
  return <img className={className} src={ART.symbols[symbol]} alt="" draggable={false} loading="lazy" />
}

function FieldCard({ symbol, held = false, compact = false }: { symbol?: ArtSymbol; held?: boolean; compact?: boolean }) {
  const name = FRONTIER_SYMBOLS.find(item => item.id === symbol)?.label
  return <span className={`frontier-howto-card${compact ? ' frontier-howto-card--compact' : ''}`} data-held={held}>
    <span className="frontier-howto-card__face" role="img" aria-label={symbol ? `${name}${held ? ', held' : ''}` : 'Card back'}>
      {symbol ? <>
        <SymbolArt symbol={symbol} className="frontier-howto-card__center" />
        <SymbolArt symbol={symbol} className="frontier-howto-card__index frontier-howto-card__index--top" />
        <SymbolArt symbol={symbol} className="frontier-howto-card__index frontier-howto-card__index--bottom" />
      </> : <img className="frontier-howto-card__back" src={ART.cardBack} alt="" draggable={false} loading="lazy" />}
    </span>
    {held && <span className="frontier-howto-card__held">HELD</span>}
  </span>
}

function CardHand({ cards, held = [], compact = false }: { cards: (ArtSymbol | undefined)[]; held?: number[]; compact?: boolean }) {
  return <div className={`frontier-howto-hand${compact ? ' frontier-howto-hand--compact' : ''}`}>
    {cards.map((symbol, index) => <FieldCard key={index} symbol={symbol} held={held.includes(index)} compact={compact} />)}
  </div>
}

function ReelIllustration({ length, selection = false }: { length: number; selection?: boolean }) {
  const row = Math.floor(FRONTIER_RULES.rows / 2)
  const symbols = selection ? ordinaryCards : FRONTIER_SYMBOLS.map(symbol => symbol.id as ArtSymbol)
  return <figure className="frontier-howto-reels-figure">
    <div className="frontier-howto-reels" role="img" aria-label={selection
      ? `Illustration: choose one visible card from each of the ${FRONTIER_RULES.columns} unheld reels.`
      : `Illustration: ${length} matching symbols start on the leftmost reel, with WILD standing in for a Jack.`}>
      {Array.from({ length: FRONTIER_RULES.columns }, (_, column) => <div className="frontier-howto-reel" key={column}>
        {Array.from({ length: FRONTIER_RULES.rows }, (_, visibleRow) => {
          const marked = selection ? visibleRow === column % FRONTIER_RULES.rows : visibleRow === row && column < length
          const symbol = selection ? symbols[(column + visibleRow) % symbols.length]
            : visibleRow === row ? column < length ? column === 1 ? FRONTIER_RULES.wildSymbol as ArtSymbol : ordinaryCards[0] : ordinaryCards[1]
              : symbols[(column * FRONTIER_RULES.rows + visibleRow + 4) % symbols.length]
          return <span key={visibleRow} className="frontier-howto-reel__cell" data-marked={marked}><SymbolArt symbol={symbol} /></span>
        })}
      </div>)}
      {!selection && <svg className="frontier-howto-reels__line" viewBox={`0 0 ${FRONTIER_RULES.columns * 100} ${FRONTIER_RULES.rows * 100}`} preserveAspectRatio="none" aria-hidden="true">
        <polyline points={Array.from({ length }, (_, column) => `${column * 100 + 50},${row * 100 + 50}`).join(' ')} />
        {Array.from({ length }, (_, column) => <circle key={column} cx={column * 100 + 50} cy={row * 100 + 50} r="8" />)}
      </svg>}
    </div>
    <figcaption>{selection ? 'One marked choice in every open reel.' : 'Start at the left edge. Follow one paying path.'}</figcaption>
  </figure>
}

function PaylineMap() {
  return <div className="frontier-howto-paths">
    {PAYLINES.map((pattern, index) => <figure key={index}>
      <svg viewBox="0 0 150 80" role="img" aria-label={`Payline ${index + 1}: rows ${pattern.map(row => row + 1).join(', ')}`}>
        {Array.from({ length: FRONTIER_RULES.columns * FRONTIER_RULES.rows }, (_, cell) => <circle key={cell}
          cx={15 + cell % FRONTIER_RULES.columns * 30} cy={15 + Math.floor(cell / FRONTIER_RULES.columns) * 25} r="3" />)}
        <polyline points={pattern.map((row, column) => `${15 + column * 30},${15 + row * 25}`).join(' ')} />
      </svg>
      <figcaption>Line {index + 1}</figcaption>
    </figure>)}
  </div>
}

function PosterAward({ posters, chips, plus = false }: { posters: number; chips: number; plus?: boolean }) {
  return <figure className="frontier-howto-poster-award">
    <div className="frontier-howto-posters" aria-hidden="true">{Array.from({ length: posters }, (_, index) => <SymbolArt key={index} symbol={FRONTIER_RULES.scatterSymbol as ArtSymbol} />)}{plus && <b>+</b>}</div>
    <figcaption><span>{posters}{plus ? '+' : ''} WANTED</span><strong>+{chips} {chips === 1 ? 'ante chip' : 'ante chips'}</strong></figcaption>
  </figure>
}

export function FrontierHowToWin({ open, onClose, formatPoints }: FrontierHowToWinProps) {
  const id = useId()
  const [chapter, setChapter] = useState<Chapter>('reels')
  const [matchLength, setMatchLength] = useState(matchLengths[0])
  const modal = useRef<HTMLDialogElement>(null)
  const reading = useRef<HTMLDivElement>(null)
  const tabs = useRef(new Map<Chapter, HTMLButtonElement>())
  const chapterIndex = chapters.findIndex(item => item.id === chapter)
  const selectChapter = (next: Chapter, focusTab = false) => {
    setChapter(next)
    if (reading.current) reading.current.scrollTop = 0
    if (focusTab) tabs.current.get(next)?.focus({ preventScroll: true })
  }
  useEffect(() => {
    const dialog = modal.current
    if (!dialog) return
    if (open && !dialog.open) { dialog.showModal(); tabs.current.get(chapter)?.focus({ preventScroll: true }) }
    else if (!open && dialog.open) dialog.close()
  }, [open, chapter])
  useEffect(() => { const dialog = modal.current; return () => { if (dialog?.open) dialog.close() } }, [])
  const navigateTabs = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const next = event.key === 'ArrowRight' ? (index + 1) % chapters.length
      : event.key === 'ArrowLeft' ? (index + chapters.length - 1) % chapters.length
        : event.key === 'Home' ? 0 : event.key === 'End' ? chapters.length - 1 : null
    if (next !== null) { event.preventDefault(); selectChapter(chapters[next].id, true) }
  }

  return <dialog ref={modal} className="frontier-howto" aria-labelledby={`${id}-title`}
    style={{ '--howto-town': `url("${ART.town}")` } as CSSProperties}
    onCancel={event => { event.preventDefault(); onClose() }} onClose={() => { if (open) onClose() }}>
    <header className="frontier-howto__header">
      <img src={ART.title} alt="Frontier Fortune" />
      <div><span className="frontier-howto-kicker">The frontier field guide</span><h2 id={`${id}-title`}>How to Win</h2></div>
      <button className="frontier-howto__close" type="button" aria-label="Close How to Win" onClick={onClose}>×</button>
    </header>
    <div className="frontier-howto__tabs" role="tablist" aria-label="How to Win chapters">
      {chapters.map((item, index) => <button key={item.id} type="button" role="tab" id={`${id}-tab-${item.id}`}
        aria-controls={`${id}-panel-${item.id}`} aria-selected={chapter === item.id} tabIndex={chapter === item.id ? 0 : -1}
        ref={button => { if (button) tabs.current.set(item.id, button); else tabs.current.delete(item.id) }}
        onClick={() => selectChapter(item.id)} onKeyDown={event => navigateTabs(event, index)}>{item.label}</button>)}
    </div>
    <div className="frontier-howto__reading" ref={reading}>
      <section className="frontier-howto-page" id={`${id}-panel-reels`} role="tabpanel" aria-labelledby={`${id}-tab-reels`} hidden={chapter !== 'reels'} tabIndex={0}>
        <div className="frontier-howto-page__heading"><span className="frontier-howto-kicker">The paying reels</span><h3>{chapters[0].title}</h3><p>Set your wager with Bet. Spin the reels. Matching symbols pay from the leftmost reel along a valid path.</p></div>
        <div className="frontier-howto-scene"><div>
          <ReelIllustration length={matchLength} />
          <div className="frontier-howto-match-options" role="group" aria-label="Illustrated matching run length">{matchLengths.map(length => <button key={length} type="button" aria-pressed={matchLength === length} onClick={() => setMatchLength(length)}>{length} match</button>)}</div>
        </div><div className="frontier-howto-notes">
          <article><SymbolArt symbol={ordinaryCards[0]} /><div><h4>Begin on reel one.</h4><p>Match {matchLengths.join(', ')} symbols along the same path. Each path pays its highest eligible match once.</p></div></article>
          <article><SymbolArt symbol={FRONTIER_RULES.wildSymbol as ArtSymbol} /><div><h4>WILD stands in.</h4><p>It substitutes for ordinary paying symbols, including card ranks, the sheriff badge and BAR.</p></div></article>
          <article><SymbolArt symbol={FRONTIER_RULES.scatterSymbol as ArtSymbol} /><div><h4>WANTED saves progress.</h4><p>Posters build your ante anywhere on the window. They do not need a payline.</p></div></article>
        </div></div>
        <div className="frontier-howto-section-heading"><h4>All {PAYLINES.length} paying paths</h4><p>Read each map from left to right. Its row shape is the path your matching symbols must follow.</p></div>
        <PaylineMap />
      </section>

      <section className="frontier-howto-page" id={`${id}-panel-ante`} role="tabpanel" aria-labelledby={`${id}-tab-ante`} hidden={chapter !== 'ante'} tabIndex={0}>
        <div className="frontier-howto-page__heading"><span className="frontier-howto-kicker">Saved ante chips</span><h3>{chapters[1].title}</h3><p>WANTED posters on your paid spins fill the physical chip rail. Your earned progress stays saved.</p></div>
        <div className="frontier-howto-earn"><PosterAward posters={OUTLAW_RULES.ante.wantedForOneChip} chips={1} /><PosterAward posters={OUTLAW_RULES.ante.wantedForTwoChips} chips={2} plus /></div>
        <figure className="frontier-howto-chip-rail"><div aria-hidden="true">{Array.from({ length: OUTLAW_RULES.ante.requiredChips }, (_, index) => <i key={index} />)}</div><figcaption><strong>{OUTLAW_RULES.ante.requiredChips} chips: your seat is ready.</strong><span>Use Spin to deal into {OUTLAW_RULES.hand.deals} included Outlaw deals. Extra chips carry forward.</span></figcaption></figure>
        <div className="frontier-howto-funding"><span className="frontier-howto-stamp">The stake ledger</span><h4>Oldest chips fund the next hand.</h4>
          <ol><li>Each chip carries the wager of the paid spin that earned it.</li><li>The oldest {OUTLAW_RULES.ante.requiredChips} chips are consumed first: first in, first out.</li><li>Their average wager is <strong>rounded down</strong> to the legal wager step, currently {formatPoints(FRONTIER_RULES.stepPoints)}.</li><li>The resulting hand stake is shown before you deal in. Remaining chips keep their funding.</li></ol>
          <p>With mixed wagers, the displayed hand stake can be lower than the unrounded average. Your included deals charge no additional wager.</p>
        </div>
      </section>

      <section className="frontier-howto-page" id={`${id}-panel-hand`} role="tabpanel" aria-labelledby={`${id}-tab-hand`} hidden={chapter !== 'hand'} tabIndex={0}>
        <div className="frontier-howto-page__heading"><span className="frontier-howto-kicker">Outlaw’s Hand</span><h3>{chapters[2].title}</h3><p>Build a hand from the actual cards in the same {FRONTIER_RULES.columns} reels. You get {OUTLAW_RULES.hand.deals} included deals.</p></div>
        <div className="frontier-howto-steps">
          <article><div className="frontier-howto-step-heading"><span>Pick</span><h4>Choose on the reels.</h4></div><ReelIllustration length={FRONTIER_RULES.columns} selection /><p>Pick one of the {FRONTIER_RULES.rows} visible cards in every open reel. You may change your picks before confirming.</p></article>
          <article><div className="frontier-howto-step-heading"><span>Keep</span><h4>Submit your choices.</h4></div><CardHand cards={[ordinaryCards[0], ordinaryCards[0], ordinaryCards[2], ordinaryCards[3], FRONTIER_RULES.wildSymbol as ArtSymbol]} /><p><b>Keep</b> confirms your picks. Each hand position belongs to its matching reel.</p></article>
          <article><div className="frontier-howto-step-heading"><span>Hold + Spin</span><h4>Commit before the next deal.</h4></div><CardHand cards={[ordinaryCards[0], ordinaryCards[0], undefined, undefined, FRONTIER_RULES.wildSymbol as ArtSymbol]} held={[0, 1, FRONTIER_RULES.columns - 1]} /><p>Tap hand cards to <b>HOLD</b> them, then Spin. Every reel deals again; held hand cards stay. Every unheld position must take a card from its own newly revealed reel.</p></article>
          <article><div className="frontier-howto-step-heading"><span>Show Hand</span><h4>The last confirmation pays.</h4></div><CardHand cards={handIllustrations['full-house']} /><p>On the final deal, pick the open positions and use <b>Show Hand</b>. The resulting rank combination awards once at your saved hand stake.</p></article>
        </div>
        <div className="frontier-howto-rule-strip"><p><strong>Hand awards only.</strong> Feature grids do not pay ordinary paylines.</p><p><strong>Rank combinations.</strong> No straights or flushes. J♣, Q♥, K♦ and A♠ keep their shown suits.</p><p><strong>WILD substitutes for a rank.</strong> The top award needs {FRONTIER_RULES.columns} actual WILD symbols.</p></div>
      </section>

      <section className="frontier-howto-page" id={`${id}-panel-awards`} role="tabpanel" aria-labelledby={`${id}-tab-awards`} hidden={chapter !== 'awards'} tabIndex={0}>
        <div className="frontier-howto-page__heading"><span className="frontier-howto-kicker">The award ledger</span><h3>{chapters[3].title}</h3><p>Base multipliers use your paid spin’s wager. Outlaw multipliers use the saved hand stake shown at the table.</p></div>
        <div className="frontier-howto-section-heading"><h4>Outlaw’s Hand · × saved hand stake</h4><p>WILDs take the most valuable valid rank substitution. {FRONTIER_RULES.columns} actual WILDs have their own top award.</p></div>
        <div className="frontier-howto-award-hands">{Object.entries(OUTLAW_RULES.hand.awards).map(([category, multiplier]) => <article key={category}><div><h4>{HAND_NAMES[category]}</h4><strong>{multiplier}×</strong></div><CardHand cards={handIllustrations[category]} compact /></article>)}</div>
        <div className="frontier-howto-section-heading"><h4>Base reels · × paid wager per path</h4><p>The highest eligible match pays once on each winning path.</p></div>
        <table className="frontier-howto-paytable"><thead><tr><th scope="col">Symbol</th>{matchLengths.map(length => <th key={length} scope="col">{length} match</th>)}</tr></thead><tbody>{payingSymbols.map(symbol => <tr key={symbol.id}><th scope="row"><SymbolArt symbol={symbol.id as ArtSymbol} /><span>{symbol.label}</span></th>{matchLengths.map(length => <td key={length}>{PAYTABLE[symbol.id][length]}×</td>)}</tr>)}</tbody></table>
      </section>
    </div>
    <footer className="frontier-howto__footer"><button type="button" disabled={chapterIndex === 0} onClick={() => selectChapter(chapters[chapterIndex - 1].id)}>Previous</button><output aria-live="polite">{chapterIndex + 1} / {chapters.length}</output><button type="button" onClick={() => chapterIndex === chapters.length - 1 ? onClose() : selectChapter(chapters[chapterIndex + 1].id)}>{chapterIndex === chapters.length - 1 ? 'Back to reels' : 'Next chapter'}</button></footer>
  </dialog>
}
