import type { AccountSummary } from '../../features/account/services/accountsApi'

/** Keeps a newer locally refreshed account while the authenticated hook re-renders its prior snapshot. */
export function synchronizeAsteroidsRouteAccount(
  currentAccount: AccountSummary,
  authenticatedAccount: AccountSummary,
  hasNewAuthenticatedSnapshot: boolean,
): AccountSummary {
  return hasNewAuthenticatedSnapshot ? authenticatedAccount : currentAccount
}
