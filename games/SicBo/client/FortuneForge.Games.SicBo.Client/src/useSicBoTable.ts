import { useEffect, useRef, useState } from 'react'
import { SicBoGatewayError, type SicBoBetRequest, type SicBoGateway, type SicBoRound, type SicBoStatus } from './contracts'
import { appendRollHistory, createTargetBet, historyStorageKey, legalSlip, parsePendingRoll, parseRollHistory, parseStake, pendingStorageKey, totalStake, type SicBoHistoryEntry, type SicBoPendingRoll } from './sicBoPresentation'

export type SicBoRecoveryMode = 'account' | 'practice'
type Phase = 'loading' | 'ready' | 'requesting' | 'revealing' | 'settled' | 'recovering' | 'retry' | 'read-failed'
type Model = { status: SicBoStatus | null; balance: number | null; slip: readonly SicBoBetRequest[]; round: SicBoRound | null; history: readonly SicBoHistoryEntry[]; pending: SicBoPendingRoll | null; phase: Phase; revealed: number; error: string | null; stake: string }
export type SicBoTableOptions = { gateway: SicBoGateway; scope: string; roundOwnerId?: string; recoveryMode?: SicBoRecoveryMode; onBalanceChange?: (balance: number) => void }
const empty = (scope: string): Model => {const pending=readPending(scope);return {status:null,balance:null,slip:pending?.bets??[],round:null,history:readHistory(scope),pending,phase:pending?'recovering':'loading',revealed:0,error:null,stake:'1'}}
export function useSicBoTable({gateway,scope,roundOwnerId,recoveryMode,onBalanceChange}: SicBoTableOptions) {
  const [model,setModel] = useState<Model>(()=>empty(scope))
  const current = useRef(model)
  const live = useRef(false)
  const requests = useRef(new Set<AbortController>())
  const timers = useRef<number[]>([])
  const statusRevision = useRef(0)
  const statusOperation = useRef<AbortController|null>(null)
  const restoreOperation = useRef<AbortController|null>(null)
  const callback = useRef(onBalanceChange)
  callback.current = onBalanceChange
  function publish(patch: Partial<Model>) { current.current={...current.current,...patch}; setModel(current.current) }
  function controller() { const request=new AbortController();requests.current.add(request);return request }
  function clearTimers() { timers.current.forEach(window.clearTimeout);timers.current=[] }
  function accept(round: SicBoRound, animate: boolean) {
    clearTimers()
    const reduce = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
    publish({round,slip:round.settlements.map(({kind,stake,face,total,firstFace,secondFace})=>({kind,stake,face,total,firstFace,secondFace})),pending:null,error:null,phase:animate&&!reduce?'revealing':'settled',revealed:animate&&!reduce?0:3})
    const finish = () => { if(!live.current)return; clearPending(scope); const validated=appendRollHistory(current.current.history,round);const history=validated===current.current.history&&!/^[a-f0-9]{64}$/.test(round.roundId)?[round,...validated.filter(item=>item.roundId!==round.roundId)].slice(0,12):validated;writeHistory(scope,history);publish({phase:'settled',revealed:3,balance:round.balance,history});callback.current?.(round.balance) }
    if(animate&&!reduce) {
      timers.current=[window.setTimeout(()=>{if(live.current)publish({revealed:1})},180),window.setTimeout(()=>{if(live.current)publish({revealed:2})},340),window.setTimeout(finish,520)]
    } else finish()
  }
  async function loadStatus() {
    if(statusOperation.current&&!statusOperation.current.signal.aborted)return
    const request=controller();statusOperation.current=request; const revision=++statusRevision.current; publish({error:null})
    try { const status=await gateway.getStatus(request.signal);if(request.signal.aborted||!live.current||revision!==statusRevision.current)return;const state=current.current;publish({status,balance:state.balance??status.balance,stake:state.status?state.stake:String(status.minimumStake),phase:state.phase==='loading'?'ready':state.phase,error:status.available?state.error:'Table unavailable.'}) }
    catch(reason){if(request.signal.aborted||!live.current||revision!==statusRevision.current)return;publish({error:message(reason)})}
    finally {requests.current.delete(request);if(statusOperation.current===request)statusOperation.current=null}
  }
  async function restore() {
    const pending=current.current.pending;if(!pending||restoreOperation.current&&!restoreOperation.current.signal.aborted)return
    const request=controller();restoreOperation.current=request;publish({phase:'recovering',error:null})
    try {
      if(!pending.roundId||!roundOwnerId||!recoveryMode)throw new Error('Pending result has no verified round ID.')
      if(pending.roundId!==await deriveRoundId(roundOwnerId,pending.idempotencyKey,recoveryMode))throw new Error('Pending result does not match this account.')
      if(request.signal.aborted||!live.current)return
      const round=await gateway.getRound(pending.roundId,request.signal);if(request.signal.aborted||!live.current)return
      if(!sameSlip(round.settlements,pending.bets))throw new Error('The saved slip does not match this result.')
      accept(round,false)
    } catch(reason) { if(request.signal.aborted||!live.current)return;const canRetry=reason instanceof SicBoGatewayError&&reason.status===404&&recoveryMode==='account';publish({phase:canRetry?'retry':'read-failed',error:canRetry?'Roll not confirmed. Retry the same slip.':'Result could not be restored. Retry restoration.'}) }
    finally {requests.current.delete(request);if(restoreOperation.current===request)restoreOperation.current=null}
  }
  useEffect(()=>{
    live.current=true;const state=empty(scope);current.current=state;setModel(state);void loadStatus();if(state.pending)void restore()
    return()=>{live.current=false;requests.current.forEach(request=>request.abort());requests.current.clear();clearTimers()}
    // Callback identity must never restart a paid request or a restoration.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  },[gateway,scope,roundOwnerId,recoveryMode])
  const unlocked=model.phase==='ready'&&model.status?.available===true&&!model.pending
  const stake=model.status?parseStake(model.stake,model.status):null
  const canAdd=unlocked&&stake!==null&&model.balance!==null&&legalSlip([...model.slip,createTargetBet('small',stake)!],model.status!,model.balance)
  const canRoll=model.status?.available===true&&(model.phase==='retry'?!!model.pending&&recoveryMode==='account':model.phase==='ready'&&!model.pending&&model.balance!==null&&legalSlip(model.slip,model.status,model.balance))
  function add(targetId: string) {const state=current.current;if(state.phase!=='ready'||state.pending||!state.status?.available||state.balance===null)return;const stake=parseStake(state.stake,state.status);const bet=stake===null?null:createTargetBet(targetId,stake);if(!bet||!legalSlip([...state.slip,bet],state.status,state.balance))return;publish({slip:[...state.slip,bet],error:null})}
  function remove(index: number) {const state=current.current;if(state.phase!=='ready'||state.pending)return;publish({slip:state.slip.filter((_,i)=>i!==index),error:null})}
  function clear() {if(current.current.phase==='ready'&&!current.current.pending)publish({slip:[],error:null})}
  async function roll() {
    const state=current.current
    if(!state.status?.available)return
    if(state.phase==='retry'){if(!state.pending||recoveryMode!=='account')return}
    else if(state.phase!=='ready'||state.pending||state.balance===null||!legalSlip(state.slip,state.status,state.balance))return
    const request=controller();const bets=state.pending?.bets??state.slip;const key=state.pending?.idempotencyKey??('sic-bo-'+crypto.randomUUID().replaceAll('-',''))
    let pending: SicBoPendingRoll = state.pending??{scope,idempotencyKey:key,bets}
    publish({phase:'requesting',pending,error:null})
    try {
      if(!pending.roundId&&roundOwnerId&&recoveryMode){pending={...pending,roundId:await deriveRoundId(roundOwnerId,key,recoveryMode)};if(request.signal.aborted||!live.current)return;publish({pending})}
      if(recoveryMode)writePending(scope,pending)
      if(state.pending?.roundId){try{const saved=await gateway.getRound(state.pending.roundId,request.signal);if(request.signal.aborted||!live.current)return;if(!sameSlip(saved.settlements,bets))throw new Error('The result does not match your slip.');accept(saved,false);return}catch(reason){if(!(reason instanceof SicBoGatewayError&&reason.status===404))throw reason}}
      const round=await gateway.createRound(bets,{idempotencyKey:key,signal:request.signal});if(request.signal.aborted||!live.current)return
      if(pending.roundId&&round.roundId!==pending.roundId)throw new Error('The result has an unexpected round ID.');
      if(!sameSlip(round.settlements,bets))throw new Error('The result does not match your slip.')
      accept(round,true)
    } catch(reason){if(request.signal.aborted||!live.current)return;
      const rejected=!state.pending&&reason instanceof SicBoGatewayError&&((reason.status===400&&reason.code==='sic-bo-invalid-request')||(reason.status===409&&reason.code==='insufficient-slot-credits'))
      if(rejected){clearPending(scope);publish({pending:null,phase:'loading',status:null,balance:null,error:null});await loadStatus();if(request.signal.aborted||!live.current)return;if(current.current.status)publish({error:reason.code==='insufficient-slot-credits'?'Insufficient balance. Edit your slip.':'Bet rejected. Edit your slip.'});return}
      publish({phase:recoveryMode==='account'?'retry':'read-failed',error:pending.roundId&&recoveryMode==='practice'?'Roll not confirmed. Retry restoration.':recoveryMode==='account'?'Roll not confirmed. Retry the same slip.':message(reason)})}
    finally {requests.current.delete(request)}
  }
  function prepare(repeat: boolean) {const state=current.current;if(state.phase!=='settled'||!state.round||!state.status||state.balance===null)return;const slip=repeat?state.slip:[];if(repeat&&!legalSlip(slip,state.status,state.balance))return;publish({round:null,slip,phase:'ready',revealed:0,error:null})}
  return {...model,unlocked,stakeValue:stake,canAdd,canRoll,totalStake:totalStake(model.slip),canRepeat:model.phase==='settled'&&!!model.status&&model.balance!==null&&legalSlip(model.slip,model.status,model.balance),add,remove,clear,roll,prepare,restore,loadStatus,setStake:(stake:string)=>{if(current.current.phase==='ready'&&!current.current.pending)publish({stake,error:null})}}
}
function sameSlip(a: readonly SicBoBetRequest[],b: readonly SicBoBetRequest[]) {return a.length===b.length&&a.every((bet,i)=>{const other=b[i];return other&&bet.kind===other.kind&&bet.stake===other.stake&&bet.face===other.face&&bet.total===other.total&&bet.firstFace===other.firstFace&&bet.secondFace===other.secondFace})}
function message(reason: unknown) {return reason instanceof Error?reason.message:'Connection failed.'}
async function deriveRoundId(owner: string,key: string,mode: SicBoRecoveryMode) {const value=mode==='practice'?'practice\nsic-bo\n'+owner+'\n'+key:owner+'\n'+key;const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value));return Array.from(new Uint8Array(digest),byte=>byte.toString(16).padStart(2,'0')).join('')}
function readPending(scope: string) {try{return parsePendingRoll(JSON.parse(sessionStorage.getItem(pendingStorageKey(scope))??'null'),scope)}catch{return null}}
function writePending(scope:string,pending:SicBoPendingRoll) {try{sessionStorage.setItem(pendingStorageKey(scope),JSON.stringify(pending))}catch{/* Optional session persistence; in-memory request remains locked. */}}
function clearPending(scope:string){try{sessionStorage.removeItem(pendingStorageKey(scope))}catch{/* Optional storage. */}}
function readHistory(scope:string){try{return parseRollHistory(JSON.parse(localStorage.getItem(historyStorageKey(scope))??'null'),scope)}catch{return []}}
function writeHistory(scope:string,rounds:readonly SicBoHistoryEntry[]){try{localStorage.setItem(historyStorageKey(scope),JSON.stringify({scope,rounds}))}catch{/* Optional history. */}}
