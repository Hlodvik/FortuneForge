import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react'
import { type SicBoGateway, type SicBoRound } from './contracts'
import { HttpSicBoGateway } from './httpSicBoGateway'
import { availableChipValues, betLabel, betTargets, targetsForKind, type SicBoBetTarget } from './sicBoPresentation'
import { playSicBoSound, type SicBoAudioCue } from './sicBoAudio'
import { useSicBoTable, type SicBoRecoveryMode } from './useSicBoTable'
import './sicBo.css'
import './sicBoViewport.css'
export type SicBoGameProps = Readonly<{ gateway?: SicBoGateway; playerId?: string; currencySymbol?: string; showTitle?: boolean; roundOwnerId?: string; recoveryMode?: SicBoRecoveryMode; onBalanceChange?: (balance: number) => void }>
const defaultGateway=new HttpSicBoGateway()
export function SicBoGame(props: SicBoGameProps) {const scope=props.roundOwnerId&&props.recoveryMode?props.roundOwnerId+':'+props.recoveryMode:props.playerId??'guest';return <SicBoSession key={scope} {...props} scope={scope}/>}
function SicBoSession({gateway=defaultGateway,scope,currencySymbol='R',showTitle=true,roundOwnerId,recoveryMode,onBalanceChange}: SicBoGameProps&{scope:string}) {
  const table=useSicBoTable({gateway,scope,roundOwnerId,recoveryMode,onBalanceChange})
  const [panel,setPanel]=useState<'slip'|'rules'|'history'|null>(null)
  const [lastRound,setLastRound]=useState<SicBoRound|null>(null)
  const dialog=useRef<HTMLDialogElement>(null)
  const opener=useRef<HTMLButtonElement|null>(null)
  const primary=useRef<HTMLButtonElement>(null)
  const stakeInput=useRef<HTMLInputElement>(null)
  const focusNext=useRef<'stake'|'roll'|null>(null)
  const rolledHere=useRef(false)
  const announcedRound=useRef<string|null>(null)
  const money=(value:number)=>currencySymbol+value.toLocaleString('en-ZA',{minimumFractionDigits:2,maximumFractionDigits:2})
  useEffect(()=>{setLastRound(null);setPanel(null);rolledHere.current=false;announcedRound.current=null},[gateway,scope])
  useEffect(()=>{if(table.phase==='settled'&&table.round){setLastRound(table.round);if(rolledHere.current&&announcedRound.current!==table.round.roundId){announcedRound.current=table.round.roundId;rolledHere.current=false;if(table.round.profit>0)playSicBoSound('win')}}},[table.phase,table.round])
  useEffect(()=>{const node=dialog.current;if(panel&&!node?.open)node?.showModal();else if(!panel&&node?.open)node.close()},[panel])
  function close(){setPanel(null);opener.current?.focus()}
  function open(value: 'slip'|'rules'|'history',event:React.MouseEvent<HTMLButtonElement>){opener.current=event.currentTarget;setPanel(value)}
  const settled=table.phase==='settled'
  const showing=table.round??(table.phase==='ready'?lastRound:null)
  const dice=showing?.dice??null
  const count=table.round?table.revealed:3
  const totalVisible=showing&&(!table.round||settled)
  const disabled=!table.canAdd
  useLayoutEffect(()=>{if(table.phase==='ready'&&focusNext.current){if(focusNext.current==='roll'&&primary.current&&!primary.current.disabled)primary.current.focus();else stakeInput.current?.focus();focusNext.current=null}},[table.phase])
  const repeat=()=>{focusNext.current='roll';table.prepare(true)}
  const fresh=()=>{focusNext.current='stake';table.prepare(false)}
  const roll=()=>{rolledHere.current=true;void table.roll()}
  const notice=table.error??(table.phase==='recovering'?'Restoring result…':table.phase==='loading'?'Connecting…':table.phase==='requesting'?'Rolling…':table.phase==='revealing'?'Revealing dice…':!table.unlocked&&!settled?'Table unavailable.':table.stakeValue===null?'Enter a valid stake.':table.slip.length===table.status?.maximumBetsPerRound?'Bet limit reached.':!table.canAdd&&table.unlocked?'Insufficient balance.':'')
  function resultFor(target:SicBoBetTarget){if(!settled||!table.round)return null;const selected=table.round.settlements.filter(b=>matches(target,b));return selected.length?selected.some(b=>b.won)?'win':'loss':null}
  function cell(target:SicBoBetTarget){const amount=table.slip.filter(b=>matches(target,b)).reduce((sum,b)=>sum+b.stake,0);const result=resultFor(target);const short=target.kind==='total'?String(target.total):target.label;return <button key={target.id} type="button" className={'ff-sic-bo__cell '+(amount?'has-bet ':'')+(result?'is-'+result:'')} disabled={disabled} aria-label={'Add '+target.label+' bet'} onClick={()=>table.add(target.id)} data-target={target.id} data-kind={target.kind} title={target.label+' · '+target.coverageText+' · '+target.odds}>
    <BetGlyph target={target} fallback={short}/>
    <small>{target.kind==='single-number'?'1 / 2 / 12:1':target.odds}</small>
    {amount>0&&<span className="ff-sic-bo__marker" aria-label={money(amount)+' staked'}>{compact(amount)}</span>}
    {result&&<span className="ff-sic-bo__result-mark" aria-label={result==='win'?'Winning bet':'Losing bet'}>{result==='win'?'✓':'×'}</span>}
  </button>}
  function trapPanel(event:KeyboardEvent<HTMLDialogElement>){if(event.key!=='Tab')return;const nodes=Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select:not(:disabled),a[href],summary,[tabindex="0"]')).filter(node=>node.getClientRects().length>0);const first=nodes[0],last=nodes.at(-1);if(!first)return;if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus()}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus()}}
  function preventSelection(event:KeyboardEvent){if(event.key==='Escape'&&panel){event.preventDefault();close()}}
  function playControlSound(event:MouseEvent<HTMLElement>){if(!(event.target instanceof Element))return;const control=event.target.closest<HTMLButtonElement>('button');if(!control||control.disabled)return;playSicBoSound((control.dataset.sicBoAudio as SicBoAudioCue|undefined)??'click')}
  return <main className={'ff-sic-bo is-'+table.phase} aria-busy={['requesting','revealing','recovering'].includes(table.phase)} onKeyDown={preventSelection} onClick={playControlSound}>
    <header className="ff-sic-bo__header">{showTitle&&<h1>Sic Bo</h1>}<nav aria-label="Table details"><button type="button" onClick={e=>open('slip',e)}>Slip <span>{table.slip.length}</span></button><button type="button" onClick={e=>open('history',e)}>History</button><button type="button" onClick={e=>open('rules',e)}>Rules</button></nav></header>
    <section className="ff-sic-bo__layout" aria-label="Sic Bo betting table">
      <div className="ff-sic-bo__roll-area">
        <div className="ff-sic-bo__stage-flare" aria-hidden="true"/>
        <div className="ff-sic-bo__dice-plinth">
          <div className="ff-sic-bo__dice" role="img" aria-label={totalVisible?'Dice '+dice!.join(', '):table.round?'Dice revealing':'Dice tray'}>{[0,1,2].map(i=><Die key={i} face={dice&&i<count?dice[i]!:null} rolling={table.phase==='requesting'||table.phase==='revealing'&&i>=count}/>)}</div>
        </div>
        <div className="ff-sic-bo__roll-total" role="status">{totalVisible?<><strong>{showing!.total}</strong><span>{showing!.isTriple?'Triple':showing!.total<=10?'Small':'Big'}</span></>:<strong>—</strong>}</div>
        <div className="ff-sic-bo__return">{settled&&<><span>Return <b>{money(table.round!.totalReturn)}</b></span><strong className={table.round!.profit>=0?'is-win':'is-loss'}>{table.round!.profit>0?'+':''}{money(table.round!.profit)} net</strong></>}</div>
      </div>
      <div className="ff-sic-bo__board">
        <div className="ff-sic-bo__board-left"><div className="ff-sic-bo__quick">{betTargets.filter(t=>['small','big','odd','even','any-triple'].includes(t.kind)).map(cell)}</div><div className="ff-sic-bo__faces">{(['single-number','specific-double','specific-triple'] as const).map(kind=><div className="ff-sic-bo__face-row" key={kind}><span>{kind==='single-number'?'Singles':kind==='specific-double'?'Doubles':'Triples'}</span>{targetsForKind(kind).map(cell)}</div>)}</div></div>
        <div className="ff-sic-bo__board-right"><div className="ff-sic-bo__totals" aria-label="Total bets">{targetsForKind('total').map(cell)}</div><div className="ff-sic-bo__combinations" aria-label="Two-number combination bets">{targetsForKind('two-number-combination').map(cell)}</div></div>
      </div>
    </section>
    <section className="ff-sic-bo__controls" aria-label="Bet controls"><div className="ff-sic-bo__stake"><label htmlFor="sic-stake">Stake</label><input ref={stakeInput} id="sic-stake" aria-invalid={!!table.status&&table.stakeValue===null} type="text" inputMode="decimal" autoComplete="off" value={table.stake} onChange={e=>table.setStake(e.target.value)} disabled={!table.unlocked}/><div className="ff-sic-bo__chips">{table.status&&availableChipValues(table.status).map(value=><button key={value} type="button" aria-label={'Set stake '+money(value)} aria-pressed={value===table.stakeValue} disabled={!table.unlocked} onClick={()=>table.setStake(String(value))}>{compact(value)}</button>)}</div></div>
      <div className="ff-sic-bo__summary"><span>Balance <b>{table.balance===null?'—':money(table.balance)}</b></span><span>Bet <b>{money(table.totalStake)}</b></span></div>
      <div className="ff-sic-bo__actions">{settled?<><button type="button" disabled={!table.status?.available} onClick={fresh}>New round</button><button className="ff-sic-bo__primary" ref={primary} type="button" disabled={!table.canRepeat} onClick={repeat}>Repeat bets</button></>:<><button type="button" disabled={!table.unlocked||!table.slip.length} onClick={()=>table.remove(table.slip.length-1)}>Undo</button><button className="ff-sic-bo__primary" ref={primary} type="button" disabled={!table.canRoll} data-sic-bo-audio="roll" onClick={roll}>{table.phase==='retry'?'Retry roll':'Roll dice'}</button></>}</div>
    </section>
    <div className="ff-sic-bo__notice" role={table.error?'alert':'status'}><span>{notice}</span>{table.phase==='read-failed'&&<button type="button" onClick={()=>void table.restore()} disabled={!table.pending?.roundId}>Retry restoration</button>}{(!table.status||!table.status.available||table.phase==='loading')&&table.error&&<button type="button" onClick={()=>void table.loadStatus()}>Retry connection</button>}</div>
    <dialog onKeyDown={trapPanel} className="ff-sic-bo__details" ref={dialog} onCancel={e=>{e.preventDefault();close()}} onClose={()=>{if(panel)close()}} aria-labelledby="sic-panel-title"><header><h2 id="sic-panel-title">{panel==='slip'?'Bet slip':panel==='rules'?'Rules & payouts':'History'}</h2><button type="button" onClick={close} autoFocus aria-label="Close table details">×</button></header><div className="ff-sic-bo__panel-body" tabIndex={0}>
      {panel==='slip'&&<><div className="ff-sic-bo__slip-heading"><span>{table.slip.length} / {table.status?.maximumBetsPerRound??'—'} bets · {money(table.totalStake)}</span><button type="button" disabled={!table.unlocked||!table.slip.length} onClick={table.clear}>Clear</button></div>{!table.slip.length?<p>No bets placed.</p>:<ol className="ff-sic-bo__slip">{table.slip.map((bet,i)=><li key={i}><span><strong>{betLabel(bet)}</strong><small>{money(bet.stake)}</small></span>{settled?<span className={table.round!.settlements[i]!.won?'is-win':'is-loss'}>{table.round!.settlements[i]!.won?'Won':'Lost'} · {money(table.round!.settlements[i]!.totalReturn)} return</span>:<button type="button" aria-label={'Remove bet '+(i+1)} disabled={!table.unlocked} onClick={()=>table.remove(i)}>Remove</button>}</li>)}</ol>}</>}
      {panel==='rules'&&<><p>Small 4–10 · Big 11–17. Small, Big, Odd and Even lose on triples.</p><p>Singles pay by matching dice; doubles need at least two. Combinations need both faces.</p><dl>{['small','big','odd','even','any-triple','single-number','specific-double','specific-triple','two-number-combination'].map(kind=>{const target=betTargets.find(t=>t.kind===kind)!;return <div key={kind}><dt>{kind==='single-number'?'Singles':kind==='specific-double'?'Doubles':kind==='specific-triple'?'Specific triples':kind==='two-number-combination'?'Combinations':target.label}</dt><dd>{target.odds}</dd></div>})}{targetsForKind('total').map(target=><div key={target.id}><dt>{target.label}</dt><dd>{target.odds}</dd></div>)}</dl><p>Profit odds. A winning return includes the stake.</p>{table.status&&<p>Min {money(table.status.minimumStake)} · Max {money(table.status.maximumStakePerBet)} · Step {money(table.status.stakeIncrement)} · {table.status.maximumBetsPerRound} bets.</p>}</>}
      {panel==='history'&&(!table.history.length?<p>No settled rolls yet.</p>:<ol className="ff-sic-bo__history">{table.history.map(round=><li key={round.roundId}><div className="ff-sic-bo__history-dice">{round.dice.map((face,i)=><Die face={face} small key={i}/>)}<strong>{round.total}{round.isTriple?' · Triple':''}</strong></div><span>Bet {money(round.totalStaked)} · Return {money(round.totalReturn)}</span><details><summary>{round.settlements.length} bets</summary><ol>{round.settlements.map(bet=><li key={bet.betIndex}>{betLabel(bet)} · {money(bet.stake)} · {bet.won?'Won':'Lost'} · {money(bet.totalReturn)} return</li>)}</ol></details></li>)}</ol>)}
    </div></dialog>
  </main>
}
const positions: Record<number,readonly number[]>={1:[4],2:[0,8],3:[0,4,8],4:[0,2,6,8],5:[0,2,4,6,8],6:[0,2,3,5,6,8]}
export function Die({face,small=false,rolling=false}:{face:number|null;small?:boolean;rolling?:boolean}){return <span className={'ff-sic-bo__die '+(small?'is-small ':'')+(rolling?'is-rolling':'')} aria-hidden="true">{face===null?<b>—</b>:positions[face]?.map(position=><i key={position} style={{gridRow:Math.floor(position/3)+1,gridColumn:position%3+1}}/>)}</span>}
function BetGlyph({target,fallback}:{target:SicBoBetTarget;fallback:string}){
  if(target.kind==='two-number-combination')return <span className="ff-sic-bo__bet-dice is-combination"><Die face={target.firstFace} small/><Die face={target.secondFace} small/></span>
  if(target.face!==null){const count=target.kind==='specific-triple'?3:target.kind==='specific-double'?2:1;return <span className={'ff-sic-bo__bet-dice is-'+target.kind}>{Array.from({length:count},(_,index)=><Die face={target.face} small key={index}/>)}</span>}
  return <strong>{fallback}</strong>
}
function matches(target:SicBoBetTarget,bet:{kind:string;face:number|null;total:number|null;firstFace:number|null;secondFace:number|null}){return target.kind===bet.kind&&target.face===bet.face&&target.total===bet.total&&target.firstFace===bet.firstFace&&target.secondFace===bet.secondFace}
function compact(value:number){return value>=1000?(value/1000).toLocaleString('en-ZA',{maximumFractionDigits:2})+'k':String(value)}
