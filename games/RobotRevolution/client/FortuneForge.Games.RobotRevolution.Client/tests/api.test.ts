import { beforeEach, expect, it, vi } from 'vitest'
import { createSunstoneApi, isSunstoneResult, isSunstoneState } from '../src/api'
import { getSunwheelPath, SUNWHEEL_GUARDIANS } from '../src/sunwheel'
import { FIFTH_SUN_ECLIPSE_MATH as math } from '../src/eclipseMath.generated'

beforeEach(() => sessionStorage.clear())
export const state = () => ({ freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null, paidMode: null, slotsCreditsBalance: 100 })
export function result(active = false) {
  const alignments = Array.from({ length: active ? 3 : 1 }, (_, step) => ({ dialStep: step,
    rings: SUNWHEEL_GUARDIANS.map((guardian, index) => ({ id: guardian.id, symbolId: guardian.symbol, rotationSteps: (step + index) % 4,
      path: getSunwheelPath(guardian.id, 4, step), matchedPositions: [], matches: 0, aligned: false, baseMultiplier: 0, awardPoints: 0 })),
    awakenedGuardianIds: [], alignmentMultiplier: 1, totalAwardPoints: 0 }))
  return { ...state(), spinId: 'sunstone-paid-test-round', gameId: 'robot-revolution-v1', wagerPoints: 12, isFreeSpin: false, pointValueInCents: 25,
    slotsCreditsBalance: 97, reels: ['GLYPH_FLAME','GLYPH_RAIN','GLYPH_WIND','GLYPH_EARTH','GLYPH_REED'].map(symbol => Array(4).fill(symbol)),
    payout: { totalPoints: 0, paylines: [] }, spinFeature: {
      contractVersion: 3, kind: 'fifth-sun', mathVersion: math.mathVersion, dialStep: 0, rows: 4, isEclipseSpin: active,
      heart: { reel: 2, row: 2, symbolId: 'GLYPH_WIND', lit: false }, rings: alignments.at(-1)!.rings, alignedRings: 0, alignmentMultiplier: 1,
      eclipseTriggered: false, eclipseAwardPoints: 0, totalAwardPoints: 0, totalityAwardPoints: 0,
      maxAwardMultiplier: active ? 54 * math.wheelAwardScale + 2 * math.totalityAwardMultiplier : 20,
      eclipseMode: active ? 'solar' : null, wheelAwardScale: active ? math.wheelAwardScale : 1, alignments,
    } }
}
const reply = (value: unknown, status = 200) => Response.json(value, { status, headers: { 'X-FortuneForge-Prepared-Spin': '1' } })

it('accepts Sunstone feature boards and rejects foreign identities, invalid geometry, totals and features', () => {
  expect(isSunstoneResult(result())).toBe(true)
  expect(isSunstoneResult(result(true))).toBe(true)
  for (const change of [{gameId:'foreign'}, {pointValueInCents:1}, {reels:[['unknown']]}, {spinFeature:null}, {payout:{totalPoints:5,paylines:[]}}])
    expect(isSunstoneResult({...result(),...change})).toBe(false)
  expect(isSunstoneResult({...result(),spinFeature:{...result().spinFeature,totalAwardPoints:100}})).toBe(false)
})
it('keeps paid Eclipse rounds distinct from saved free rounds and validates the locked wager', () => {
  expect(isSunstoneState({...state(),paidMode:{mode:'solar-eclipse',remaining:5,wagerPoints:12}})).toBe(true)
  for (const paidMode of [{mode:'other',remaining:5,wagerPoints:12},{mode:'solar-eclipse',remaining:6,wagerPoints:12},{mode:'lunar-eclipse',remaining:2,wagerPoints:13}])
    expect(isSunstoneState({...state(),paidMode})).toBe(false)
  expect(isSunstoneState({...state(),freeSpinsRemaining:1,freeSpinWagerPoints:12,paidMode:{mode:'solar-eclipse',remaining:5,wagerPoints:12}})).toBe(false)
})
it('retains historical bonus modes and permits a frozen classic paid recovery without inventing a Sunwheel feature', () => {
  expect(isSunstoneState({...state(),freeSpinsRemaining:2,freeSpinWagerPoints:101,freeSpinFeatureMode:'sync-rows-paw-rand'})).toBe(true)
  expect(isSunstoneResult({...result(),symbolSetId:'classic-symbols-v3',spinFeature:null})).toBe(true)
})
it('recovers a lost reply with the exact same paid operation and account-bound headers', async () => {
  let lost = true
  const calls: RequestInit[] = []
  const fetch = vi.fn(async (_path:string, init?:RequestInit) => {
    if(init?.method==='GET')return reply(state())
    calls.push(init!); if(lost)throw Error('lost reply'); return reply(result())
  })
  const first=createSunstoneApi(fetch,'one');await first.state();await expect(first.spin(12,false)).rejects.toThrow('lost reply')
  await expect(first.spin(20,false)).rejects.toThrow('Recover')
  const restored=createSunstoneApi(fetch,'one');await restored.state();lost=false;await restored.resume()
  expect(calls).toHaveLength(2);expect(calls[1]).toEqual(calls[0]);expect(restored.pending()).toBeUndefined()
  expect(new Headers(calls[0].headers).get('X-FortuneForge-Expected-Account')).toBe('one')
  expect(JSON.parse(String(calls[0].body))).toEqual({gameId:'robot-revolution-v1',wagerPoints:12,useFreeSpin:false,useSpecialBoost:false})
})
it('preserves an uncertain malformed response and isolates accounts', async () => {
  const fetch=vi.fn(async (_path:string,init?:RequestInit)=>reply(init?.method==='GET'?state():{...result(),wagerPoints:20}))
  const api=createSunstoneApi(fetch,'one');await api.state();await expect(api.spin(12,false)).rejects.toThrow('mismatched')
  expect(api.pending()?.wagerPoints).toBe(12);expect(createSunstoneApi(fetch,'two').pending()).toBeUndefined()
})
it('adopts a pending server operation and rejects unsupported paid transport and out-of-range wagers', async () => {
  const operation={operationId:'server-sunstone-pending-0001',wagerPoints:101,useFreeSpin:true,useSpecialBoost:false}
  const saved=createSunstoneApi(async()=>reply({...state(),pendingOperation:operation}),'saved');await saved.state();expect(saved.pending()).toEqual(operation)
  const unavailable=createSunstoneApi(async()=>Response.json(state()),'no-protocol');await unavailable.state()
  await expect(unavailable.spin(12,false)).rejects.toThrow('unavailable')
  const api=createSunstoneApi(async()=>reply(state()),'limits');await api.state()
  for(const amount of [2,11,13,302])await expect(api.spin(amount,false)).rejects.toThrow('wager')
})
