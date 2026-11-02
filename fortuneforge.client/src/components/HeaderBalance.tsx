import '@fontsource-variable/sora'
import './HeaderBalance.css'

const randFormatter = new Intl.NumberFormat('en-US', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

export function HeaderBalance({ amount, href, className = '' }: { amount: number; href?: string; className?: string }) {
  const formatted = `R${randFormatter.format(amount)}`
  const wholeRand = `R${new Intl.NumberFormat('en-US').format(Math.trunc(amount))}`
  const content = <><span>Balance</span><strong className="site-header-balance__full">{formatted}</strong><strong className="site-header-balance__whole" aria-hidden="true">{wholeRand}</strong></>
  const classes = ['site-header-balance', className].filter(Boolean).join(' ')

  return href
    ? <a className={classes} href={href} aria-label={`Balance ${formatted}. Add funds`}>{content}</a>
    : <span className={classes} aria-label={`Balance ${formatted}`}>{content}</span>
}
