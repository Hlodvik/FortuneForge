import { MinesGatewayError,type MinesCommand,type MinesGateway,type MinesSession } from './contracts'

export class HttpMinesGateway implements MinesGateway {
  constructor(readonly basePath='/api/games/mines',private readonly requestFn:typeof fetch=fetch){}
  getSession(signal?:AbortSignal){return this.request('/session',{signal})}
  execute(command:MinesCommand){
    const path=command.action==='start'?'/rounds':`/rounds/${encodeURIComponent(command.roundId)}/${command.action}`
    const payload=command.action==='start'?{requestId:command.requestId,wagerCents:command.wagerCents,mineCount:command.mineCount}:command.action==='reveal'?{requestId:command.requestId,tile:command.tile}:{requestId:command.requestId}
    return this.request(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)})
  }
  private async request(path:string,init:RequestInit):Promise<MinesSession>{
    const request=this.requestFn
    let response:Response
    try{response=await request(`${this.basePath.replace(/\/$/,'')}${path}`,{credentials:'same-origin',...init})}
    catch{throw new MinesGatewayError('Connection interrupted. Restore your round to continue safely.')}
    const value:unknown=await response.json().catch(()=>undefined)
    if(!response.ok){const err=record(value)?value:{};throw new MinesGatewayError(typeof err.message==='string'?err.message:'The table could not complete your request. Please retry.',typeof err.code==='string'?err.code:undefined,response.status)}
    if(!isMinesSession(value))throw new MinesGatewayError('The table returned an incomplete result. Restore your round to continue safely.','mines-invalid-response')
    return value
  }
}
const record=(v:unknown):v is Record<string,unknown>=>!!v&&typeof v==='object'
const cents=(v:unknown):v is number=>typeof v==='number'&&Number.isSafeInteger(v)&&v>=0
const positive=(v:unknown):v is number=>typeof v==='number'&&Number.isFinite(v)&&v>0
const onlyKeys=(v:Record<string,unknown>,keys:string[])=>Object.keys(v).every(key=>keys.includes(key))
export function isMinesSession(v:unknown):v is MinesSession{
  if(!record(v)||!cents(v.balanceCents)||typeof v.practice!=='boolean'||!cents(v.minimumWagerCents)||!cents(v.maximumWagerCents)||v.minimumWagerCents<=0||v.maximumWagerCents<v.minimumWagerCents||v.houseEdgeBasisPoints!==100)return false
  if(!onlyKeys(v,['balanceCents','practice','round','minimumWagerCents','maximumWagerCents','houseEdgeBasisPoints']))return false
  if(v.round===null)return true
  const r=v.round
  if(!record(r)||typeof r.roundId!=='string'||!r.roundId||!cents(r.wagerCents)||!cents(r.mineCount)||r.mineCount<1||r.mineCount>24||!['active','lost','cashed-out','cleared'].includes(String(r.status))||!cents(r.safeReveals)||r.safeReveals>25-r.mineCount||r.gemsRemaining!==25-r.mineCount-r.safeReveals||!(r.status==='lost'?r.currentMultiplier===0:positive(r.currentMultiplier))||!(r.nextMultiplier===null||positive(r.nextMultiplier))||!cents(r.cashOutCents)||!cents(r.payoutCents)||typeof r.canCashOut!=='boolean'||!Array.isArray(r.tiles)||r.tiles.length!==25)return false
  if(typeof r.status!=='string'||r.wagerCents<v.minimumWagerCents||r.wagerCents>v.maximumWagerCents||!onlyKeys(r,['roundId','wagerCents','mineCount','status','safeReveals','gemsRemaining','currentMultiplier','nextMultiplier','cashOutCents','canCashOut','payoutCents','tiles']))return false
  if(r.status==='active'?(r.payoutCents!==0||!positive(r.nextMultiplier)):(r.nextMultiplier!==null||r.cashOutCents!==0))return false
  if(r.status==='lost'&&r.payoutCents!==0||r.status==='cleared'&&r.gemsRemaining!==0)return false
  let revealedGems=0,mines=0,revealedMines=0
  for(let i=0;i<25;i++){
    const t=r.tiles[i]
    if(!record(t)||t.index!==i||typeof t.isRevealed!=='boolean'||typeof t.kind!=='string'||!['hidden','gem','mine'].includes(t.kind)||!onlyKeys(t,['index','isRevealed','kind']))return false
    if(r.status==='active'&&((t.kind!=='hidden')!==t.isRevealed||t.kind==='mine'))return false
    if(r.status!=='active'&&t.kind==='hidden')return false
    if(t.isRevealed&&t.kind==='gem')revealedGems++
    if(t.isRevealed&&t.kind==='mine')revealedMines++
    if(t.kind==='mine')mines++
  }
  if(r.status==='lost'?revealedMines!==1:revealedMines!==0)return false
  if((r.status==='cashed-out'||r.status==='cleared')&&r.payoutCents<=0)return false
  return revealedGems===r.safeReveals&&(r.status==='active'||mines===r.mineCount)&&r.canCashOut===(r.status==='active'&&r.safeReveals>0)
}
