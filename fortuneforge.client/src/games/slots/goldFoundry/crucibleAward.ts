import type { SlotEnergyAward } from '../../../features/slots/config/slotFeatures'

const isOptionalCentAmount = (amount: unknown): amount is number | null | undefined =>
  amount == null || (typeof amount === 'number' && Number.isSafeInteger(amount) && amount >= 0)

export function decodeCrucibleAward(response: object): SlotEnergyAward | null {
  const {
    crucibleWageredCents,
    cruciblePourValueCents,
    cruciblePayoutCents,
    cruciblePoured,
  } = response as Record<string, unknown>

  if (!isOptionalCentAmount(crucibleWageredCents) ||
      !isOptionalCentAmount(cruciblePourValueCents) ||
      !isOptionalCentAmount(cruciblePayoutCents) ||
      (cruciblePoured != null && typeof cruciblePoured !== 'boolean')) {
    return null
  }

  return {
    demoState: { crucibleWageredCents: crucibleWageredCents ?? 0 },
    valueCents: cruciblePourValueCents ?? 0,
    payoutCents: cruciblePayoutCents ?? 0,
    completed: cruciblePoured === true,
  }
}
