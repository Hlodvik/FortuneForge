import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { type AndarBaharCard, type AndarBaharGateway, type AndarBaharRound, type AndarBaharSide } from './contracts'
import { HttpAndarBaharGateway } from './httpAndarBaharGateway'
import { chipValues, type ChipValue } from './andarBaharSession'
import { useAndarBaharTable } from './useAndarBaharTable'
import chip050 from './assets/chip-050.png'
import chip1 from './assets/chip-1.png'
import chip2 from './assets/chip-2.png'
import chip5 from './assets/chip-5.png'
import chip10 from './assets/chip-10.png'
import chip25 from './assets/chip-25.png'
import chip50 from './assets/chip-50.png'
import './andarBahar.css'

export type AndarBaharGameProps = Readonly<{ gateway?: AndarBaharGateway; playerId?: string; currencySymbol?: string; onBalanceChange?: (balance: number) => void }>
const defaultGateway = new HttpAndarBaharGateway()
const chipImages: Record<ChipValue, string> = { .5: chip050, 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25, 50: chip50 }
const ranks: Record<AndarBaharCard['rank'], string> = { ace:'A',two:'2',three:'3',four:'4',five:'5',six:'6',seven:'7',eight:'8',nine:'9',ten:'10',jack:'J',queen:'Q',king:'K' }
const suits = { clubs:'♣', diamonds:'♦', hearts:'♥', spades:'♠' }
const title = (value: string) => value.charAt(0).toUpperCase() + value.slice(1)
const money = (value: number, symbol: string) => `${value < 0 ? '−' : ''}${symbol}${Math.abs(value).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}`

