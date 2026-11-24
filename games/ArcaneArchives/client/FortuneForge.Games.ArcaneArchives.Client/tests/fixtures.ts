import type { ArcaneSpinResult, ArcaneState } from '../src/types.js'
import type { SlotPotionState, SlotTomeState } from '../src/arcaneFeatures.js'

export const tome = (overrides: Partial<SlotTomeState> = {}): SlotTomeState => ({
  selectedPage: 'embers', activePage: null, boostedSpinsRemaining: 0, grimoireReady: false,
  ingredients: { MANDRAKE_ROOT: 2, PHOENIX_FEATHER: 1 },
  pages: [{ id: 'embers', label: 'Embers', ingredients: ['MANDRAKE_ROOT', 'PHOENIX_FEATHER'] }],
  ...overrides,
})
export const potions = (overrides: Partial<SlotPotionState> = {}): SlotPotionState => ({
  capacity: 7, majority: 4, pot: ['MANDRAKE_ROOT'], bottles: [{
    id: 7, ingredientId: 'MANDRAKE_ROOT', name: 'Mandrake Tonic', effect: 'Scribe Mark payline value', potency: 4, lockedWagerPoints: 20,
  }], active: null, preview: null, ...overrides,
})
export const state = (overrides: Partial<ArcaneState> = {}): ArcaneState => ({
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
  specialPointsBalance: 0, energyBalance: 0, arcaneTome: tome(), arcanePotions: potions(), slotsCreditsBalance: 100,
  ...overrides,
})
export const spin = (overrides: Partial<ArcaneSpinResult> = {}): ArcaneSpinResult => ({
  ...state(), spinId: 'settled-arcane-round', gameId: 'arcane-archives-v1',
  reelSetId: 'arcane-archives-payline-reels-v8', symbolSetId: 'arcane-archives-payline-symbols-v2', paytableId: 'arcane-archives-paytable-v8',
  pointValueInCents: 25, wagerPoints: 12, chargedWagerCents: 300, reelStops: [1, 2, 3, 4, 5],
  reels: [['2', '3', '4', '6'], ['3', '4', '5', '6'], ['4', '5', '6', '2'], ['5', '6', '2', '3'], ['BOLT', '3', '6', '2']],
  isFreeSpin: false, freeSpinsAwarded: 0, specialPointsAwarded: 0, slotsCreditsBalance: 97,
  payout: { totalPoints: 0, paylines: [] }, arcaneIngredientsAwarded: { MANDRAKE_ROOT: 1 },
  ...overrides,
})
export function reply(value: unknown, prepared = true, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: {
    'Content-Type': 'application/json', ...(prepared ? { 'X-FortuneForge-Prepared-Spin': '1' } : {}),
  } })
}
