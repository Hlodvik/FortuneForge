// Edited: 2026-11-18T22:44:06-06:00 | Computer: Vinnabok
import { useEffect } from 'react'
import type { AccountSummary } from './services/accountsApi'
import { useOptionalAccountSession } from './useOptionalAccountSession'

type AuthenticatedAccountState = {
  account: AccountSummary | null
  error: string | null
  isLoading: boolean
  reload: () => void
}

export function useAuthenticatedAccount(returnPath = '/home', options: { unauthenticatedRedirect?: string } = {}): AuthenticatedAccountState {
  const { account, error, isLoading, reload, status } = useOptionalAccountSession()
  const redirect = options.unauthenticatedRedirect ?? `/login?returnTo=${encodeURIComponent(returnPath)}`
  useEffect(() => {
    if (status === 'unauthenticated') window.location.replace(redirect)
  }, [redirect, status])

  return { account, error, isLoading: isLoading || status === 'unauthenticated', reload }
}
