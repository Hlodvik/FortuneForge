export function walletPracticeModeEnabled(): boolean {
  return new URLSearchParams(window.location.search).get('mode') === 'practice'
}

export function practiceAccountFetch(baseFetch: typeof fetch): typeof fetch {
  return (input, init = {}) => {
    const headers = new Headers(init.headers)
    headers.set('X-FortuneForge-Practice', 'true')
    return baseFetch(input, { ...init, headers })
  }
}
