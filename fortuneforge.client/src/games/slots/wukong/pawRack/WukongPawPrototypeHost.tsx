import { useEffect, useRef, useState } from 'react'
import { WukongPawRackPreview } from './WukongPawRackPreview'
import { isLoopbackPrototypeLocation } from './localOnly'
import type { WukongPawPrototypeState } from './types'

const prefix = '/__prototype/wukong-paws'
type TransportState = WukongPawPrototypeState & {
  sessionId: string
  gameId: string
  mode: 'synthetic-local'
  liveEarningEnabled: false
  liveConsumptionEnabled: false
  error?: string | null
}
type PendingCommand = { path: string; body: Record<string, unknown> }

function readState(value: unknown): TransportState {
  const state = value as Partial<TransportState> | null
  if (!state || state.mode !== 'synthetic-local' || state.gameId !== 'wukong-journey-v1' ||
    state.liveEarningEnabled !== false || state.liveConsumptionEnabled !== false ||
    !Array.isArray(state.grants) || !Array.isArray(state.reels) || state.reels.length !== 5 ||
    !Array.isArray(state.wagerOptions) || typeof state.sessionId !== 'string' ||
    !['ready', 'placed', 'spinning', 'settled', 'blocked'].includes(state.phase ?? '')) {
    throw new Error('The local host did not return a disabled synthetic Wukong fixture.')
  }
  return state as TransportState
}

export default function WukongPawPrototypeHost() {
  const [state, setState] = useState<TransportState | null>(null)
  const [selectedWager, setSelectedWager] = useState(100)
  const [pending, setPending] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [recoverable, setRecoverable] = useState(false)
  const busy = useRef(false)
  const outstanding = useRef<PendingCommand | null>(null)

  useEffect(() => {
    if (!isLoopbackPrototypeLocation(window.location)) return
    const abort = new AbortController()
    void fetch(prefix, { credentials: 'same-origin', signal: abort.signal, cache: 'no-store' })
      .then(async response => {
        const next = readState(await response.json())
        if (!response.ok) throw new Error(next.error ?? 'The local fixture is unavailable.')
        if (!abort.signal.aborted) { setState(next); setSelectedWager(next.wagerPoints) }
      })
      .catch((failure: unknown) => {
        if (!abort.signal.aborted) setError(failure instanceof Error ? failure.message : 'Local fixture unavailable.')
      })
      .finally(() => { if (!abort.signal.aborted) setPending(false) })
    return () => abort.abort()
  }, [])

  async function submit(command: PendingCommand) {
    if (busy.current) return
    busy.current = true
    outstanding.current = command
    setPending(true)
    setError(null)
    try {
      const response = await fetch(prefix + command.path, {
        method: 'POST', credentials: 'same-origin', cache: 'no-store',
        headers: { 'content-type': 'application/json' }, body: JSON.stringify(command.body),
        signal: AbortSignal.timeout(20_000),
      })
      const next = readState(await response.json())
      setState(next)
      setSelectedWager(next.wagerPoints)
      setError(response.ok ? null : next.error ?? 'The fixture rejected this action.')
      outstanding.current = null
      setRecoverable(false)
    } catch (failure: unknown) {
      // A lost response must retry the identical command/key, never generate a
      // second admission or restore a fixture paw on the client.
      setError(`Response unknown. ${failure instanceof Error ? failure.message : 'The local host could not be reached.'}`)
      setRecoverable(true)
    } finally {
      busy.current = false
      setPending(false)
    }
  }

  function command(path: string, body: Record<string, unknown> = {}) {
    if (busy.current || outstanding.current) return
    void submit({ path, body: { ...body, requestId: crypto.randomUUID() } })
  }

  if (!isLoopbackPrototypeLocation(window.location)) return <p>This preview is loopback-only.</p>
  if (!state) return <main className="wukong-paw-preview"><p role="status">{error ?? 'Connecting to the local synthetic fixture…'}</p>
    {error && <button type="button" onClick={() => window.location.reload()}>Retry connection</button>}</main>

  return <>
    {recoverable && <aside className="wukong-paw-preview__recovery" role="alert">
      <p>{error} Other actions are locked until the same request is resolved.</p>
      <button type="button" disabled={pending} onClick={() => {
        if (outstanding.current) void submit(outstanding.current)
      }}>Retry the same request</button>
    </aside>}
    <WukongPawRackPreview {...state} wagerPoints={state.placement ? state.wagerPoints : selectedWager}
      pending={pending || recoverable} error={recoverable ? null : error}
      onWager={wager => { if (!busy.current && !outstanding.current && state.phase === 'ready') setSelectedWager(wager) }}
      onPlace={(grantId, column) => command('/place', { grantId, column, wagerPoints: selectedWager })}
      onClear={() => { if (state.placement) command('/clear', { placementId: state.placement.id }) }}
      onSpin={() => command('/spin', { placementId: state.placement?.id ?? null, wagerPoints: selectedWager })}
      onNextPreview={() => command('/next-preview')}
      onResetFixture={() => command('/reset-fixture')}
      onFixtureEarnSymbolChange={enabled => command('/fixture-earn-symbol', { enabled })} />
  </>
}
