// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { loadCatalogueVisibility, notifyCatalogueChanged, useCatalogueVisibility, type CatalogueVisibility } from './catalogVisibility'

let version = 0
let host: HTMLDivElement
let root: Root
const snapshot = (visible: boolean, revision = version): CatalogueVisibility => ({
  version: revision,
  games: [{ id: 'hearts', route: '/cards/hearts', visible }],
})

function Catalogue({ name }: { name: string }) {
  const visibility = useCatalogueVisibility()
  return <section data-catalogue={name}>
    {visibility.isVisible('/cards/hearts') && <a href="/cards/hearts">Play Hearts</a>}
    {visibility.error && <span role="alert">{visibility.error}</span>}
  </section>
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  // Avoid a real cross-tab channel or network request in this bounded test.
  vi.stubGlobal('BroadcastChannel', undefined)
  vi.stubGlobal('fetch', vi.fn())
  version += 10
  notifyCatalogueChanged(snapshot(true))
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

const mount = () => act(async () => { root.render(<><Catalogue name="home" /><Catalogue name="games" /></>) })

describe('mounted catalogue synchronization', () => {
  it('applies an authoritative admin receipt to every mounted subscriber immediately', async () => {
    await mount()
    expect(host.querySelectorAll('a[href="/cards/hearts"]')).toHaveLength(2)

    act(() => notifyCatalogueChanged(snapshot(false, ++version)))
    expect(host.querySelectorAll('a[href="/cards/hearts"]')).toHaveLength(0)
    expect(fetch).not.toHaveBeenCalled()

    act(() => notifyCatalogueChanged(snapshot(true, ++version)))
    expect(host.querySelectorAll('a[href="/cards/hearts"]')).toHaveLength(2)
  })

  it('does not let an older in-flight response relist a newly hidden game', async () => {
    await mount()
    let resolveResponse!: (response: Response) => void
    vi.mocked(fetch).mockReturnValueOnce(new Promise(resolve => { resolveResponse = resolve }))
    const older = snapshot(true)
    const request = loadCatalogueVisibility(true)
    expect(fetch).toHaveBeenCalledTimes(1)

    act(() => notifyCatalogueChanged(snapshot(false, ++version)))
    expect(host.querySelectorAll('a')).toHaveLength(0)

    await act(async () => {
      resolveResponse(Response.json(older))
      expect(await request).toEqual(snapshot(false))
    })
    expect(host.querySelectorAll('a')).toHaveLength(0)
  })

  it('preserves a known hidden game when refresh fails, then recovers on a newer response', async () => {
    await mount()
    act(() => notifyCatalogueChanged(snapshot(false, ++version)))
    vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 503 }))

    await act(async () => {
      await expect(loadCatalogueVisibility(true)).rejects.toThrow('Catalogue availability could not be checked')
    })
    expect(host.querySelectorAll('a')).toHaveLength(0)
    expect(host.querySelectorAll('[role="alert"]')).toHaveLength(2)

    vi.mocked(fetch).mockResolvedValueOnce(Response.json(snapshot(true, ++version)))
    await act(async () => { await loadCatalogueVisibility(true) })
    expect(host.querySelectorAll('a')).toHaveLength(2)
    expect(host.querySelectorAll('[role="alert"]')).toHaveLength(0)
  })

  it('ignores an older admin snapshot as well as an older public response', async () => {
    await mount()
    const older = snapshot(true)
    act(() => notifyCatalogueChanged(snapshot(false, ++version)))
    act(() => notifyCatalogueChanged(older))
    expect(host.querySelectorAll('a')).toHaveLength(0)
  })
})
