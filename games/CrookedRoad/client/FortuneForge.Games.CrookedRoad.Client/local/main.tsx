import { createRoot } from 'react-dom/client'
import { useEffect, useState } from 'react'
import { CrookedRoadGame, type CrookedRoadAccount, type CrookedRoadAuthenticatedFetch, type CrookedRoadShellProps } from '../src/index'

// This local adapter supplies authentication/account ports only. All play uses the production game.
const authenticatedFetch: CrookedRoadAuthenticatedFetch = (input, init) => fetch(input, {
  ...init,
  credentials: 'same-origin',
})

function decodeAccount(value: unknown): CrookedRoadAccount | null {
  if (value === null || typeof value !== 'object') return null
  const account = value as Partial<CrookedRoadAccount>
  return typeof account.userId === 'string' && account.userId.length > 0 && typeof account.role === 'string' &&
    typeof account.balances?.slotsCredits === 'number' && Number.isFinite(account.balances.slotsCredits) &&
    account.balances.slotsCredits >= 0 ? account as CrookedRoadAccount : null
}

const formatBalance = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' })
function renderShell({ balance, onNavigate, children }: CrookedRoadShellProps) {
  return <div className="local-shell">
    <nav className="local-navbar" aria-label="Local test navigation">
      <a href="/" onClick={onNavigate}>Fortune Forge · local paid test</a>
      <p className="local-navbar__balance"><span>Slot credits</span><strong data-testid="local-paid-balance">{formatBalance.format(balance)}</strong></p>
    </nav>
    {children}
  </div>
}

function LocalPaidGame() {
  const [account, setAccount] = useState<CrookedRoadAccount | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    void (async () => {
      try {
        const response = await authenticatedFetch('/api/accounts/me', { signal: abort.signal, cache: 'no-store' })
        if (!response.ok) throw new Error(`The local paid fixture account is unavailable (${response.status}).`)
        const loaded = decodeAccount(await response.json())
        if (!loaded) throw new Error('The local paid fixture returned an invalid account.')
        if (!abort.signal.aborted) setAccount(loaded)
      } catch (failure) {
        if (!abort.signal.aborted) setError(failure instanceof Error ? failure.message : 'The local paid fixture could not be reached.')
      }
    })()
    return () => abort.abort()
  }, [])
  if (error) return <main className="local-status" role="alert"><h1>Local paid fixture unavailable</h1><p>{error}</p><p>Start the isolated paid fixture, then reload this page.</p></main>
  if (!account) return <main className="local-status" role="status">Loading the isolated paid test account…</main>
  return <CrookedRoadGame account={account} authenticatedFetch={authenticatedFetch} renderShell={renderShell} />
}

createRoot(document.getElementById('root')!).render(<LocalPaidGame />)
