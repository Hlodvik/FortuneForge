import type { MinesCommand } from './contracts'
export const pendingKey=(playerId:string)=>`fortuneforge:mines:pending:${playerId}`
export function readPending(playerId:string):MinesCommand|null{
  const raw=localStorage.getItem(pendingKey(playerId))
  if(!raw)return null
  let v:unknown
  try{v=JSON.parse(raw)}catch{throw new Error('Your last request could not be read. Restore your round before playing again.')}
  if(!v||typeof v!=='object')throw new Error('Your last request could not be read. Restore your round before playing again.')
  const c=v as Record<string,unknown>
  if(typeof c.requestId!=='string'||!/^[a-zA-Z0-9_-]{16,128}$/.test(c.requestId))throw new Error('Your last request could not be read. Restore your round before playing again.')
  if(c.action==='start'&&typeof c.wagerCents==='number'&&Number.isSafeInteger(c.wagerCents)&&c.wagerCents>=10&&c.wagerCents<=10000&&typeof c.mineCount==='number'&&Number.isInteger(c.mineCount)&&c.mineCount>=1&&c.mineCount<=24)return c as MinesCommand
  if((c.action==='cashout'||c.action==='reveal'&&typeof c.tile==='number'&&Number.isInteger(c.tile)&&c.tile>=0&&c.tile<25)&&typeof c.roundId==='string'&&!!c.roundId)return c as MinesCommand
  throw new Error('Your last request could not be read. Restore your round before playing again.')
}
export function savePending(playerId:string,command:MinesCommand){localStorage.setItem(pendingKey(playerId),JSON.stringify(command))}
export function clearPending(playerId:string){localStorage.removeItem(pendingKey(playerId))}
export const newRequestId=()=>`mines-${crypto.randomUUID()}`
