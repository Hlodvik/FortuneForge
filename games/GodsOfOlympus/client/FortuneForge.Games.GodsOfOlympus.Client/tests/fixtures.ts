import type { OlympusState, OlympusResult } from '../src/api'
export function state(): OlympusState { return { freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
  specialPointsBalance: 20, slotsCreditsBalance: 100, sealCollections: ['sync','rows','paw','rand'].map(sealId => ({sealId,count:0,requiredCount:28,averageWagerPoints:0})) } }
export function classic(): OlympusResult { return { ...state(), slotsCreditsBalance:97, gameId:'gods-of-olympus-v1', spinId:'paid-test-round-1', wagerPoints:12,
  isFreeSpin:true, pointValueInCents:25, freeSpinsAwarded:0, collectionFreeSpinsAwarded:0, moneyGrabPoints:0, bananaBonusPoints:0,
  sealsAwarded:{}, reels: Array.from({length:5},()=>['2','3','4','6']), payout:{totalPoints:0,paylines:[]} } }
export const reply = (value: unknown, status=200) => Response.json(value, {status,headers:{'X-FortuneForge-Prepared-Spin':'1'}})
export function result(): OlympusResult {
  const reels = Array.from({length:6},(_,column)=>Array.from({length:5},(_,row)=>['2','3','4','5','6','7','8','A','J'][(column*5+row)%9]))
  return {...classic(), isFreeSpin:false, reels, olympusTumble:{initialReels:reels,steps:[],totalPoints:0,baseTotalPoints:0,multiplier:1}}
}
export function tumbleWin(): OlympusResult {
  const reels=[['2','2','2','2','2'],['2','2','2','3','4'],['3','4','5','6','7'],['4','5','6','7','8'],['5','6','7','8','A'],['6','7','8','A','X2']]
  const cleared=[...Array.from({length:5},(_,row)=>({reel:0,row})),...Array.from({length:3},(_,row)=>({reel:1,row}))]
  const next=[['3','4','5','6','7'],['8','A','J','3','4'],...reels.slice(2)]
  return {...result(), reels, payout:{totalPoints:12,paylines:[]}, olympusTumble:{initialReels:reels,totalPoints:12,baseTotalPoints:6,multiplier:2,
    steps:[{reels:next,cleared,dropRows:[[5,5,5,5,5],[3,3,3,0,0],...[0,0,0,0].map(()=>[0,0,0,0,0])],amountPoints:6,
      awards:[{symbol:'2',amountPoints:6,positions:cleared}]}]}}
}
