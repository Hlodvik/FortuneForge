import { describe, expect, it, vi } from 'vitest'
vi.mock('../../features/account/services/accountsApi', () => ({ fetchWithAccountSession: vi.fn(), getCurrentAccount: vi.fn() }))
import { validateJadeOutcome, validateJadeSpinRequest } from './jadeDragonApi'
const outcome = () => ({ gameId:'empty-slots-v1', spinId:'round-1', slotsCreditsBalance:99.5, pointValueInCents:25,
  payout:{totalPoints:0,paylines:[]},reels:Array.from({length:5},()=>['S01','S07','S08','S23']),
  freeSpinsRemaining:0,freeSpinWagerPoints:null,freeSpinFeatureMode:null,specialPointsBalance:0,sealCollections:[] })
describe('Jade Dragon host contract', () => {
 it('fixes the server game ID and caps new paid wagers at R100', () => {
  expect(validateJadeSpinRequest({gameId:'another-game',wagerPoints:400,useFreeSpin:false,useSpecialBoost:false}).gameId).toBe('empty-slots-v1')
  expect(()=>validateJadeSpinRequest({wagerPoints:402,useFreeSpin:false,useSpecialBoost:false})).toThrow()
  expect(()=>validateJadeSpinRequest({wagerPoints:3,useFreeSpin:false,useSpecialBoost:false})).toThrow()
 })
 it('allows an existing higher-wager entitlement but never a client boost on a free spin', () => {
  expect(validateJadeSpinRequest({wagerPoints:2000,useFreeSpin:true,useSpecialBoost:false}).wagerPoints).toBe(2000)
  expect(()=>validateJadeSpinRequest({wagerPoints:20,useFreeSpin:true,useSpecialBoost:true})).toThrow()
 })
 it('accepts all opaque IDs in four- and six-row server grids', () => {
  expect(validateJadeOutcome(outcome()).reels[0]).toHaveLength(4)
  expect(validateJadeOutcome({...outcome(),reels:Array.from({length:5},()=>['S01','S07','S08','S20','S21','S23'])}).reels[0]).toHaveLength(6)
 })
 it('rejects foreign symbols, dimensions, currency, missing wallet, and invalid win positions', () => {
  for(const patch of [{reels:[['dragon']]},{pointValueInCents:100},{slotsCreditsBalance:null},{slotsCreditsBalance:-1},
    {payout:{totalPoints:1,paylines:[{amountPoints:1,matches:[{match:{positions:[{reel:5,row:0}]}}]}]}}])
    expect(()=>validateJadeOutcome({...outcome(),...patch})).toThrow()
 })
})
