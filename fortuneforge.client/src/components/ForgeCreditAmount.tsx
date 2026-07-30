import './ForgeCreditAmount.css'

const amountFormatter = new Intl.NumberFormat('en-US')

type ForgeCoinProps = {
  className?: string
}

export function ForgeCoin({ className }: ForgeCoinProps) {
  return (
    <span
      className={['forge-coin', className].filter(Boolean).join(' ')}
      aria-hidden="true"
    >
      R
    </span>
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
      aria-label={`${formattedAmount} South African rand`}
    >
      <ForgeCoin />
      <strong>{formattedAmount}</strong>
    </span>
  )
}
