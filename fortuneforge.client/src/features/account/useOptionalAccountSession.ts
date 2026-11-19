// Edited: 2026-11-18T22:44:06-06:00 | Computer: Vinnabok
import { useCallback, useEffect, useState } from 'react'
import {
  AccountRequestError,
  clearAccountToken,
  getCurrentAccount,
  getAccountToken,
  type AccountSummary,
} from './services/accountsApi'
import { getAccountSessionRevision, subscribeAccountSessionEvents } from './services/accountSessionEvents'

type OptionalAccountSessionState = {
  account: AccountSummary | null
  error: string | null
  isLoading: boolean
  status: 'loading' | 'authenticated' | 'unauthenticated' | 'error'
  reload: () => void
}

export function useOptionalAccountSession(): OptionalAccountSessionState {
  const [state, setState] = useState<Omit<OptionalAccountSessionState, 'reload'>>({
    account: null, error: null, isLoading: true, status: 'loading',
  })
  const [reloadKey, setReloadKey] = useState(0)
  const reload = useCallback(() => setReloadKey(key => key + 1), [])

  useEffect(() => {
    let isActive = true
    let generation = 0

    function loadAccount() {
      const request = ++generation
      const session = getAccountSessionRevision()
      const isCurrent = () => isActive && request === generation && session === getAccountSessionRevision()
      setState({ account: null, error: null, isLoading: true, status: 'loading' })

      void getCurrentAccount()
        .then(account => {
          if (isCurrent()) setState({ account, error: null, isLoading: false, status: 'authenticated' })
        })
        .catch((error: unknown) => {
          if (!isCurrent()) return
          if (error instanceof AccountRequestError && error.status === 401) {
            setState({ account: null, error: null, isLoading: false, status: 'unauthenticated' })
            // Only emit another session change when there are credentials to
            // remove. A cookie-only 401 must not create an endless reload loop.
            if (getAccountToken() !== null) clearAccountToken()
            return
          }
          setState({ account: null, error: error instanceof Error ? error.message : 'Your account could not be loaded.',
            isLoading: false, status: 'error' })
        })
    }

    const unsubscribe = subscribeAccountSessionEvents(event => {
      if (event === 'session-changed') loadAccount()
    })
    loadAccount()

    return () => {
      isActive = false
      generation++
      unsubscribe()
    }
  }, [reloadKey])

  return { ...state, reload }
}
