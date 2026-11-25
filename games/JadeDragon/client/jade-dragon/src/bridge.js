import { createJadeGameApi } from './api.ts'

const waiting = new Map()
const session = crypto.randomUUID()
const declaredOrigin = new URLSearchParams(location.search).get('ffHostOrigin')
const parentOrigin = resolveParentOrigin()
let sequence = 0

function resolveParentOrigin() {
  if (!declaredOrigin) return location.origin
  try {
    return new URL(document.referrer).origin === declaredOrigin ? declaredOrigin : null
  } catch {
    return null
  }
}

function transport(method, payload = {}) {
  if (parent === window || !parentOrigin) {
    return Promise.reject(Error('Open this game through Fortune Forge to connect your account.'))
  }
  const id = 'jade-' + session + '-' + ++sequence
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      waiting.delete(id)
      reject(Error('The game service did not respond. Reload to recover your settled balance before continuing.'))
    }, 30000)
    waiting.set(id, { resolve, reject, timer })
    parent.postMessage({ channel: 'fortune-forge.game-transport.v1', id, method, payload }, parentOrigin)
  })
}

addEventListener('message', event => {
  if (event.source !== parent || event.origin !== parentOrigin || event.data?.channel !== 'fortune-forge.game-transport.v1') return
  const pending = waiting.get(event.data.id)
  if (!pending) return
  clearTimeout(pending.timer)
  waiting.delete(event.data.id)
  if (event.data.error) pending.reject(Error(event.data.error))
  else pending.resolve(event.data.result)
})

let api
let busy = false
async function gameApi() {
  if (!api) {
    const account = await transport('account')
    api = createJadeGameApi(account.userId, {
      loadAccount: () => transport('account'),
      authenticatedFetch: async (path, init = {}) => {
        const result = await transport('request', { path, init: { method: init.method ?? 'GET',
          headers: Object.fromEntries(new Headers(init.headers)), ...(init.body ? { body: init.body } : {}) } })
        return new Response(result.body, { status: result.status, headers: result.headers })
      },
    })
  }
  return api
}

/** The game interprets its own commands; the parent only carries authenticated HTTP. */
export async function callHost(method, payload = {}) {
  if (busy) throw Error('A Jade Dragon operation is already in progress.')
  busy = true
  try {
    const current = await gameApi()
    if (method === 'init') return await current.loadSession()
    if (method === 'spin') return await current.spin(payload)
    throw Error('Unknown Jade Dragon command.')
  } finally { busy = false }
}
