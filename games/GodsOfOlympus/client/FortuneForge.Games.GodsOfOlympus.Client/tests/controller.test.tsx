import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { OlympusGame } from '../src/OlympusGame'
import { state, result, reply, classic, tumbleWin } from './fixtures'
const audio = vi.hoisted(()=>({preferences:{mode:'muted',volume:0},playCue:()=>{},playSequence:()=>{},startLoop:()=>{},stopLoop:()=>{},stopResultCues:()=>{},toggleMuted:()=>{},toggleResultsOnly:()=>{},setVolume:()=>{}}))
vi.mock('@fortuneforge/slot-client-core', async actual => ({...await actual<typeof import('@fortuneforge/slot-client-core')>(),
 usePrefersReducedMotion:()=>true, useSlotAudio:()=>audio}))
let root:Root, host:HTMLDivElement
beforeEach(()=>{ vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT',true); sessionStorage.clear();host=document.createElement('div');document.body.append(host);root=createRoot(host) })
afterEach(()=>{ act(()=>root.unmount());host.remove();vi.unstubAllGlobals() })
const account={userId:'client-controller-test',balances:{slotsCredits:100}}
it('mounts its own cabinet, admits one paid spin, and presents only the validated server result',async()=>{
 let finish!:(response:Response)=>void
 const fetch=vi.fn(async (_path:string,init?:RequestInit)=>init?.method==='GET'?reply(state()):new Promise<Response>(resolve=>{finish=resolve}))
 await act(async()=>root.render(<OlympusGame account={account} authenticatedFetch={fetch} />))
 const spin=host.querySelector<HTMLButtonElement>('button[aria-label="Spin the reels"]')!
 expect(spin).not.toBeNull();expect(spin.disabled).toBe(false)
 await act(async()=>{spin.click();spin.click()})
 expect(fetch.mock.calls.filter(([,init])=>init?.method==='POST')).toHaveLength(1)
 await act(async()=>finish(reply(tumbleWin())))
 expect(host.querySelector('[data-game-busy]')).toBeNull()
 expect(host.textContent).toContain('Balance')
 expect(host.textContent).toContain('97')
 expect(host.querySelectorAll('.slot-reel')).toHaveLength(6)
})
it('uses the exact saved free wager, including odd averages, instead of the paid picker',async()=>{
 const next={...classic(),isFreeSpin:true,wagerPoints:101}
 const fetch=vi.fn(async (_path:string,init?:RequestInit)=>reply(init?.method==='GET'?{...state(),freeSpinsRemaining:2,freeSpinWagerPoints:101}:next))
 await act(async()=>root.render(<OlympusGame account={account} authenticatedFetch={fetch} />))
 const spin=host.querySelector<HTMLButtonElement>('button[aria-label="Spin the reels"]')!
 await act(async()=>spin.click())
 const post=fetch.mock.calls.find(([,init])=>init?.method==='POST')!
 expect(JSON.parse(String(post[1]!.body))).toMatchObject({wagerPoints:101,useFreeSpin:true,useSpecialBoost:false})
})

it('discards a late paid result after the signed-in account changes',async()=>{
 let finish!:(response:Response)=>void
 const first=vi.fn(async (_path:string,init?:RequestInit)=>init?.method==='GET'?reply(state()):new Promise<Response>(resolve=>{finish=resolve}))
 await act(async()=>root.render(<OlympusGame account={account} authenticatedFetch={first} />))
 await act(async()=>host.querySelector<HTMLButtonElement>('button[aria-label="Spin the reels"]')!.click())
 const next=vi.fn(async()=>reply({...state(),slotsCreditsBalance:22}))
 await act(async()=>root.render(<OlympusGame account={{...account,userId:'new-account'}} authenticatedFetch={next} />))
 await act(async()=>finish(reply({...result(),slotsCreditsBalance:999})))
 expect(host.textContent).toMatch(/22[,.]00/);expect(host.textContent).not.toContain('999')
 expect(next).toHaveBeenCalledOnce()
})
