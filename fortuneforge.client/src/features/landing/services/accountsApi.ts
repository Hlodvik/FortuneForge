export type CreateAccountInput = {
  playerName: string
  email: string
  password: string
}

export type LoginInput = {
  email: string
  password: string
  remainLoggedIn: boolean
}

export type AccountSummary = {
  userId: string
  playerName: string
  email: string
  createdAtUtc: string
  balances: {
    slotsCredits: number
    freeGames: number
  }
  slots: {
    spinsPlayed: number
    wins: number
    losses: number
    creditsWagered: number
    creditsWon: number
    netCredits: number
  }
  role: string
}

export type AuthenticationResponse = {
  account: AccountSummary
  token: string
  expiresAtUtc: string
}

export type CreateAccountResponse = {
  account: AccountSummary
  emailVerificationRequired: boolean
  verificationEmailSent: boolean
}

export type ResendVerificationResponse = {
  emailVerified: boolean
  verificationEmailSent: boolean
}

export type SlotSpinHistoryItem = {
  spinId: string
  gameId: string
  wageredSlotsCredits: number
  wonSlotsCredits: number
  netSlotsCredits: number
  result: 'win' | 'loss'
  createdAtUtc: string
}

export type SlotHistoryResponse = {
  spins: SlotSpinHistoryItem[]
}

type ProblemDetails = {
  title?: string
  detail?: string
}

export class AccountRequestError extends Error {
  readonly status: number
  readonly title?: string

  constructor(
    message: string,
    status: number,
    title?: string,
  ) {
    super(message)
    this.name = 'AccountRequestError'
    this.status = status
    this.title = title
  }
}

const accountTokenStorageKey = 'fortune-forge.account-token'

export function createAccount(input: CreateAccountInput): Promise<CreateAccountResponse> {
  return accountRequest<CreateAccountResponse>('/api/accounts', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function resendVerification(
  email: string,
  password: string,
): Promise<ResendVerificationResponse> {
  return accountRequest<ResendVerificationResponse>('/api/accounts/resend-verification', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  })
}

export async function loginAccount(input: LoginInput): Promise<AuthenticationResponse> {
  const authentication = await accountRequest<AuthenticationResponse>('/api/accounts/login', {
    method: 'POST',
    body: JSON.stringify(input),
  })
  // Older API deployments may omit the bearer token and rely on the cookie.
  // Avoid persisting the string "undefined" while hosting and API roll forward.
  if (typeof authentication.token === 'string' && authentication.token.length >= 32) {
    storeAccountToken(authentication.token, input.remainLoggedIn)
  } else {
    clearAccountToken()
  }
  return authentication
}

export function getCurrentAccount(): Promise<AccountSummary> {
  return accountRequest<AccountSummary>('/api/accounts/me', { method: 'GET' }, true)
}

export function getSlotHistory(limit = 25): Promise<SlotHistoryResponse> {
  return accountRequest<SlotHistoryResponse>(
    `/api/accounts/me/history?limit=${encodeURIComponent(limit)}`,
    { method: 'GET' },
    true,
  )
}

export function updateCurrentAccount(playerName: string): Promise<AccountSummary> {
  return accountRequest<AccountSummary>('/api/accounts/me', {
    method: 'PATCH',
    body: JSON.stringify({ playerName }),
  }, true)
}

export function changeAccountPassword(
  currentPassword: string,
  newPassword: string,
): Promise<AccountSummary> {
  return accountRequest<AccountSummary>('/api/accounts/change-password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword }),
  }, true)
}

export async function logoutAccount(): Promise<void> {
  try {
    await accountRequest<void>('/api/accounts/logout', { method: 'POST' }, true)
  } finally {
    // A failed network request must not leave local credentials behind after
    // the user deliberately chooses to log out.
    clearAccountToken()
  }
}

export async function deactivateCurrentAccount(password: string): Promise<void> {
  await accountRequest<void>('/api/accounts/me', {
    method: 'DELETE',
    body: JSON.stringify({ password }),
  }, true)
  clearAccountToken()
}

export function getAccountToken(): string | null {
  // Session storage wins if both stores somehow contain a token, because it is
  // the user's most recent, explicitly session-scoped login choice.
  return readStoredToken('sessionStorage') ?? readStoredToken('localStorage')
}

export function clearAccountToken(): void {
  removeStoredToken('sessionStorage')
  removeStoredToken('localStorage')
}

function storeAccountToken(token: string, remainLoggedIn: boolean): void {
  clearAccountToken()
  const preferredStorage = remainLoggedIn ? 'localStorage' : 'sessionStorage'

  try {
    window[preferredStorage].setItem(accountTokenStorageKey, token)
  } catch {
    // The HttpOnly session cookie remains the primary credential. Storage is a
    // compatibility fallback for browsers or privacy modes that allow only one.
  }
}

function readStoredToken(storageName: 'localStorage' | 'sessionStorage'): string | null {
  try {
    return window[storageName].getItem(accountTokenStorageKey)
  } catch {
    return null
  }
}

function removeStoredToken(storageName: 'localStorage' | 'sessionStorage'): void {
  try {
    window[storageName].removeItem(accountTokenStorageKey)
  } catch {
    // Storage can be unavailable under strict browser privacy policies.
  }
}

async function accountRequest<T>(
  path: string,
  init: RequestInit,
  authenticated = false,
): Promise<T> {
  const response = authenticated
    ? await fetchWithAccountSession(path, init)
    : await fetch(path, {
        ...init,
        headers: jsonHeaders(init.headers),
        credentials: 'include',
      })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as ProblemDetails | null
    throw new AccountRequestError(
      problem?.detail ?? problem?.title ?? `Account request failed (${response.status}).`,
      response.status,
      problem?.title,
    )
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export async function fetchWithAccountSession(
  path: string,
  init: RequestInit,
): Promise<Response> {
  const sessionToken = readStoredToken('sessionStorage')
  const persistentToken = readStoredToken('localStorage')
  const tokenAttempts = [sessionToken, persistentToken]
    .filter((token, index, tokens): token is string =>
      token !== null && tokens.indexOf(token) === index)

  for (const token of tokenAttempts) {
    const response = await fetch(path, {
      ...init,
      headers: authenticatedHeaders(init.headers, token),
      credentials: 'include',
    })
    if (response.status !== 401) {
      if (token === persistentToken && sessionToken !== null && sessionToken !== persistentToken) {
        removeStoredToken('sessionStorage')
      }
      return response
    }
  }

  // A token saved by an older tab can expire while the HttpOnly cookie is
  // still valid. Retrying without Authorization lets the server evaluate the
  // cookie instead of allowing the stale bearer token to mask it.
  const cookieResponse = await fetch(path, {
    ...init,
    headers: jsonHeaders(init.headers),
    credentials: 'include',
  })
  if (cookieResponse.ok && tokenAttempts.length > 0) {
    clearAccountToken()
  }

  return cookieResponse
}

function jsonHeaders(source?: HeadersInit): Headers {
  const headers = new Headers(source)
  headers.set('Content-Type', 'application/json')
  return headers
}

function authenticatedHeaders(source: HeadersInit | undefined, token: string): Headers {
  const headers = jsonHeaders(source)
  headers.set('Authorization', `Bearer ${token}`)
  return headers
}
