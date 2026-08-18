import { useCallback, useEffect, useRef, useState } from 'react'
import {
  isUncertainPracticeFailure,
  PracticeBotRequestError,
  stablePracticeMutation,
  type PendingPracticeMutation,
} from './practiceBots'

export type PracticeSessionState<T> =
  | Readonly<{ kind: 'loading' }>
  | Readonly<{ kind: 'idle' }>
  | Readonly<{ kind: 'ready'; response: T }>
  | Readonly<{ kind: 'disabled'; message: string }>
  | Readonly<{ kind: 'error'; message: string }>

export function usePracticeBotSession<T>({
  getSession,
}: {
  getSession: (signal?: AbortSignal) => Promise<T | null>
}) {
  const [state, setState] = useState<PracticeSessionState<T>>({ kind: 'loading' })
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const pending = useRef<PendingPracticeMutation | null>(null)

  const refresh = useCallback(async (quiet = false, signal?: AbortSignal) => {
    if (!quiet) setState({ kind: 'loading' })
    try {
      const response = await getSession(signal)
      setState(response === null ? { kind: 'idle' } : { kind: 'ready', response })
      pending.current = null
      setMessage(null)
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (error instanceof PracticeBotRequestError && error.code === 'card-bots-disabled') {
        setState({ kind: 'disabled', message: error.message })
        pending.current = null
        setMessage(null)
        return
      }
      if (!quiet) setState({ kind: 'error', message: messageFor(error) })
    }
  }, [getSession])

  useEffect(() => {
    const controller = new AbortController()
    void refresh(false, controller.signal)
    return () => controller.abort()
  }, [refresh])

  useEffect(() => {
    if (state.kind !== 'ready') return
    const poll = window.setInterval(() => {
      if (document.visibilityState === 'visible') void refresh(true)
    }, 1_000)
    return () => window.clearInterval(poll)
  }, [refresh, state.kind])

  const mutate = useCallback(async (
    fingerprint: string,
    request: (idempotencyKey: string) => Promise<T>,
  ) => {
    if (pending.current !== null && pending.current.fingerprint !== fingerprint) {
      setMessage('Retry the same pending request or refresh the table before acting again.')
      return
    }
    const active = stablePracticeMutation(pending.current, fingerprint)
    pending.current = active
    setBusy(true)
    setMessage(null)
    try {
      const response = await request(active.idempotencyKey)
      pending.current = null
      setState({ kind: 'ready', response })
    } catch (error) {
      if (error instanceof PracticeBotRequestError && error.code === 'card-bot-state-conflict') {
        pending.current = null
        setMessage('The table advanced before that action arrived. Restoring the latest view…')
        await refresh(true)
      } else {
        if (!isUncertainPracticeFailure(error)) pending.current = null
        setMessage(messageFor(error))
      }
    } finally {
      setBusy(false)
    }
  }, [refresh])

  return {
    state,
    busy,
    message,
    pending: pending.current,
    refresh: () => void refresh(false),
    mutate,
  }
}

function messageFor(error: unknown): string {
  if (error instanceof Error) return error.message
  return 'The account-neutral practice table could not complete the request.'
}