export function AndarBaharGame({ gateway = defaultGateway, playerId = 'standalone-practice', currencySymbol = 'R', onBalanceChange }: AndarBaharGameProps) {
  const t = useAndarBaharTable(gateway, playerId, onBalanceChange)
  const [rules, setRules] = useState(false), [historyOpen, setHistoryOpen] = useState(false)
  const settled = t.phase === 'result' && t.round !== null
  const shown = t.round?.deals.slice(0, t.visible) ?? []
  const practice = t.status?.mode === 'development-practice-andar-first'
  return <section className="ff-ab" aria-label="Andar Bahar game">
    <header className="ff-ab__header"><div className="ff-ab__brand"><span aria-hidden="true">♠</span><div><strong>FORTUNEFORGE</strong><small>THE TABLE COLLECTION</small></div></div><div className="ff-ab__tools"><button type="button" onClick={() => setHistoryOpen(true)} aria-label="Recent results"><Icon name="history"/><span>History</span></button><button type="button" aria-label={t.muted ? 'Turn sound on' : 'Mute sound'} aria-pressed={!t.muted} onClick={t.toggleMuted}><Icon name={t.muted ? 'muted' : 'sound'}/><span>Sound {t.muted ? 'off' : 'on'}</span></button><button type="button" onClick={() => setRules(true)} aria-label="How to play"><Icon name="info"/><span>How to play</span></button></div></header>
    <div className="ff-ab__table">
      <div className="ff-ab__table-head"><span className="ff-ab__table-mode"><i className={t.status?.available ? 'is-on' : ''}/>{practice ? 'PRACTICE' : t.status ? 'SOLO TABLE' : 'CONNECTING'}</span><div className="ff-ab__title"><span aria-hidden="true">✧</span><h1>Andar <i>Bahar</i></h1><span aria-hidden="true">✧</span></div><span className="ff-ab__limits">{t.status ? `${money(t.status.minimumStake,currencySymbol)} – ${money(t.status.maximumStake,currencySymbol)}` : 'One deck · two sides'}</span></div>
      <div className="ff-ab__hands">
        <Hand side="andar" cards={shown.filter(deal => deal.side === 'andar').map(deal => deal.card)} winner={settled && t.round!.winner === 'andar'}/>
        <div className="ff-ab__joker"><span className="ff-ab__joker-label">THE JOKER</span><div className="ff-ab__joker-frame"><Card card={t.round?.joker}/></div><span className="ff-ab__joker-caption">{t.round ? `Match ${ranks[t.round.joker.rank]} to win` : 'The rank to match'}</span></div>
        <Hand side="bahar" cards={shown.filter(deal => deal.side === 'bahar').map(deal => deal.card)} winner={settled && t.round!.winner === 'bahar'}/>
      </div>
      <div className="ff-ab__announcement" aria-live="polite" role="status"><span className="ff-ab__divider"/><div><strong>{settled ? `${title(t.round!.winner)} wins` : t.phase === 'dealing' ? t.visible === 0 ? 'The Joker is revealed' : `Card ${t.visible} · ${t.visible % 2 ? 'Andar' : 'Bahar'}` : t.phase === 'submitting' ? 'Your cards are on their way' : t.phase === 'recover' ? 'Restore your deal' : 'Place your bets'}</strong><small>{t.notice}</small></div><span className="ff-ab__divider"/></div>
      <div className="ff-ab__bet-zones" role="group" aria-label="Choose your betting side">{(['andar','bahar'] as const).map(side => {
        const amount = t.side === side ? t.stake : 0
        return <button type="button" key={side} className={`ff-ab__bet ff-ab__bet--${side}${amount ? ' has-bet' : ''}${settled && t.round!.winner === side ? ' is-winner' : ''}`} aria-label={`Bet on ${title(side)}, pays ${side === 'andar' ? '0.9' : '1'} to 1, current bet ${money(amount,currencySymbol)}`} disabled={!t.editable} onClick={() => t.place(side)}><span className="ff-ab__bet-copy"><small>{side === 'andar' ? 'FIRST CARD' : 'SECOND CARD'}</small><strong>{title(side)}</strong><span>PAYS <b>{side === 'andar' ? '0.9:1' : '1:1'}</b></span></span><span className="ff-ab__bet-amount"><ChipStack chips={t.side === side ? t.chips : []}/><b>{amount ? money(amount,currencySymbol) : '+ Place chip'}</b></span></button>
      })}</div>
      <div className="ff-ab__controls">
        <div className="ff-ab__chip-row"><span className="ff-ab__chip-hint">CHOOSE YOUR CHIP</span><div className="ff-ab__chips" role="group" aria-label="Choose chip value">{chipValues.map(value => <button type="button" key={value} className={'ff-ab__chip' + (value === t.selectedChip ? ' is-selected' : '')} aria-label={`${currencySymbol}${value.toFixed(2)} chip`} aria-pressed={value === t.selectedChip} disabled={!t.editable} onClick={() => t.chooseChip(value)}><img src={chipImages[value]} alt="" draggable={false}/></button>)}</div></div>
        <div className="ff-ab__actions"><div className="ff-ab__edit-actions"><button type="button" aria-label="Undo" disabled={!t.editable || !t.chips.length || t.phase === 'result'} onClick={t.undo}><Icon name="undo"/><span>Undo</span></button><button type="button" aria-label="Clear" disabled={!t.editable || !t.chips.length && !t.round} onClick={t.clear}><Icon name="clear"/><span>Clear</span></button><button type="button" aria-label="Rebet" disabled={!t.editable || !t.round} onClick={t.rebet}><Icon name="repeat"/><span>Rebet</span></button></div>{t.phase === 'dealing' ? <button type="button" className="ff-ab__deal" onClick={t.skip}>Skip deal <Icon name="forward"/></button> : <button type="button" className="ff-ab__deal" disabled={!t.status?.available || t.phase === 'loading' || t.phase === 'submitting' || t.phase === 'result' || t.phase === 'betting' && !t.stake} onClick={() => void t.submit(t.phase === 'recover')}>{t.phase === 'recover' ? 'Restore deal' : t.phase === 'submitting' ? 'Dealing…' : 'Deal cards'}<Icon name="forward"/></button>}</div>
      </div>
      <div className="ff-ab__feedback">{t.error && <span role="alert">{t.error}{!t.status && <button type="button" onClick={t.reconnect}>Reconnect</button>}</span>}{t.status && !t.status.available && <span role="alert">The table is temporarily unavailable. <button type="button" onClick={t.reconnect}>Reconnect</button></span>}</div>
      <footer className="ff-ab__wallet"><div><span>BALANCE {practice && <i>· PRACTICE</i>}</span><strong data-testid="andar-bahar-balance">{money(t.status?.balance ?? 0,currencySymbol)}</strong></div><div><span>YOUR BET</span><strong data-testid="andar-bahar-total-bet">{money(t.stake,currencySymbol)}</strong></div><div className={settled && t.round!.profit > 0 ? 'has-win' : ''}><span>LAST RETURN</span><strong>{money(settled ? t.round!.totalReturn : 0,currencySymbol)}</strong></div></footer>
      <div className="ff-ab__table-foot"><span>ONE DECK <b>·</b> ANDAR DEALT FIRST <b>·</b> FIRST RANK MATCH WINS</span><div className="ff-ab__beads" aria-label="Recent winners">{t.history.slice(-10).map(item => <i key={item.id} className={`is-${item.winner}`} title={title(item.winner)}>{item.winner === 'andar' ? 'A' : 'B'}</i>)}</div></div>
      {practice && <span className="ff-ab__practice-note">Development practice credits · no cash value</span>}
    </div>
    {t.resultOpen && t.round && <Result round={t.round} currencySymbol={currencySymbol} onClose={t.closeResult}/>}
    {rules && <Rules currencySymbol={currencySymbol} onClose={() => setRules(false)}/>}
    {historyOpen && <Modal title="Your recent results" onClose={() => setHistoryOpen(false)}><p>Results from this browser’s last {t.history.length} rounds. Each fresh deck is independent; past results do not predict the next winner.</p><div className="ff-ab__history-grid">{t.history.length ? t.history.map((item,i) => <span key={item.id} className={`is-${item.winner}`}><small>{i + 1}</small>{title(item.winner)}</span>) : <p>Your results will appear here after your first deal.</p>}</div><button type="button" className="ff-ab__deal" onClick={() => setHistoryOpen(false)}>Back to the table</button></Modal>}
  </section>
}

