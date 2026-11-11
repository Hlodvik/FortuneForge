export type GoldCrucibleAward = {
  crucibleWageredCents: number
  valueCents: number
  payoutCents: number
  completed: boolean
}

const cents = (amount: unknown): amount is number =>
  typeof amount === 'number' && Number.isSafeInteger(amount) && amount >= 0

export function decodeCrucibleAward(response: object): GoldCrucibleAward | null {
  const value = response as Record<string, unknown>
  if ((value.crucibleWageredCents !== undefined && !cents(value.crucibleWageredCents)) ||
      (value.cruciblePourValueCents !== undefined && !cents(value.cruciblePourValueCents)) ||
      (value.cruciblePayoutCents !== undefined && !cents(value.cruciblePayoutCents)) ||
      (value.cruciblePoured !== undefined && typeof value.cruciblePoured !== 'boolean')) return null
  return {
    crucibleWageredCents: value.crucibleWageredCents ?? 0,
    valueCents: value.cruciblePourValueCents ?? 0,
    payoutCents: value.cruciblePayoutCents ?? 0,
    completed: value.cruciblePoured === true,
  }
}
