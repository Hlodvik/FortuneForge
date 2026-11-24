import { createRoot } from 'react-dom/client'
import { PharaohAscentGame } from '../src/index'
import type { PharaohAccount, PharaohAuthenticatedFetch } from '../src/types'
import './review.css'

async function openLocalReview() {
  const health = await fetch('/__fixture/health').then(response => response.json())
  if (health.localOnly !== true || health.productionEngine !== true || health.testFundsOnly !== true ||
    health.gameId !== 'pharaohs-ascent-v1') throw new Error('Local review requires the isolated production-game harness.')
  const account = await fetch('/__fixture/account').then(response => response.json()) as PharaohAccount
  if (account.userId !== health.account) throw new Error('The local test account does not match its isolated wallet.')
  const authenticatedFetch: PharaohAuthenticatedFetch = (input, init) => fetch(input, init)
  createRoot(document.getElementById('root')!).render(<PharaohAscentGame account={account} authenticatedFetch={authenticatedFetch}
    renderShell={({ balance, children }) => <><header className="pharaoh-review-shell"><span>Fortune Forge <small>Local review · test funds</small></span>
      <strong aria-label="Game balance">{new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(balance)}</strong></header>{children}</>} />)
}
openLocalReview().catch(error => { document.getElementById('root')!.textContent = error instanceof Error ? error.message : 'The local game could not be opened.' })
