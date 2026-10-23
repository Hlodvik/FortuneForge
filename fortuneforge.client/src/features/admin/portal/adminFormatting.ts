const number = new Intl.NumberFormat('en-ZA', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
export const adminMoney = (value: number | null | undefined) => value == null ? 'Not recorded' : `R${number.format(value)}`
export function purchaseAmount(amountMinor: number | null, currency: string) {
  if (amountMinor === null) return 'Amount not recorded'
  try {
    const format = new Intl.NumberFormat('en-ZA', { style: 'currency', currency })
    const decimals = format.resolvedOptions().maximumFractionDigits ?? 2
    return format.format(amountMinor / (10 ** decimals))
  } catch { return `${amountMinor} minor units (${currency || 'currency unknown'})` }
}
