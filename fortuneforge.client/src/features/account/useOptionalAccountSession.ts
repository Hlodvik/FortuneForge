import { useEffect, useState } from 'react'
import {
  AccountRequestError,
  clearAccountToken,
  getCurrentAccount,
  type AccountSummary,
} from './services/accountsApi'

type OptionalAccountSessionState = {
  account: AccountSummary | null
  isLoading: boolean
}

export function useOptionalAccountSession(): OptionalAccountSessionState {
  const [account, setAccount] = useState<AccountSummary | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    let isActive = true

    void getCurrentAccount()
      .then((currentAccount) => {
        if (isActive) {
          setAccount(currentAccount)
        }
      })
      .catch((error: unknown) => {
        if (error instanceof AccountRequestError && error.status === 401) {
          clearAccountToken()
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
  }, [])

  return { account, isLoading }
}
