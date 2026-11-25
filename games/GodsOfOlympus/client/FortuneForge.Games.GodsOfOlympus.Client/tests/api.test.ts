import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createOlympusApi, isOlympusResult } from '../src/api'
import { state, result, reply, classic, tumbleWin } from './fixtures'
beforeEach(()=>sessionStorage.clear())
describe('Olympus package paid contract',()=>{
 it('accepts its own server board and rejects malformed identities, coordinates and totals',()=>{
  expect(isOlympusResult(result())).toBe(true)
  expect(isOlympusResult({...result(),gameId:'foreign'})).toBe(false)
  expect(isOlympusResult({...result(),pointValueInCents:1})).toBe(false)
  expect(isOlympusResult({...result(),slotsCreditsBalance:-1})).toBe(false)
  expect(isOlympusResult({...result(),reels:[['made-up']]})).toBe(false)
  expect(isOlympusResult({...result(),payout:{totalPoints:1,paylines:[]}})).toBe(false)
 })
 it('persists the exact paid operation after a lost reply and replays only that operation',async()=>{
  let lost=true
  const posts: RequestInit[]=[]
  const fetch=vi.fn(async (_path:string,init?:RequestInit)=>{
    if(init?.method==='GET')return reply(state())
    posts.push(init!);if(lost)throw Error('lost reply');return reply(result())
  })
  const first=createOlympusApi(fetch,'recovery-player');await first.state()
  await expect(first.spin(12,false)).rejects.toThrow('lost reply')
  await expect(first.spin(20,false)).rejects.toThrow('Resume')
  const restored=createOlympusApi(fetch,'recovery-player');await restored.state();lost=false
  await restored.resume()
  expect(posts).toHaveLength(2);expect(posts[1]).toEqual(posts[0]);expect(restored.pending()).toBeUndefined()
  expect(JSON.parse(String(posts[0].body))).toEqual({gameId:'gods-of-olympus-v1',wagerPoints:12,useFreeSpin:false,useSpecialBoost:false})
  expect(new Headers(posts[0].headers).get('X-FortuneForge-Expected-Account')).toBe('recovery-player')
 })
 it('keeps a malformed or mismatched settled reply recoverable and isolates accounts',async()=>{
  const fetch=vi.fn(async (_path:string,init?:RequestInit)=>reply(init?.method==='GET'?state():{...result(),wagerPoints:20}))
  const api=createOlympusApi(fetch,'one');await api.state();await expect(api.spin(12,false)).rejects.toThrow('mismatched')
  expect(api.pending()?.wagerPoints).toBe(12);expect(createOlympusApi(fetch,'two').pending()).toBeUndefined()
 })
 it('adopts a server-admitted operation and refuses new paid play without prepared support',async()=>{
  const operation={operationId:'saved-server-operation-0001',wagerPoints:101,useFreeSpin:true,useSpecialBoost:false}
  const fetch=vi.fn(async()=>reply({...state(),freeSpinsRemaining:2,freeSpinWagerPoints:101,pendingOperation:operation}))
  const api=createOlympusApi(fetch,'adopt');await api.state();expect(api.pending()).toEqual(operation)
  const unavailable=createOlympusApi(async()=>Response.json(state()),'unavailable');await unavailable.state()
  await expect(unavailable.spin(12,false)).rejects.toThrow('unavailable');expect(unavailable.pending()).toBeUndefined()
 })
 it('preserves odd funded wagers and never boosts a free round',async()=>{
  const free={...classic(),isFreeSpin:true,wagerPoints:101}
  const fetch=vi.fn(async (_path:string,init?:RequestInit)=>reply(init?.method==='GET'?{...state(),freeSpinsRemaining:2,freeSpinWagerPoints:101}:free))
  const api=createOlympusApi(fetch,'funded');await api.state();await expect(api.spin(101,true,true)).rejects.toThrow('wager')
  await expect(api.spin(101,true)).resolves.toMatchObject({wagerPoints:101,isFreeSpin:true})
 })
})
it('validates every tumble award, clear, refill and multiplier against the settled presentation',()=>{
 const win=tumbleWin();expect(isOlympusResult(win)).toBe(true)
 for(const mutate of [
  (v:ReturnType<typeof tumbleWin>)=>{v.olympusTumble!.multiplier=3},
  (v:ReturnType<typeof tumbleWin>)=>{v.olympusTumble!.steps[0].cleared[0]={reel:8,row:0}},
  (v:ReturnType<typeof tumbleWin>)=>{v.olympusTumble!.steps[0].dropRows[1][4]=3},
  (v:ReturnType<typeof tumbleWin>)=>{v.olympusTumble!.steps[0].reels[1][4]='J'},
 ]){const value=structuredClone(win);mutate(value);expect(isOlympusResult(value)).toBe(false)}
})
