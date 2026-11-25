import type { PiratesState, PiratesResult } from '../src/api'
export function state(): PiratesState { return { freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
  specialPointsBalance: 20, slotsCreditsBalance: 100, sealCollections: ['sync','rows','paw','rand'].map(sealId => ({sealId,count:0,requiredCount:25,averageWagerPoints:0})) } }
export function result(): PiratesResult { return { ...state(), slotsCreditsBalance:97, gameId:'pirates-fortune-v1', spinId:'paid-test-round-1', wagerPoints:12,
  isFreeSpin:false, pointValueInCents:25, freeSpinsAwarded:0, collectionFreeSpinsAwarded:0, moneyGrabPoints:0, bananaBonusPoints:0,
  sealsAwarded:{}, reels: Array.from({length:5},()=>['2','3','4','6']), payout:{totalPoints:0,paylines:[]} } }
export const reply = (value: unknown, status=200) => Response.json(value, {status,headers:{'X-FortuneForge-Prepared-Spin':'1'}})
