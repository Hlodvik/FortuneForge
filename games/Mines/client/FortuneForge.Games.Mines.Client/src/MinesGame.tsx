import { useEffect,useMemo,useRef,useState,type KeyboardEvent } from 'react'
import type { MinesGateway,MinesRound } from './contracts'
import { HttpMinesGateway } from './httpMinesGateway'
import { playMinesAudio,unlockMinesAudio } from './minesAudio'
import { useMinesSession } from './useMinesSession'
import { currentMinesHouseEdgeBasisPoints,legacyMinesHouseEdgeBasisPoints,minesMultiplierQuote,currentMinesPayoutPolicyVersion,legacyMinesPayoutPolicyVersion,currentMinesMaximumMultiplier } from './minesPayoutQuote'
import { currentMinesMaximumWagerCents,currentMinesMinimumWagerCents } from './minesWagerLimits'
import './mines.css'

export type MinesGameProps=Readonly<{playerId:string;gateway?:MinesGateway;onBalanceChange?:(balanceCents:number)=>void;currencySymbol?:string;showTitle?:boolean}>
const defaultGateway=new HttpMinesGateway()
export function MinesGame(props:MinesGameProps){return <MinesTable key={props.playerId} {...props}/>}
const money=(cents:number,symbol:string)=>`${symbol}${(cents/100).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}`
const multiplier=(value:number)=>`${value.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}×`
function Icon({name}:{name:'diamond'|'sound'|'mute'|'help'|'arrow'|'lock'}){
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{name==='diamond'?<><path d="m3 8 4-5h10l4 5-9 13L3 8Z"/><path d="M3 8h18M7 3l5 18 5-18"/></>:name==='help'?<><circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 1 1 4 2c-1.5 1-1.5 2-1.5 2M12 16h.01"/></>:name==='arrow'?<path d="m9 6 6 6-6 6"/>:name==='lock'?<><rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></>:<><path d="M11 4 5 9H2v6h3l6 5V4Z"/>{name==='mute'?<path d="m16 9 6 6m0-6-6 6"/>:<><path d="M15 8a6 6 0 0 1 0 8M18 4a11 11 0 0 1 0 16"/></>}</>}</svg>
}
function MinesTable({playerId,gateway=defaultGateway,onBalanceChange,currencySymbol='R',showTitle=true}:MinesGameProps){
  const {session,busy,error,needsRecovery,restore,start,reveal,cashout}=useMinesSession(gateway,playerId,onBalanceChange)
  const [wager,setWager]=useState('1.00'),[mineCount,setMineCount]=useState(3),[rulesOpen,setRulesOpen]=useState(false)
  const [muted,setMuted]=useState(()=>{try{return localStorage.getItem('fortuneforge:mines:muted')==='true'}catch{return true}})
  const [reset,setReset]=useState(false),[focusTile,setFocusTile]=useState(0),[resultDismissed,setResultDismissed]=useState(false)
  const sound=useRef(muted);sound.current=muted
  const previous=useRef<MinesRound|null>(null),rulesButton=useRef<HTMLButtonElement>(null),dialog=useRef<HTMLDialogElement>(null)
  const board=useRef<HTMLDivElement>(null),keyboardReveal=useRef(false)
  const round=reset?null:session?.round,active=round?.status==='active',terminal=!!round&&!active
  const locked=busy||needsRecovery||active,selectedMines=active?round.mineCount:mineCount
  const newWagerEdge=session?.houseEdgeBasisPoints??currentMinesHouseEdgeBasisPoints
  const displayedEdge=round?(round.houseEdgeBasisPoints??legacyMinesHouseEdgeBasisPoints):newWagerEdge
  const newWagerPolicy=session?(session.payoutPolicyVersion??legacyMinesPayoutPolicyVersion):currentMinesPayoutPolicyVersion
  const displayedPolicy=round?(round.payoutPolicyVersion??legacyMinesPayoutPolicyVersion):newWagerPolicy
  const nominalRtp=(10000-displayedEdge)/100,nominalHouseEdge=displayedEdge/100
  const maximumMultiplier=displayedPolicy===currentMinesPayoutPolicyVersion?currentMinesMaximumMultiplier:minesMultiplierQuote(12,13,displayedEdge,legacyMinesPayoutPolicyVersion)
  const minimumWagerCents=session?.minimumWagerCents??currentMinesMinimumWagerCents,maximumWagerCents=session?.maximumWagerCents??currentMinesMaximumWagerCents
  const wagerCents=Math.round(Number(wager)*100),validWager=/^\d+(?:\.\d{0,2})?$/.test(wager)&&wagerCents>=minimumWagerCents&&wagerCents<=maximumWagerCents
  const gems=round?.gemsRemaining??25-selectedMines,current=round?.currentMultiplier??1,next=round?.nextMultiplier??(!terminal?minesMultiplierQuote(selectedMines,1,newWagerEdge,newWagerPolicy):null)
  const payout=round?(active?round.cashOutCents:round.payoutCents):0
  const railMines=round?.mineCount??selectedMines
  const rail=useMemo(()=>Array.from({length:Math.min(5,25-railMines-(round?.safeReveals??0))},(_,i)=>({pick:(round?.safeReveals??0)+i+1,value:minesMultiplierQuote(railMines,(round?.safeReveals??0)+i+1,displayedEdge,displayedPolicy)})),[railMines,round?.safeReveals,displayedEdge,displayedPolicy])
  useEffect(()=>{
    if(!session?.round)return
    const r=session.round,old=previous.current
    if(r.status==='active')setReset(false)
    if(r.status==='active'||!old||old.roundId!==r.roundId){setWager((r.wagerCents/100).toFixed(2));setMineCount(r.mineCount)}
    if(old&&old.roundId===r.roundId){
      if((r.status==='lost'||r.status==='consolation')&&old.status==='active')playMinesAudio(r.payoutCents>r.wagerCents?'cashout':'mine',sound.current)
      else if(r.status==='cashed-out'||r.status==='cleared'){if(old.status==='active')playMinesAudio('cashout',sound.current)}
      else if(r.safeReveals>old.safeReveals)playMinesAudio('gem',sound.current)
    }
    if(!old||old.roundId!==r.roundId)setResultDismissed(false)
    previous.current=r
  },[session?.round])
  useEffect(()=>{if(rulesOpen)dialog.current?.showModal();else dialog.current?.close()},[rulesOpen])
  useEffect(()=>{
    if(!active||!round.tiles[focusTile]?.isRevealed)return
    const nextTile=round.tiles.find(tile=>!tile.isRevealed)
    if(nextTile)setFocusTile(nextTile.index)
  },[active,round,focusTile])
  useEffect(()=>{
    if(busy||!keyboardReveal.current)return
    keyboardReveal.current=false
    if(!active)return
    const target=round.tiles[focusTile]?.isRevealed?round.tiles.find(tile=>!tile.isRevealed)?.index:focusTile
    if(target!==undefined){setFocusTile(target);board.current?.querySelector<HTMLButtonElement>(`[data-tile="${target}"]`)?.focus()}
  },[busy,active,round,focusTile])
  function gesture(){if(!muted)unlockMinesAudio()}
  function editWager(factor:number){setWager((Math.max(minimumWagerCents,Math.min(maximumWagerCents,Math.round(wagerCents*factor)||minimumWagerCents))/100).toFixed(2))}
  async function begin(){gesture();setReset(false);setResultDismissed(false);playMinesAudio('wager',muted);await start(wagerCents,mineCount)}
  function keyTile(event:KeyboardEvent<HTMLButtonElement>,index:number){
    const delta={ArrowRight:1,ArrowLeft:-1,ArrowDown:5,ArrowUp:-5}[event.key]
    if(delta===undefined)return
    event.preventDefault();let target=index
    for(let i=0;i<25;i++){target=(target+delta+25)%25;if(!round?.tiles[target]?.isRevealed)break}
    if(round?.tiles[target]?.isRevealed)target=round.tiles.find(tile=>!tile.isRevealed)?.index??index
    setFocusTile(target);event.currentTarget.closest('.ff-mines__board')?.querySelector<HTMLButtonElement>(`[data-tile="${target}"]`)?.focus()
  }
  const message=busy?'Confirming with the table…':needsRecovery?'Restore your round to continue.':!session?'Opening the table…':terminal?round.status==='lost'?'Mine found. Your wager was lost.':round.status==='consolation'?`Mine hit. ${money(round.payoutCents,currencySymbol)} returned.`:`Round complete. ${money(round.payoutCents,currencySymbol)} returned.`:active?round.safeReveals?`${round.safeReveals} ${round.safeReveals===1?'gem':'gems'} revealed. Cash out or choose another tile.`:'Choose a tile to find your first gem.':'Set your wager and choose your mines.'
  return <section className="ff-mines" aria-label="Mines casino game">
    <header className="ff-mines__heading"><div className="ff-mines__identity"><span className="ff-mines__mark"><Icon name="diamond"/></span><div><span className="ff-mines__eyebrow">FORTUNEFORGE ORIGINALS</span>{showTitle?<h1>Mines</h1>:<strong className="ff-mines__name">Mines</strong>}</div></div><div className="ff-mines__heading-right"><div className="ff-mines__wallet"><span>{session?.practice?'PRACTICE BALANCE':'BALANCE'}</span><strong data-testid="mines-balance">{session?money(session.balanceCents,currencySymbol):'—'}</strong></div><button className="ff-mines__icon-button" aria-label={muted?'Turn sound on':'Mute sound'} aria-pressed={!muted} onClick={()=>{const value=!muted;setMuted(value);if(!value)unlockMinesAudio();try{localStorage.setItem('fortuneforge:mines:muted',String(value))}catch{/* optional preference */}}}><Icon name={muted?'mute':'sound'}/></button><button ref={rulesButton} className="ff-mines__icon-button" aria-label="How to play Mines" onClick={()=>setRulesOpen(true)}><Icon name="help"/></button></div></header>
    <div className="ff-mines__table">
      <aside className="ff-mines__controls" aria-label="Wager controls">
        <div className="ff-mines__mode"><span className="ff-mines__mode-dot"/>Manual play<span>5 × 5</span></div>
        <div className="ff-mines__wager-group"><label htmlFor={`mines-wager-${playerId}`}>Wager amount<span>{currencySymbol}</span></label><div className="ff-mines__input-row"><input id={`mines-wager-${playerId}`} aria-label="Wager amount" inputMode="decimal" autoComplete="off" value={wager} disabled={locked} onChange={event=>setWager(event.target.value)} onBlur={()=>{if(validWager)setWager((wagerCents/100).toFixed(2))}}/><button disabled={locked} aria-label="Halve wager" onClick={()=>editWager(.5)}>½</button><button disabled={locked} aria-label="Double wager" onClick={()=>editWager(2)}>2×</button></div><small className="ff-mines__limits">{money(minimumWagerCents,currencySymbol)} – {money(maximumWagerCents,currencySymbol)}</small></div>
        <div className="ff-mines__counts"><div><label htmlFor={`mines-count-${playerId}`}>Mines {active&&<Icon name="lock"/>}</label><select id={`mines-count-${playerId}`} aria-label="Mine count" disabled={locked} value={selectedMines} onChange={event=>setMineCount(Number(event.target.value))}>{Array.from({length:24},(_,i)=><option key={i+1} value={i+1}>{i+1}</option>)}</select></div><div><span className="ff-mines__label">Gems left</span><div className="ff-mines__gem-count"><span className="ff-mines__sprite ff-mines__sprite--gem"/>{gems}</div></div></div>
        <div className="ff-mines__return"><span>{terminal?'Total returned':'Cash-out value'}<strong>{multiplier(current)}</strong></span><output data-testid="mines-cashout-value">{money(payout,currencySymbol)}</output>{active&&(round.nextMinePayoutCents??0)>0&&<p className="ff-mines__mine-return" aria-live="polite">If you hit a mine: <strong data-testid="mines-next-mine-return">{money(round.nextMinePayoutCents!,currencySymbol)}</strong> returned</p>}</div>
        <div className="ff-mines__primary-actions">{needsRecovery?<button className="ff-mines__primary" disabled={busy} onClick={()=>void restore()}>Restore round <Icon name="arrow"/></button>:active?<button className="ff-mines__primary" disabled={busy||!round.canCashOut} onClick={()=>{gesture();void cashout()}}>{busy?'Confirming…':'Cash out'}{round.canCashOut&&!busy&&<span>{money(round.cashOutCents,currencySymbol)}</span>}</button>:<button className="ff-mines__primary" disabled={busy||!session||!validWager||wagerCents>(session.balanceCents??0)} onClick={()=>void begin()}>{busy?'Opening…':terminal?'Bet again':'Place wager'}<Icon name="arrow"/></button>}
        {terminal&&<button className="ff-mines__secondary" disabled={busy||needsRecovery} onClick={()=>{setReset(true);setResultDismissed(false)}}>Clear board</button>}</div>
        <div className="ff-mines__helper">{active?<><Icon name="lock"/>Wager & mines locked for this round</>:<><Icon name="diamond"/>Find gems. Avoid mines. Cash out.</>}</div>
        {!validWager&&!active&&<p className="ff-mines__form-error">Enter {money(minimumWagerCents,currencySymbol)}–{money(maximumWagerCents,currencySymbol)}, with up to two decimal places.</p>}
        {validWager&&!active&&session&&wagerCents>session.balanceCents&&<p className="ff-mines__form-error">Not enough credits for this wager.</p>}
        <div className="ff-mines__practice-note">{session?.practice?<><span>Practice mode</span>Play credits only · no cash value</>:<><span>Nominal RTP {nominalRtp}% · House edge {nominalHouseEdge}%</span>Final returns round down to cents</>}</div>
      </aside>
      <div className="ff-mines__playfield">
        <div className="ff-mines__board-top"><div><span className="ff-mines__live-dot"/>{active?'ROUND IN PLAY':terminal?'ROUND COMPLETE':'READY TO PLAY'}</div><div>Next gem <strong data-testid="mines-next">{next===null?'—':multiplier(next)}</strong></div></div>
        <div className="ff-mines__board-wrap"><div ref={board} className={`ff-mines__board${round?.status==='lost'?' is-lost':''}`} aria-label="Mines board, 5 rows and 5 columns" role="group">{Array.from({length:25},(_,i)=>{
          const tile=round?.tiles[i],kind=tile?.kind??'hidden',picked=tile?.isRevealed??false
          const label=`Tile ${i+1}, row ${Math.floor(i/5)+1} column ${i%5+1}, ${kind==='hidden'?'covered':kind==='gem'?'gem':'mine'}${picked?', revealed':''}`
          return <button key={`${round?.roundId??'ready'}-${i}`} type="button" data-tile={i} aria-label={label} disabled={!active||busy||needsRecovery||picked} tabIndex={active&&!picked?(focusTile===i?0:-1):-1} className={`ff-mines__tile ff-mines__tile--${kind}${picked?' is-picked':''}${terminal&&!picked?' is-unchosen':''}`} onFocus={()=>setFocusTile(i)} onKeyDown={event=>keyTile(event,i)} onClick={event=>{gesture();keyboardReveal.current=event.detail===0;void reveal(i)}}>{kind==='hidden'?<span className="ff-mines__tile-engraving"><Icon name="diamond"/></span>:<span className={`ff-mines__sprite ff-mines__sprite--${kind}`}/>}</button>
        })}</div>{terminal&&!resultDismissed&&<div className={`ff-mines__result ${round.status==='lost'?'ff-mines__result--loss':round.status==='consolation'?'ff-mines__result--consolation':''}`} role="status"><button aria-label="Dismiss round result" onClick={()=>setResultDismissed(true)}>×</button><span>{round.status==='lost'?'MINE FOUND':round.status==='consolation'?'MINE HIT':round.status==='cleared'?'ALL GEMS FOUND':'CASHED OUT'}</span><strong>{round.status==='lost'?'Round over':round.status==='consolation'?'Returned':multiplier(round.currentMultiplier)}</strong><b>{round.status==='lost'?`${money(round.wagerCents,currencySymbol)} wager lost`:money(round.payoutCents,currencySymbol)}</b></div>}</div>
        <div className="ff-mines__payout-rail" aria-label="Upcoming gem multipliers">{rail.map((step,i)=><div key={step.pick} className={i===0?'is-next':''}><span>{i===0?'NEXT GEM':`${step.pick} GEMS`}</span><strong>{multiplier(step.value)}</strong></div>)}</div>
        <p className="ff-mines__status" role="status" aria-live="polite">{message}</p>
      </div>
    </div>
    {error&&<div className="ff-mines__error" role="alert">{error}<button disabled={busy} onClick={()=>void restore()}>Retry / restore</button></div>}
    <footer className="ff-mines__footer"><span><span className="ff-mines__footer-diamond">◇</span> MINES <b>·</b> SINGLE PLAYER</span><span>{nominalRtp}% nominal RTP <b>·</b> {nominalHouseEdge}% house edge <b>·</b> Rounded down to cents</span></footer>
    <dialog ref={dialog} className="ff-mines__rules" onCancel={()=>setRulesOpen(false)} onClose={()=>{setRulesOpen(false);rulesButton.current?.focus()}}>
      <button className="ff-mines__rules-close" aria-label="Close rules" onClick={()=>setRulesOpen(false)}>×</button>
      <span className="ff-mines__eyebrow">HOW TO PLAY</span><h2>Every gem is a choice.</h2>
      <ol>
        <li>Choose a wager and 1–24 mines. Place your wager to start.</li>
        <li>Pick covered tiles. Each gem unlocks the displayed cash-out value. A mine ends the round; {displayedPolicy===2?'early mines lose your wager, while later mines may return the amount shown before your next pick.':'your wager is lost.'}</li>
        <li>After any gem, cash out the displayed return or reveal another tile. You can keep playing until every safe tile is found; a full clear pays automatically.</li>
      </ol>
      <p>Wager and mine count stay locked during play. The board is fixed before your first pick. There are no clues or safe patterns.</p>
      <p>Nominal RTP: {nominalRtp}%. Nominal house edge: {nominalHouseEdge}%.
        {displayedPolicy===2?' The gem curve rises normally, then stretches toward a maximum of 50× on higher-risk boards. Later mine-return payments preserve the nominal return while keeping every safe tile playable. There is no early automatic cash-out.':<> Gross multiplier = {((10000-displayedEdge)/10000).toFixed(2)} ÷ survival probability. Survival after k gems = C(25 − mines, k) ÷ C(25, k).</>}
        {' '}Returns include your wager and round down once to whole cents; rounding can increase the effective edge above {nominalHouseEdge}%.</p>
      {round&&(displayedEdge!==newWagerEdge||displayedPolicy!==newWagerPolicy)&&<p>This accepted round keeps its original rules. New wagers use {(10000-newWagerEdge)/100}% nominal RTP and {newWagerEdge/100}% nominal house edge.{newWagerPolicy===2?' New rounds have a 50× maximum and later mine-return payments.':''}</p>}
      {round&&(round.wagerCents<minimumWagerCents||round.wagerCents>maximumWagerCents)&&<p>Your accepted {money(round.wagerCents,currencySymbol)} wager keeps its original terms. New wagers must be {money(minimumWagerCents,currencySymbol)}–{money(maximumWagerCents,currencySymbol)}.</p>}
      <p>With 1 or 2 mines, the first safe cash-out can return less than your original wager. Check the displayed return before playing or cashing out.</p>
      <p>Wagers: {money(minimumWagerCents,currencySymbol)}–{money(maximumWagerCents,currencySymbol)}. Largest possible multiplier: {maximumMultiplier.toLocaleString('en-US')}×. Maximum gross return: {money(maximumMultiplier*Math.max(maximumWagerCents,round?.wagerCents??0),currencySymbol)}. Cash-out requires at least one gem.</p>
      {displayedPolicy===2&&<p>Up to 50× includes your stake: a {money(maximumWagerCents,currencySymbol)} wager can return up to {money(maximumWagerCents*50,currencySymbol)}. One-mine and 24-mine full clears pay 22.5×; other full clears pay 50×. A mine-return payment may be below, equal to, or above your original wager. Check both the next-gem multiplier and the next-mine return before continuing.</p>}
      <p>Keyboard: Tab to the board; arrow keys move between covered tiles; Enter or Space reveals. Your active round resumes after refresh. If a connection drops, restore the round before playing again.</p>
      {session?.practice&&<p><strong>Practice uses play credits with no cash value.</strong></p>}
      <button className="ff-mines__primary" onClick={()=>setRulesOpen(false)}>Got it</button>
    </dialog>
  </section>
}
