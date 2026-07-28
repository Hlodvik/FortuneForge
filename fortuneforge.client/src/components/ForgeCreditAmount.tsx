import './ForgeCreditAmount.css'

const amountFormatter = new Intl.NumberFormat('en-US')

type ForgeCoinProps = {
  className?: string
}

// A geometric C with one straight vertical stroke keeps the currency mark
// distinct from both a dollar sign and the diagonal-stroked cent symbol.
export function ForgeCoin({ className }: ForgeCoinProps) {
  return (
    <svg
      className={['forge-coin', className].filter(Boolean).join(' ')}
      viewBox="0 0 64 64"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="32" cy="32" r="29" fill="#f4a900" stroke="#fff0a3" strokeWidth="3" />
      <circle cx="32" cy="32" r="23.5" fill="#ffc83d" stroke="#a94d00" strokeWidth="2" />
      <path
        d="M43 21.5c-2.8-3.6-6.5-5.4-11-5.4-8.4 0-14.4 6.9-14.4 15.9S23.6 47.9 32 47.9c4.5 0 8.2-1.8 11-5.4"
        fill="none"
        stroke="#6c2900"
        strokeLinecap="round"
        strokeWidth="6"
      />
      <path d="M32 10v44" fill="none" stroke="#6c2900" strokeLinecap="round" strokeWidth="4" />
      <path d="M20 13.5c4-3.2 9.2-4.7 14.4-4.2" fill="none" stroke="#fff6bd" strokeLinecap="round" strokeWidth="3" opacity="0.9" />
    </svg>
  )
}

type ForgeCreditAmountProps = {
  amount: number
  className?: string
  inline?: boolean
}

export function ForgeCreditAmount({ amount, className, inline = false }: ForgeCreditAmountProps) {
  const formattedAmount = amountFormatter.format(amount)
  return (
    <span
      className={[
        'forge-credit-amount',
        inline ? 'forge-credit-amount--inline' : null,
        className,
      ].filter(Boolean).join(' ')}
      aria-label={`${formattedAmount} game credits`}
    >
      <ForgeCoin />
      <strong>{formattedAmount}</strong>
    </span>
  )
}
