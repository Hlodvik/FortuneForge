// Edited: 2026-11-18T22:49:50-06:00 | Computer: Vinnabok
import { SITE_ASSET_DIRECTORY } from './site-release-contract.mjs'

const baseUrl = new URL(process.env.FORTUNE_FORGE_URL ?? process.argv[2] ?? 'https://fortuneforgegame.web.app')
if (baseUrl.protocol !== 'https:' && baseUrl.hostname !== 'localhost') throw new Error('Site smoke requires HTTPS unless the target is localhost.')

for (const route of ['/', '/home', '/login', '/create-account', '/balance', '/admin']) {
  const response = await fetch(new URL(route, baseUrl), { signal: AbortSignal.timeout(20_000), cache: 'no-store' })
  const html = await response.text()
  if (!response.ok || !html.includes('<div id="root"></div>') || !html.includes(`/${SITE_ASSET_DIRECTORY}/`)) throw new Error(`${route} did not serve the isolated site shell (${response.status}).`)
}
const account = await fetch(new URL('/api/accounts/me', baseUrl), { signal: AbortSignal.timeout(20_000), cache: 'no-store' })
if (account.status !== 401) throw new Error(`Signed-out account request returned ${account.status}; expected 401.`)
console.log('Site smoke passed: public/account/auth/admin shell routes and signed-out account boundary.')
