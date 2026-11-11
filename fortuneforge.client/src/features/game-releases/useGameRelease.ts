import { useEffect, useState, type ComponentType } from 'react'

type GameReleaseState<Content extends object> =
  | { status: 'local' | 'loading' }
  | ({ status: 'ready' } & Content)
  | { status: 'error'; error: string }

type ReleaseKind = 'module' | 'frame'
type ReleaseAssets = { entry: URL; styles: URL[] }
const identifier = /^[a-z0-9][a-z0-9-]{0,127}$/
const assetSegment = /^[a-z0-9_-][a-z0-9._-]*$/i

function releaseBase(value: string): URL {
  const url = new URL(value)
  const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && loopback)) ||
    url.username || url.password || url.search || url.hash) {
    throw new Error('The game release URL is invalid.')
  }
  if (!url.pathname.endsWith('/')) url.pathname += '/'
  return url
}

function assetUrl(base: URL, path: unknown, extension: RegExp): URL {
  if (typeof path !== 'string' || !extension.test(path) || !path.split('/').every(segment => assetSegment.test(segment))) {
    throw new Error('The game release contains an invalid asset path.')
  }
  return new URL(path, base)
}

async function fetchRelease(gameId: string, rawBase: string, kind: ReleaseKind, signal: AbortSignal): Promise<ReleaseAssets | null> {
  if (!identifier.test(gameId)) throw new Error('The game release ID is invalid.')
  const game = new URL('games/' + gameId + '/', releaseBase(rawBase))
  const response = await fetch(new URL('active.json', game), {
    signal, cache: 'no-store', credentials: 'omit', redirect: 'error',
  })
  if (response.status === 404) return null
  if (!response.ok) throw new Error('The active game release could not be loaded.')
  const manifest = await response.json() as Record<string, unknown> | null
  if (!manifest || manifest.gameId !== gameId || manifest.contractVersion !== 1 ||
    (manifest.kind ?? 'module') !== kind ||
    typeof manifest.releaseId !== 'string' || !identifier.test(manifest.releaseId) ||
    !Array.isArray(manifest.styles) || (kind === 'frame' && manifest.styles.length > 0)) {
    throw new Error('The active game release is incompatible.')
  }
  const base = new URL('releases/' + manifest.releaseId + '/', game)
  return {
    entry: assetUrl(base, manifest.entry, kind === 'frame' ? /\.html$/i : /\.[cm]?js$/i),
    styles: [...new Set(manifest.styles)].map(path => assetUrl(base, path, /\.css$/i)),
  }
}

function loadStylesheet(url: URL, signal: AbortSignal, stylesheets: Set<HTMLLinkElement>): Promise<void> {
  signal.throwIfAborted()
  const link = document.createElement('link')
  link.rel = 'stylesheet'
  link.href = url.href
  stylesheets.add(link)
  return new Promise((resolve, reject) => {
    const settle = (error?: Error) => {
      signal.removeEventListener('abort', abort)
      link.onload = null
      link.onerror = null
      if (error) reject(error)
      else resolve()
    }
    const abort = () => settle(new Error('The game release load was cancelled.'))
    signal.addEventListener('abort', abort, { once: true })
    link.onload = () => settle()
    link.onerror = () => settle(new Error('The game stylesheet could not be loaded.'))
    document.head.append(link)
  })
}

async function loadComponent<Props>(assets: ReleaseAssets, signal: AbortSignal, stylesheets: Set<HTMLLinkElement>) {
  const [module] = await Promise.all([
    import(/* @vite-ignore */ assets.entry.href) as Promise<{ default?: unknown }>,
    ...assets.styles.map(url => loadStylesheet(url, signal, stylesheets)),
  ])
  if (typeof module.default !== 'function') throw new Error('The game release did not provide a component.')
  return { Game: module.default as ComponentType<Props> }
}

async function loadFrame(assets: ReleaseAssets) {
  return { frameUrl: assets.entry.href }
}

function useRelease<Content extends object>(gameId: string, kind: ReleaseKind,
  loadContent: (assets: ReleaseAssets, signal: AbortSignal, stylesheets: Set<HTMLLinkElement>) => Promise<Content>,
  base: string | undefined) {
  const [attempt, setAttempt] = useState(0)
  const [loaded, setLoaded] = useState<{ request: string; state: GameReleaseState<Content> } | null>(null)
  const request = JSON.stringify([gameId, base, kind, attempt])

  useEffect(() => {
    if (!base) return
    const controller = new AbortController()
    const { signal } = controller
    const stylesheets = new Set<HTMLLinkElement>()
    const removeStyles = () => { for (const link of stylesheets) link.remove() }

    async function load(releaseUrl: string): Promise<GameReleaseState<Content>> {
      const assets = await fetchRelease(gameId, releaseUrl, kind, signal)
      if (!assets) return { status: 'local' }
      signal.throwIfAborted()
      return { status: 'ready', ...await loadContent(assets, signal, stylesheets) }
    }

    void load(base).then(state => {
      if (!signal.aborted) setLoaded({ request, state })
    }, (error: unknown) => {
      if (signal.aborted) return
      controller.abort()
      removeStyles()
      setLoaded({ request, state: { status: 'error', error: error instanceof Error ? error.message : 'The game release could not be opened.' } })
    })
    return () => { controller.abort(); removeStyles() }
  }, [base, gameId, kind, loadContent, request])

  const state: GameReleaseState<Content> = !base ? { status: 'local' }
    : loaded?.request === request ? loaded.state : { status: 'loading' }
  return { state, retry: () => setAttempt(value => value + 1) }
}

export function useGameRelease<Props>(gameId: string, base = import.meta.env.VITE_GAME_RELEASE_BASE_URL?.trim()) {
  return useRelease(gameId, 'module', loadComponent<Props>, base)
}

export function useGameFrameRelease(gameId: string, base = import.meta.env.VITE_GAME_RELEASE_BASE_URL?.trim()) {
  return useRelease(gameId, 'frame', loadFrame, base)
}
