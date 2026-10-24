import { StrictMode, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import { CreditHoldemGameHeader } from '../../../../../../fortuneforge.client/src/pages/cards/texasHoldem/CreditTexasHoldemPage'
import { PlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/shared/PlayingCard'
import { toCreditHoldemPlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/texasHoldem/creditHoldemApi'
import { useCardAudioClick } from '../../../../../../fortuneforge.client/src/games/cards/shared/cardAudio'
import '../../../../../../fortuneforge.client/src/games/cards/shared/playingCards.css'
import { HoldemTableSurface } from '../HoldemTableSurface'
import { HoldemActionBar, HoldemLobby, HoldemQueue } from '../index'
import type { HoldemCard, HoldemSeat, HoldemTable, HoldemTableRule } from '../contracts'
import '../holdem.css'
import './preview.css'

const rule: HoldemTableRule = { id: 'standard', name: 'Classic', description: 'Automatic blinds · no ante', smallBlindCredits: .5, bigBlindCredits: 1, anteCredits: 0, maximumTableStackCredits: 100 }
const cards = (...values: [string, 'hearts' | 'diamonds' | 'clubs' | 'spades'][]): HoldemCard[] => values.map(([rank, suit]) => ({rank, suit, hidden:false}))
const seat = (position: number, name: string, stack: number, committed: number, action: string): HoldemSeat => ({
  seatId:`preview-${position}`,displayName:name,seat:position,startingStack:10000,stack,committed,committedRound:committed,status:action === 'fold' ? 'folded' : 'active',lastAction:action,
  holeCards:position === 0 ? cards(['K','hearts'],['Q','hearts']) : [{hidden:true},{hidden:true}], isCurrentPlayer:position === 0,
})
const initial: HoldemTable = {
  matchId:'design-preview',status:'active',street:'turn',handNumber:7,dealerSeat:3,activeSeat:0,pot:2450,currentBet:500,minimumRaiseTo:1000,maximumRaiseTo:8250,
  communityCards:cards(['A','hearts'],['10','clubs'],['J','hearts'],['3','diamonds']),
  seats:[seat(0,'You',8250,250,'call'),seat(1,'Demo West',7950,500,'raise'),seat(2,'Demo North',8800,0,'check'),seat(3,'Demo East',9400,0,'fold'),seat(4,'Demo South',8750,250,'call')],
  legalActions:['fold','call','raise'],winningSeatIds:[],winningAmount:0,remainingActionMilliseconds:25000,startedAtUtc:'2026-10-24T12:00:00Z',matchDeadlineAtUtc:'2026-10-24T13:00:00Z',tableRule:rule,
}
function previewBuyInTable(selectedRule: HoldemTableRule, amount: number, count: number): HoldemTable {
  const cents=Math.round(amount*100), smallBlind=Math.round(selectedRule.smallBlindCredits*100), bigBlind=Math.round(selectedRule.bigBlindCredits*100), ante=Math.round(selectedRule.anteCredits*100)
  // Opponents have called around to the viewer's small blind; all amounts are
  // internally consistent fixture chips, never account/API transactions.
  const players=initial.seats.slice(0,count).map((player,index)=>{
    const starting=index===0?cents:Math.round(selectedRule.maximumTableStackCredits*100)
    const paidAnte=Math.min(starting,ante), committedRound=Math.min(starting-paidAnte,index===0?smallBlind:bigBlind), committed=paidAnte+committedRound
    return {...player,startingStack:starting,stack:starting-committed,committed,committedRound,status:starting===committed?'all-in':'active',lastAction:index===0?'small blind':index===1?'big blind':'call'}
  })
  const viewer=players[0], currentBet=Math.max(...players.map(player=>player.committedRound)), maximumRaiseTo=viewer.stack+viewer.committedRound, minimumRaiseTo=currentBet+bigBlind
  return {...initial,tableRule:selectedRule,street:'preflop',handNumber:1,communityCards:[],dealerSeat:count===2?0:count-1,activeSeat:0,
    pot:players.reduce((sum,player)=>sum+player.committed,0),currentBet,minimumRaiseTo,maximumRaiseTo,
    shortAllInRaiseTo:maximumRaiseTo>currentBet&&maximumRaiseTo<minimumRaiseTo?maximumRaiseTo:null,
    legalActions:viewer.status==='all-in'?[]:['fold',viewer.committedRound<currentBet?'call':'check',...(maximumRaiseTo>currentBet?['raise' as const]:[])],seats:players}
}
function renderCard(card: HoldemCard,index:number,scope:string) {
  const value=toCreditHoldemPlayingCard(card,index,scope)
  return <span className="ff-card-slot"><PlayingCard card={value ?? {id:`${scope}-${index}-hidden`,rank:1,suit:'spades'}} faceDown={!value}/></span>
}
function App() {
  const [view,setView]=useState('table')
  const [seatCount,setSeatCount]=useState(5)
  const [table,setTable]=useState(initial)
  const [selected,setSelected]=useState('standard')
  const [buyInAmount,setBuyInAmount]=useState('100.00')
  const [action,setAction]=useState('')
  const [muted,setMuted]=useState(false)
  const clickAudio=useCardAudioClick()
  const previewTable={...table,seats:table.seats.slice(0,seatCount),dealerSeat:Math.min(table.dealerSeat,seatCount-1)}
  const displayed=view==='result' ? {...previewTable,status:'completed',street:'showdown',activeSeat:-1,communityCards:cards(['A','hearts'],['10','clubs'],['J','hearts'],['3','diamonds'],['9','hearts']),winningSeatIds:['preview-0'],winningAmount:2450,legalActions:[],seats:previewTable.seats.map(player=>player.isCurrentPlayer ? {...player,handName:'Flush'} : player)} : previewTable
  return <InGameShell title="Texas Hold’em" theme="cards" bodyClassName="ff-holdem-shell-body" actions={<><label className="holdem-preview-view">Preview<select aria-label="Preview view" value={view} onChange={event=>{setView(event.target.value);setTable(initial);setAction('')}}><option value="table">Table</option><option value="lobby">Lobby</option><option value="result">Result</option><option value="queue">Queue</option></select></label><label className="holdem-preview-view">Seats<select aria-label="Preview seats" value={seatCount} onChange={event=>{setSeatCount(Number(event.target.value));setTable(initial);setAction('')}}>{[2,3,4,5].map(count=><option value={count} key={count}>{count}</option>)}</select></label></>}>
    <div className="ff-holdem-page ff-holdem-preview" onClickCapture={muted ? undefined : clickAudio}><main className="ff-holdem-main">
      <CreditHoldemGameHeader muted={muted} onToggleSound={()=>setMuted(value=>!value)} tableRules={[rule]} actionDeadlineSeconds={25}/>
      {view==='lobby' ? <HoldemLobby rules={[rule,{...rule,id:'ante',name:'Ante',description:'Blinds + table ante',anteCredits:.25}]} selectedRuleId={selected} balanceCredits={1000} busy={false} buyInAmount={buyInAmount} onBuyInAmountChange={setBuyInAmount} onSelectRule={id=>{setSelected(id);setBuyInAmount('100.00')}} onJoin={(selectedRule,amount)=>{setTable(previewBuyInTable(selectedRule,amount,seatCount));setView('table')}}/>
        : view==='queue' ? <HoldemQueue busy={false} onCancel={()=>setView('lobby')}/>
        : <section className={view==='result'?'ff-holdem-result':'ff-holdem-match'}>
          <HoldemTableSurface table={displayed} renderCard={renderCard}/>
          {view==='result' ? <><div className="ff-holdem-result__outcome" role="status"><strong>R24.50 collected</strong></div><div className="ff-holdem-result__controls"><button className="ff-holdem-primary" onClick={()=>{setTable(initial);setView('table')}}>Next hand</button><button onClick={()=>setView('lobby')}>Leave table</button></div></>
            : <HoldemActionBar table={displayed} busy={false} onAction={(type,amount)=>{setAction(`${type}${amount ? ` to R${(amount/100).toFixed(2)}` : ''}`);setTable({...table,activeSeat:1,legalActions:[],seats:table.seats.map(player=>player.isCurrentPlayer?{...player,lastAction:type,status:type==='fold'?'folded':'active'}:player)})}} onLeave={()=>setView('lobby')}/>}
          {action&&<button className="holdem-preview-reset" onClick={()=>{setTable(initial);setAction('')}}>Preview: {action} · Reset</button>}
        </section>}
    </main></div>
  </InGameShell>
}

createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>)
