type Account = { userId: string; balances: { slotsCredits: number } }
type Ports = {
  accountId: string
  source: () => Window | null | undefined
  origin: string
  authenticatedFetch: (path: string, init?: RequestInit) => Promise<Response>
  loadAccount: () => Promise<Account>
}
const channel = 'fortune-forge.game-transport.v1'
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)

/** The frame owns gameplay and protocols; the platform exposes authenticated HTTP and account access. */
export function attachAuthenticatedFrameTransport(ports: Ports) {
  let mounted = true
  const requests = new Map<string, Promise<unknown>>()
  async function execute(method: unknown, payload: unknown) {
    if (method === 'account') {
      const account = await ports.loadAccount()
      if (account.userId !== ports.accountId) throw new Error('The signed-in account changed. Reopen the game.')
      return account
    }
    if (method !== 'request' || !record(payload) || typeof payload.path !== 'string' ||
      !payload.path.startsWith('/api/') || payload.path.includes('\\') ||
      new URL(payload.path, ports.origin).origin !== ports.origin || !record(payload.init)) throw new Error('Invalid game transport request.')
    const init = payload.init
    if (!['GET', 'POST'].includes(String(init.method)) || init.body !== undefined &&
      (typeof init.body !== 'string' || init.body.length > 65536)) throw new Error('Invalid game transport request.')
    const headers = new Headers()
    if (record(init.headers)) for (const [name, value] of Object.entries(init.headers)) {
      if (typeof value !== 'string' || !['content-type', 'idempotency-key', 'x-fortuneforge-expected-account'].includes(name.toLowerCase()))
        throw new Error('Unsupported game transport header.')
      headers.set(name, value)
    }
    headers.set('X-FortuneForge-Expected-Account', ports.accountId)
    const response = await ports.authenticatedFetch(payload.path, { method: String(init.method), headers,
      ...(init.method === 'POST' && typeof init.body === 'string' ? { body: init.body } : {}), cache: 'no-store' })
    return { status: response.status, headers: [...response.headers], body: await response.text() }
  }
  const receive = (event: MessageEvent) => {
    const source = ports.source()
    if (!source || event.source !== source || event.origin !== ports.origin || !record(event.data) ||
      event.data.channel !== channel || typeof event.data.id !== 'string' || event.data.id.length > 100) return
    const { id, method, payload } = event.data
    let request = requests.get(id)
    if (!request) {
      if (requests.size >= 256) requests.delete(requests.keys().next().value!)
      request = execute(method, payload)
      requests.set(id, request)
    }
    void request.then(result => {
      if (mounted) source.postMessage({ channel, id, result }, ports.origin)
    }, issue => {
      if (mounted) source.postMessage({ channel, id, error: issue instanceof Error ? issue.message : 'Game transport failed.' }, ports.origin)
    })
  }
  window.addEventListener('message', receive)
  return () => { mounted = false; window.removeEventListener('message', receive); requests.clear() }
}
