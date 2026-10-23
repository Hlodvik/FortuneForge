import { useCallback,useEffect,useRef,useState } from 'react'
import { MinesGatewayError,type MinesCommand,type MinesGateway,type MinesSession } from './contracts'
import { clearPending,newRequestId,readPending,savePending } from './pendingCommand'

export function useMinesSession(gateway:MinesGateway,playerId:string,onBalanceChange?:(balanceCents:number)=>void){
  const [session,setSession]=useState<MinesSession|null>(null)
  const [busy,setBusy]=useState(true)
  const [error,setError]=useState<string|null>(null)
  const [needsRecovery,setNeedsRecovery]=useState(false)
  const working=useRef(false),alive=useRef(true),pending=useRef<MinesCommand|null>(null)
  const callback=useRef(onBalanceChange);callback.current=onBalanceChange
  const accept=useCallback((next:MinesSession)=>{if(alive.current){setSession(next);callback.current?.(next.balanceCents)}},[])

  const run=useCallback(async(command:MinesCommand)=>{
    if(working.current)return
    working.current=true;setBusy(true);setError(null)
    try{
      // Save identity before sending. A lost response is retried with the identical action and payload.
      savePending(playerId,command);pending.current=command
      await gateway.execute(command)
      // A replayed receipt may be older than another tab's action. Always restore the current ledger view.
      accept(await gateway.getSession())
      clearPending(playerId);pending.current=null;setNeedsRecovery(false)
    }catch(cause){
      const definite=cause instanceof MinesGatewayError&&cause.status>=400&&cause.status<500&&cause.status!==408&&cause.status!==429
      if(definite){
        try{accept(await gateway.getSession());clearPending(playerId);pending.current=null;setNeedsRecovery(false)}
        catch{setNeedsRecovery(true)}
      }else setNeedsRecovery(true)
      if(alive.current)setError(cause instanceof Error?cause.message:'The table could not complete your request. Restore your round.')
    }finally{working.current=false;if(alive.current)setBusy(false)}
  },[gateway,playerId,accept])

  const restore=useCallback(async()=>{
    if(working.current)return
    let saved:MinesCommand|null=null
    try{saved=pending.current??readPending(playerId)}catch{/* A fresh authoritative read recovers from damaged optional browser storage. */}
    if(saved){await run(saved);return}
    working.current=true;setBusy(true);setError(null)
    try{accept(await gateway.getSession());clearPending(playerId);setNeedsRecovery(false)}
    catch(cause){setError(cause instanceof Error?cause.message:'Could not open the table. Please retry.');setNeedsRecovery(true)}
    finally{working.current=false;if(alive.current)setBusy(false)}
  },[gateway,playerId,run,accept])

  useEffect(()=>{
    alive.current=true;void restore()
    const sync=()=>{if(!working.current)void restore()}
    const visible=()=>{if(document.visibilityState==='visible')sync()}
    window.addEventListener('focus',sync);document.addEventListener('visibilitychange',visible)
    return()=>{alive.current=false;window.removeEventListener('focus',sync);document.removeEventListener('visibilitychange',visible)}
  },[restore])
  const start=(wagerCents:number,mineCount:number)=>run({action:'start',requestId:newRequestId(),wagerCents,mineCount})
  const reveal=(tile:number)=>session?.round&&run({action:'reveal',requestId:newRequestId(),roundId:session.round.roundId,tile})
  const cashout=()=>session?.round&&run({action:'cashout',requestId:newRequestId(),roundId:session.round.roundId})
  return{session,busy,error,needsRecovery,restore,start,reveal,cashout}
}
