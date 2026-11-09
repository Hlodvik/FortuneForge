// @vitest-environment jsdom
import { act, lazy, Suspense } from 'react'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useGameExperience } from '@fortuneforge/client-presentation/game-experience'
import { GameLoadingRouteProvider } from '../game-loading/GameLoadingRouteContext'
import { resolveGameLoadingEntry } from '../game-loading/gameLoadingRegistry'
import { gameExperienceIdentity, CANONICAL_EXPERIENCE_GAME_IDS } from './gameExperienceIdentity'
import { publishAccountSessionEvent } from '../account/services/accountSessionEvents'

const api = vi.hoisted(() => ({ fetch: vi.fn<typeof fetch>() }))
vi.mock('../account/services/accountsApi', () => ({ fetchWithAccountSession: api.fetch }))
const inventory = await import(pathToFileURL(resolve(process.cwd(), '../scripts/game-smoke-inventory.mjs')).href) as {
  nonSlotSmokeInventory: { smokePath: string; catalogPath?: string }[]
  slotSmokeInventory: [string, string][]
}
const paid = [...inventory.nonSlotSmokeInventory.map(game => game.catalogPath ?? game.smokePath),
  ...inventory.slotSmokeInventory.map(([route]) => `/slots/${route}`)]
const demo = [...inventory.slotSmokeInventory.map(([route]) => `/slots/${route}/demo`), '/demo/cards/blackjack', '/demo/cards/texas-holdem']
let host: HTMLDivElement, root: Root
function value() {
  return { schemaVersion: 1, userId: 'experience-player', generatedAtUtc: '2026-11-02T00:00:00Z', games: CANONICAL_EXPERIENCE_GAME_IDS.map(gameId => ({
    gameId, state: 'unknown', hasPlayed: null, provenance: [], coverage: 'partial', firstPlayedAtUtc: null,
  })) }
}
function Probe({ label = 'game' }: { label?: string }) {
  const experience = useGameExperience()
  return <output data-label={label} data-game={experience.gameId} data-status={experience.status}
    data-played={String(experience.hasPlayed)} data-player={experience.userId ?? ''} />
}
beforeEach(() => {
  vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  api.fetch.mockReset(); api.fetch.mockImplementation(async () => new Response(JSON.stringify(value())))
  publishAccountSessionEvent('session-changed')
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('player-game knowledge during universal shared loading', () => {
  it('covers the 44-game/65-route inventory, including 19 distinct slots and hidden playable games, with one batch', async () => {
    expect(paid).toHaveLength(44); expect(demo).toHaveLength(21)
    expect(new Set(paid.map(gameExperienceIdentity)).size).toBe(44)
    await act(async () => root.render(<>{[...paid, ...demo].map(path => <GameLoadingRouteProvider key={path}
      entry={resolveGameLoadingEntry(path)} pathname={path}><Probe label={path} /></GameLoadingRouteProvider>)}</>))
    expect(api.fetch).toHaveBeenCalledTimes(1)
    for (const path of paid) {
      const output = [...host.querySelectorAll('output')].find(item => item.dataset.label === path)!
      expect(output.dataset.game, path).toBe(gameExperienceIdentity(path))
      expect(output.dataset.status, path).toBe('unknown')
      expect(output.dataset.played, path).toBe('null')
      expect(output.dataset.player, path).toBe('experience-player')
    }
    for (const path of demo) {
      const output = [...host.querySelectorAll('output')].find(item => item.dataset.label === path)!
      expect(output.dataset.status, path).toBe('guest')
      expect(output.dataset.played, path).toBe('null')
      expect(output.dataset.player, path).toBe('')
    }
    for (const id of ['asteroids', 'flappy', 'snake', 'horse-flight']) expect(gameExperienceIdentity(`/games/${id}`)).toBe(id)
    expect(new Set(inventory.slotSmokeInventory.map(([route]) => gameExperienceIdentity(`/slots/${route}`))).size).toBe(19)
  })

  it('provides explicit loading then proven knowledge while the playable module is still pending', async () => {
    let finishApi!: (response: Response) => void
    api.fetch.mockReturnValueOnce(new Promise(resolve => { finishApi = resolve }))
    let finishModule!: (module: { default: typeof Probe }) => void
    const Game = lazy(() => new Promise<{ default: typeof Probe }>(resolve => { finishModule = resolve }))
    await act(async () => root.render(<GameLoadingRouteProvider entry={resolveGameLoadingEntry('/cards/blackjack')} pathname="/cards/blackjack">
      <Suspense fallback={<Probe label="loading-screen" />}><Game /></Suspense>
    </GameLoadingRouteProvider>))
    expect(host.querySelector('output')?.dataset).toMatchObject({ label: 'loading-screen', status: 'loading', played: 'null' })
    const response = value(), row = response.games.find(item => item.gameId === 'blackjack')!
    Object.assign(row, { state: 'known', hasPlayed: false, provenance: ['complete-authoritative-coverage'], coverage: 'complete' })
    await act(async () => finishApi(new Response(JSON.stringify(response))))
    expect(host.querySelector('output')?.dataset).toMatchObject({ label: 'loading-screen', status: 'known', played: 'false' })
    await act(async () => finishModule({ default: Probe }))
    expect(host.querySelector('output')?.dataset).toMatchObject({ label: 'game', status: 'known', played: 'false' })
  })

  it('keeps missing/error data null and never changes a playable child or adds tutorial UI', async () => {
    api.fetch.mockRejectedValueOnce(new Error('offline'))
    await act(async () => root.render(<GameLoadingRouteProvider pathname="/games/snake"><Probe /></GameLoadingRouteProvider>))
    expect(host.querySelector('output')?.dataset).toMatchObject({ game: 'snake', status: 'error', played: 'null' })
    expect(host.querySelectorAll('output')).toHaveLength(1)
    expect(host.querySelector('button, dialog, [role="dialog"]')).toBeNull()
  })

  it('does not fetch authenticated history for a demo-only loader or a non-game route', async () => {
    await act(async () => root.render(<GameLoadingRouteProvider pathname="/slots/reel-riches/demo"><Probe /></GameLoadingRouteProvider>))
    expect(host.querySelector('output')?.dataset.status).toBe('guest')
    await act(async () => root.render(<GameLoadingRouteProvider pathname="/home"><Probe /></GameLoadingRouteProvider>))
    expect(host.querySelector('output')?.dataset).toMatchObject({ status: 'unavailable', played: 'null' })
    expect(api.fetch).not.toHaveBeenCalled()
  })
})
