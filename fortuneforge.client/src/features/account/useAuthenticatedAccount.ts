import { useCallback, useEffect, useState } from 'react'
import {
  AccountRequestError,
  clearAccountToken,
  getCurrentAccount,
  type AccountSummary,
} from './services/accountsApi'

type AuthenticatedAccountState = {
  account: AccountSummary | null
  error: string | null
  isLoading: boolean
  reload: () => void
}

export function useAuthenticatedAccount(returnPath = '/home'): AuthenticatedAccountState {
  const [account, setAccount] = useState<AccountSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadKey, setReloadKey] = useState(0)
  const reload = useCallback(() => setReloadKey((key) => key + 1), [])

  useEffect(() => {
    const loginUrl = `/login?returnTo=${encodeURIComponent(returnPath)}`
    let isActive = true
    setIsLoading(true)
    setError(null)
    void getCurrentAccount()
      .then((loadedAccount) => {
        if (isActive) {
          setAccount(loadedAccount)
        }
      })
      .catch((requestError: unknown) => {
        if (requestError instanceof AccountRequestError && requestError.status === 401) {
          clearAccountToken()
          window.location.replace(loginUrl)
          return
        }

        if (isActive) {
          setError(requestError instanceof Error
            ? requestError.message
            : 'Your account could not be loaded.')
        }
      })
      .finally(() => {
        if (isActive) {
          setIsLoading(false)
        }
      })

    return () => {
      isActive = false
    }
  }, [reloadKey, returnPath])

  return { account, error, isLoading, reload }
}
