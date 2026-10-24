/** Table chip fields are integer cents; account/rule credits are decimal rand. */
export const formatHoldemCents = (cents: number) => formatHoldemCredits(cents / 100)
export const formatHoldemCredits = (credits: number) => `R${credits.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