function Hand({ side, cards, winner }: { side: AndarBaharSide; cards: readonly AndarBaharCard[]; winner: boolean }) {
  return <section className={`ff-ab__hand ff-ab__hand--${side}${winner ? ' is-winner' : ''}`} aria-label={`${title(side)} cards`}><div className="ff-ab__pile">{cards.length ? cards.slice(-3).map((card,i,shown) => <div key={`${card.rank}-${card.suit}`} className="ff-ab__pile-card" style={{'--pile-index':i, '--pile-count':shown.length} as CSSProperties}><Card card={card}/></div>) : <div className="ff-ab__card-placeholder"><span>{side === 'andar' ? 'A' : 'B'}</span></div>}{winner && <span className="ff-ab__match-tag">MATCH</span>}</div><h2>{title(side)}</h2><span className="ff-ab__hand-count">{cards.length ? `${cards.length} ${cards.length === 1 ? 'card' : 'cards'}` : side === 'andar' ? 'Dealt first' : 'Dealt second'}</span></section>
}

// Card indexes and suit treatment follow FortuneForge's shared PlayingCard convention.
function Card({ card }: { card?: AndarBaharCard }) {
  if (!card) return <div className="ff-ab__card ff-ab__card--back" aria-label="Face down Joker"><span>FF</span></div>
  const rank = ranks[card.rank], symbol = suits[card.suit], number = Number(rank)
  const red = card.suit === 'hearts' || card.suit === 'diamonds'
  return <div className={'ff-ab__card' + (red ? ' is-red' : '')} aria-label={`${title(card.rank)} of ${card.suit}`}><span className="ff-ab__card-corner"><b>{rank}</b><i>{symbol}</i></span><div className={'ff-ab__pips' + (!Number.isFinite(number) && rank !== 'A' ? ' is-court' : '')} aria-hidden="true">{rank === 'A' ? <span className="ff-ab__ace">{symbol}</span> : Number.isFinite(number) ? <Pips number={number} symbol={symbol}/> : <span className="ff-ab__court"><b>{rank}</b><i>{symbol}</i><em>{rank === 'K' ? '♚' : rank === 'Q' ? '♛' : '♜'}</em></span>}</div><span className="ff-ab__card-corner ff-ab__card-corner--bottom" aria-hidden="true"><b>{rank}</b><i>{symbol}</i></span></div>
}
function Pips({number,symbol}:{number:number;symbol:string}) {
  const patterns: Record<number, [number,number][]> = { 2:[[50,15],[50,85]],3:[[50,15],[50,50],[50,85]],4:[[25,15],[75,15],[25,85],[75,85]],5:[[25,15],[75,15],[50,50],[25,85],[75,85]],6:[[25,15],[75,15],[25,50],[75,50],[25,85],[75,85]],7:[[25,15],[75,15],[50,33],[25,50],[75,50],[25,85],[75,85]],8:[[25,15],[75,15],[50,33],[25,50],[75,50],[50,67],[25,85],[75,85]],9:[[25,12],[75,12],[25,37],[75,37],[50,50],[25,63],[75,63],[25,88],[75,88]],10:[[25,12],[75,12],[50,24],[25,37],[75,37],[25,63],[75,63],[50,76],[25,88],[75,88]] }
  return <>{patterns[number]?.map(([x,y],i) => <span key={i} style={{left:`${x}%`,top:`${y}%`,transform:`translate(-50%,-50%)${y > 50 ? ' rotate(180deg)' : ''}`}}>{symbol}</span>)}</>
}
function ChipStack({chips}:{chips:readonly ChipValue[]}) {
  return <span className="ff-ab__stack" aria-label={`${chips.length} chips placed`}>{chips.slice(-8).map((chip,i) => <img key={i} src={chipImages[chip]} alt="" draggable={false} style={{'--chip-index':i} as CSSProperties}/>)}</span>
}
function Modal({title:heading,onClose,children,className=''}:{title:string;onClose:()=>void;children:ReactNode;className?:string}) {
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close() },[])
  return <dialog ref={dialog} className={`ff-ab__modal ${className}`} aria-label={heading} onCancel={onClose} onClick={event => { if (event.target === event.currentTarget) onClose() }}><div><button type="button" className="ff-ab__close" aria-label={className === 'ff-ab__rules' ? 'Close rules' : 'Close dialog'} onClick={onClose}>×</button><span className="ff-ab__eyebrow">FORTUNEFORGE · ANDAR BAHAR</span><h2>{heading}</h2>{children}</div></dialog>
}
function Result({round,currencySymbol,onClose}:{round:AndarBaharRound;currencySymbol:string;onClose:()=>void}) {
  const win = round.disposition === 'win'
  return <Modal title={`${title(round.winner)} wins`} onClose={onClose} className="ff-ab__result"><span className="ff-ab__result-ornament" aria-hidden="true">✧</span><div className="ff-ab__result-cards"><Card card={round.joker}/><span aria-hidden="true">=</span><Card card={round.deals.at(-1)!.card}/></div><p>{title(round.winner)} matches the Joker on card {round.deals.length}.</p><strong className="ff-ab__result-return">{win ? money(round.totalReturn,currencySymbol) : 'No return'}</strong><span className="ff-ab__result-label">{win ? 'RETURNED TO YOUR BALANCE' : `YOUR ${title(round.side).toUpperCase()} BET LOST`}</span><dl><div><dt>Your bet</dt><dd>{money(round.stake,currencySymbol)}</dd></div><div><dt>Net {win ? 'profit' : 'result'}</dt><dd>{win ? '+' : ''}{money(round.profit,currencySymbol)}</dd></div><div><dt>Balance</dt><dd>{money(round.balance,currencySymbol)}</dd></div></dl><button type="button" className="ff-ab__deal" onClick={onClose} autoFocus>Continue <Icon name="forward"/></button></Modal>
}
function Rules({currencySymbol,onClose}:{currencySymbol:string;onClose:()=>void}) {
  return <Modal title="One Joker. Two sides." onClose={onClose} className="ff-ab__rules"><p>A single standard card becomes the central <strong>Joker</strong>. Choose which side will receive the first card with the same rank. Suits never decide the winner.</p><ol><li>Choose a chip, then tap <strong>Andar</strong> or <strong>Bahar</strong> to place it. Tap again to add chips. Choosing the other side starts a new wager on that side.</li><li>Select <strong>Deal cards</strong>. The Joker turns over, then cards arrive one at a time: Andar first, Bahar second, alternating.</li><li>The first rank match ends the round. Only one side wins. There are no ties, pushes, or side bets.</li></ol><table><thead><tr><th>Side</th><th>Profit payout</th><th>Return on {money(10,currencySymbol)}</th></tr></thead><tbody><tr><td>Andar · dealt first</td><td>0.9:1</td><td>{money(19,currencySymbol)}</td></tr><tr><td>Bahar · dealt second</td><td>1:1</td><td>{money(20,currencySymbol)}</td></tr></tbody></table><p>Winning returns include your stake. A losing bet returns zero. <strong>Last return</strong> shows the amount returned; the result also shows net profit or loss. A {money(.5,currencySymbol)} winning Andar bet returns {money(.95,currencySymbol)} exactly.</p><h3>The table</h3><p>{money(.5,currencySymbol)} minimum, {money(100,currencySymbol)} maximum, in {money(.5,currencySymbol)} increments. One side per round. A freshly shuffled 52-card deck is used every round. The central “Joker” is a normal card, not a wild card. The match must arrive within 49 dealt cards.</p><h3>Odds & recovery</h3><p>Andar wins about 51.5006% of rounds; Bahar about 48.4994%. The long-run return is 97.8511% on Andar and 96.9988% on Bahar. The first side’s higher chance is reflected in its lower payout.</p><p>This solo table accepts the wager before revealing the Joker. Its rank does not change either side’s chances. The server deals and settles the entire round. <strong>Skip deal</strong> finishes the animation; it never changes the result. Refresh restores your last round. If the connection drops, select <strong>Restore deal</strong> to recover the same wager without paying twice. There is no separate cash-out stage.</p><button type="button" className="ff-ab__deal" onClick={onClose}>Back to the table <Icon name="forward"/></button></Modal>
}
function Icon({name}:{name:string}) {
  const paths:Record<string,ReactNode> = { info:<><circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10v1"/></>,history:<><path d="M3 5v5h5M4 9a8 8 0 1 1 0 7M12 7v5l3 2"/></>,sound:<><path d="m11 4-5 4H3v8h3l5 4V4Zm4 4a6 6 0 0 1 0 8m3-11a10 10 0 0 1 0 14"/></>,muted:<><path d="m11 4-5 4H3v8h3l5 4V4Zm5 5 6 6m0-6-6 6"/></>,undo:<><path d="M4 5v6h6M5 10a7 7 0 1 1 0 7"/></>,clear:<><path d="m6 6 12 12M6 18 18 6"/></>,repeat:<><path d="m18 2 4 4-4 4M2 11a8 8 0 0 1 8-5h12M6 22l-4-4 4-4m16-1a8 8 0 0 1-8 5H2"/></>,forward:<><path d="M4 12h16m-6-6 6 6-6 6"/></> }
  return <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}
