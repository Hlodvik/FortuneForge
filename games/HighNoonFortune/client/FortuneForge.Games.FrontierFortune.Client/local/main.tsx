import { createRoot } from 'react-dom/client'
import { FrontierFortuneGame } from '../src/index.js'
import { InGameShell } from '../../../../../fortuneforge.client/src/components/InGameShell.js'
import type { AccountSummary } from '../../../../../fortuneforge.client/src/features/account/services/accountsApi.js'
import '@fontsource-variable/sora'
import './shell.css'

// Infrastructure-only mount: the ordinary client calls the production paid API.
const localAccount = new URLSearchParams(window.location.search).get('localAccount') === 'qa' ? 'qa' : 'review'
const authenticatedFetch = (input: RequestInfo | URL, init: RequestInit = {}) => {
  const headers = new Headers(input instanceof Request ? input.headers : undefined)
  new Headers(init.headers).forEach((value, name) => headers.set(name, value))
  headers.set('X-Frontier-Local-Account', localAccount)
  return fetch(input, { ...init, headers, credentials: 'include' })
}
const account: AccountSummary = {
  userId: localAccount === 'qa' ? 'frontier-outlaw-paid-validation-v2' : 'frontier-outlaw-paid-review-v2', playerName: 'Local test funds', email: 'frontier-local@example.test',
  createdAtUtc: '2026-11-22T00:00:00Z', role: 'player', balances: { slotsCredits: 1000, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
createRoot(document.getElementById('root')!).render(<FrontierFortuneGame
  account={account}
  authenticatedFetch={authenticatedFetch}
  renderShell={({ balance, onNavigate, children }) => <InGameShell account={account} authenticated balance={balance} onNavigate={onNavigate}
    reportTitle="Frontier Fortune" bodyClassName="in-game-shell__body--slots">{children}</InGameShell>}
/>)
