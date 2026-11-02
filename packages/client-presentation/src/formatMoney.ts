/** The games' symbol-prefixed, two-decimal display convention in the user's locale. */
export function formatMoney(value: number, symbol: string): string {
  return (value < 0 ? '-' : '') + symbol + Math.abs(value).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}
