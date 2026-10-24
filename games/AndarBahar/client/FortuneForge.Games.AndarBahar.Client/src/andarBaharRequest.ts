import { AndarBaharGatewayError } from './contracts'

export const andarBaharRequestTimeoutMs = 20_000

/** Bound the whole operation, including body decoding and adapters that ignore abort. */
export async function withAndarBaharRequestDeadline<T>(request: (signal: AbortSignal) => Promise<T>, parent?: AbortSignal): Promise<T> {
  const controller = new AbortController()
  const cancel = () => controller.abort(parent?.reason ?? new DOMException('The table request was cancelled.', 'AbortError'))
  let rejectAbort!: (reason: unknown) => void
  const aborted = new Promise<never>((_resolve, reject) => { rejectAbort = reject })
  const onAbort = () => rejectAbort(controller.signal.reason)
  controller.signal.addEventListener('abort', onAbort, { once: true })
  parent?.addEventListener('abort', cancel, { once: true })
  const timer = globalThis.setTimeout(() => controller.abort(new AndarBaharGatewayError(
    'The table took too long to respond. Restore your deal to confirm its result without paying twice.',
    'andar-bahar-request-timeout',
  )), andarBaharRequestTimeoutMs)
  if (parent?.aborted) cancel()
  try {
    return await Promise.race([
      Promise.resolve().then(() => {
        if (controller.signal.aborted) throw controller.signal.reason
        return request(controller.signal)
      }),
      aborted,
    ])
  } finally {
    globalThis.clearTimeout(timer)
    controller.signal.removeEventListener('abort', onAbort)
    parent?.removeEventListener('abort', cancel)
  }
}
