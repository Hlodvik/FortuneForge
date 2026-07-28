import { PaymentRequestError, type PaymentMarketOption } from './services/paymentsApi'

export const creditsFormatter = new Intl.NumberFormat('en-US')

export function formatMoney(market: PaymentMarketOption, amountMinor: number) {
  return new Intl.NumberFormat(market.locale, {
    style: 'currency',
    currency: market.currency,
    currencyDisplay: 'symbol',
  }).format(amountMinor / 100)
}

export function errorMessage(error: unknown) {
  if (error instanceof PaymentRequestError || error instanceof Error) {
    return error.message
  }

  return 'The mock payment service is unavailable.'
}
