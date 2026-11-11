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

export function callHost(method, payload = {}) {
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
    parent.postMessage({ channel: 'fortune-jade-v1', id, method, payload }, parentOrigin)
  })
}

addEventListener('message', event => {
  if (event.source !== parent || event.origin !== parentOrigin || event.data?.channel !== 'fortune-jade-v1') return
  const pending = waiting.get(event.data.id)
  if (!pending) return
  clearTimeout(pending.timer)
  waiting.delete(event.data.id)
  if (event.data.error) pending.reject(Error(event.data.error))
  else pending.resolve(event.data.result)
})
