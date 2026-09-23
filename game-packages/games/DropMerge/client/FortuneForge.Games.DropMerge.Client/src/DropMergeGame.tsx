import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react'
import { DropMergeGatewayError } from './httpDropMergeGateway'
import type { DropMergeGameState, DropMergeGateway, DropMergeMergeStep } from './contracts'
import { tileClass, tileLabel, tileTone } from './dropMergeHelpers'

export type DropMergeGameProps = Readonly<{
  gateway: DropMergeGateway
  backHref?: string
  playerName?: string
  tableLabel?: string
}>

const columns = Array.from({ length: 7 }, (_, index) => index)
const fastDropRatio = 0.38
const mergeDurationMilliseconds = 300
const landingPauseMilliseconds = 100
const chainMergePauseMilliseconds = 70
const bestTileKey = 'fortuneforge:drop-merge:best-tile'

export function DropMergeGame({ gateway, backHref = '/', playerName = 'Player', tableLabel = 'Local free play' }: DropMergeGameProps) {
  const [game, setGame] = useState<DropMergeGameState | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [falling, setFalling] = useState<{ column: number; row: number; value: number; duration: number } | null>(null)
  const [targetColumn, setTargetColumn] = useState(3)
  const [timerRemaining, setTimerRemaining] = useState(0)
  const [isPaused, setIsPaused] = useState(false)
  const [mergeStep, setMergeStep] = useState<DropMergeMergeStep | null>(null)
  const [visualTiles, setVisualTiles] = useState<readonly number[] | null>(null)
  const [bestTile, setBestTile] = useState(readBestTile)
  const timerRemainingRef = useRef(0)
  const dragStart = useRef<{ x: number; pointerId: number; moved: boolean } | null>(null)
  const suppressClick = useRef(false)
  const updateTimerRemaining = useCallback((milliseconds: number) => {
    const next = Math.max(0, milliseconds)
    timerRemainingRef.current = next
    setTimerRemaining(next)
  }, [])

  const run = useCallback(async (action: () => Promise<DropMergeGameState>) => {
    if (busy) return
    setBusy(true)
    setError(null)
    setFalling(null)
    setMergeStep(null)
    setVisualTiles(null)
    try {
      setGame(await action())
    } catch (reason) {
      setError(messageForError(reason))
    } finally {
      setBusy(false)
    }
  }, [busy])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(status => status.available ? gateway.startGame(undefined, controller.signal) : Promise.reject(new Error('The Drop Merge service is unavailable.')))
      .then(nextGame => {
        setGame(nextGame)
        setTargetColumn(3)
      })
      .catch((reason: unknown) => {
        if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason))
      })
    return () => controller.abort()
  }, [gateway])

  const drop = useCallback((column: number) => {
    if (!game || busy || isPaused || game.phase !== 'playing') return
    const value = game.currentTile
    const dropDuration = Math.max(190, Math.round(game.dropDurationMilliseconds * fastDropRatio))

    void (async () => {
      setBusy(true)
      setError(null)
      setMergeStep(null)
      setVisualTiles(null)
      setTargetColumn(column)
      try {
        const localDropRow = lowestEmptyRow(game.tiles, game.columns, column)
        if (localDropRow >= 0) {
          setFalling({ column, row: localDropRow, value, duration: dropDuration })
          await wait(dropDuration)
          await wait(landingPauseMilliseconds)
        }

        const nextGame = await gateway.drop(game.gameId, column)
        if (localDropRow >= 0 && nextGame.dropRow >= 0) {
          setFalling(null)
          setGame(nextGame)
          const landedTiles = [...game.tiles]
          landedTiles[(localDropRow * game.columns) + column] = value
          setVisualTiles(landedTiles)
          for (const [stepIndex, step] of nextGame.mergeSteps.entries()) {
            setMergeStep(step)
            await wait(mergeDurationMilliseconds)
            setMergeStep(null)
            setVisualTiles(step.tilesAfter)
            if (stepIndex < nextGame.mergeSteps.length - 1) {
              await wait(chainMergePauseMilliseconds)
            }
          }
          if (nextGame.removedTile > 0) {
            setVisualTiles(nextGame.tiles)
            await wait(landingPauseMilliseconds)
          }
        } else {
          setGame(nextGame)
        }
      } catch (reason) {
        setError(messageForError(reason))
      } finally {
        setFalling(null)
        setMergeStep(null)
        setVisualTiles(null)
        setBusy(false)
      }
    })()
  }, [busy, game, gateway, isPaused])

  useEffect(() => {
    if (game) updateTimerRemaining(game.dropTimerMilliseconds)
  }, [game?.dropTimerMilliseconds, game?.gameId, game?.moves, updateTimerRemaining])

  useEffect(() => {
    if (!game || game.highestTile <= bestTile) return
    setBestTile(game.highestTile)
    try { window.localStorage.setItem(bestTileKey, String(game.highestTile)) } catch { /* storage is optional */ }
  }, [bestTile, game])

  useEffect(() => {
    if (!game || busy || isPaused || game.phase !== 'playing') return
    const duration = timerRemainingRef.current
    if (duration <= 0) {
      drop(targetColumn)
      return
    }
    const startedAt = Date.now()
    const update = () => updateTimerRemaining(duration - (Date.now() - startedAt))
    const interval = window.setInterval(update, 100)
    const timeout = window.setTimeout(() => {
      update()
      drop(targetColumn)
    }, duration)
    return () => {
      window.clearInterval(interval)
      window.clearTimeout(timeout)
      update()
    }
  }, [busy, drop, game?.gameId, game?.moves, game?.phase, isPaused, targetColumn, updateTimerRemaining])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!game || busy) return
      const key = event.key.toLowerCase()

      if (key === 'p') {
        if (game.phase === 'playing') {
          event.preventDefault()
          setIsPaused(paused => !paused)
        }
        return
      }

      if (isPaused && event.key === 'Enter') {
        event.preventDefault()
        setIsPaused(false)
        return
      }

      if (isPaused || game.phase !== 'playing') return

      if (event.key === 'ArrowLeft' || key === 'a') {
        event.preventDefault()
        setTargetColumn(selected => Math.max(0, selected - 1))
        return
      }

      if (event.key === 'ArrowRight' || key === 'd') {
        event.preventDefault()
        setTargetColumn(selected => Math.min(6, selected + 1))
        return
      }

      if (event.code === 'Space' || key === 's') {
        event.preventDefault()
        drop(targetColumn)
        return
      }

      const column = Number(event.key) - 1
      if (!Number.isInteger(column) || column < 0 || column > 6) return
      event.preventDefault()
      drop(column)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [busy, drop, game, isPaused, targetColumn])

  const newGame = () => {
    setTargetColumn(3)
    setIsPaused(false)
    void run(() => game ? gateway.reset(game.gameId) : gateway.startGame())
  }
  const undo = () => {
    setIsPaused(false)
    if (game) void run(() => gateway.undo(game.gameId))
  }
  const boardTiles = visualTiles ?? game?.tiles ?? []
  const columnForPointer = (event: ReactPointerEvent<HTMLElement>) => {
    const bounds = event.currentTarget.getBoundingClientRect()
    return Math.max(0, Math.min(6, Math.floor(((event.clientX - bounds.left) / bounds.width) * 7)))
  }
  const beginDrag = (event: ReactPointerEvent<HTMLElement>) => {
    dragStart.current = { x: event.clientX, pointerId: event.pointerId, moved: false }
    event.currentTarget.setPointerCapture?.(event.pointerId)
    setTargetColumn(columnForPointer(event))
  }
  const moveDrag = (event: ReactPointerEvent<HTMLElement>) => {
    const drag = dragStart.current
    if (!drag || drag.pointerId !== event.pointerId) return
    if (Math.abs(event.clientX - drag.x) > 8) drag.moved = true
    setTargetColumn(columnForPointer(event))
  }
  const finishDrag = (event: ReactPointerEvent<HTMLElement>) => {
    const drag = dragStart.current
    dragStart.current = null
    if (!drag || drag.pointerId !== event.pointerId || !drag.moved) return
    const column = columnForPointer(event)
    suppressClick.current = true
    drop(column)
  }

  return <div className="ff-drop-merge-page">
    <header className="ff-drop-merge-header">
      <a className="ff-drop-merge-brand" href={backHref} aria-label="Fortune Forge home"><span aria-hidden="true">✦</span><strong>Fortune Forge</strong></a>
      <a className="ff-drop-merge-games" href={backHref}>Other games</a>
      <div className="ff-drop-merge-account"><strong>{playerName}</strong><span>{tableLabel}</span></div>
    </header>

    <main className="ff-drop-merge-main">
      <section className="ff-drop-merge-title">
        <div>
          <small>Seven-column number arcade</small>
          <h1>Drop Merge</h1>
          <p>A box slowly descends toward your chosen column, then falls automatically. Build chains, make a 1024 tile, and watch the board evolve.</p>
        </div>
        <div className="ff-drop-merge-actions">
          <button type="button" onClick={newGame} disabled={busy}>{busy ? 'Dropping…' : 'New run'}</button>
          <button type="button" onClick={undo} disabled={busy || !game?.canUndo}>Undo</button>
          <button type="button" onClick={() => setIsPaused(paused => !paused)} disabled={busy || game?.phase !== 'playing'}>{isPaused ? 'Resume' : 'Pause'}</button>
        </div>
      </section>

      {game ? <div className="ff-drop-merge-layout">
        <section className="ff-drop-merge-play-area">
          <section className="ff-drop-merge-stats" aria-live="polite">
            <div><small>Score</small><strong>{game.score.toLocaleString()}</strong></div>
            <div><small>Best combo</small><strong>{game.bestCombo}×</strong></div>
            <div><small>Best tile</small><strong>{tileLabel(Math.max(bestTile, game.highestTile))}</strong><span>this run {tileLabel(game.highestTile)}</span></div>
            <div><small>Tempo</small><strong>{game.tempoLevel}</strong><span>gentle ramp</span></div>
          </section>

          <div className="ff-drop-merge-board-shell">
            <div className="ff-drop-merge-drop-preview" aria-label={`Current box above column ${targetColumn + 1}`} style={{ '--preview-duration': `${game.dropTimerMilliseconds}ms` } as CSSProperties}>
              {columns.map(column => <span className="ff-drop-merge-drop-preview-slot" key={column}>{!busy && targetColumn === column && <span className={`ff-drop-merge-preview-tile ${tileClass(game.currentTile)} ${tileTone(game.currentTile)}`} style={previewStyle(game, timerRemaining, isPaused)} key={`${game.moves}-${targetColumn}`}>{tileLabel(game.currentTile)}</span>}</span>)}
            </div>
            <div className="ff-drop-merge-column-board" aria-label="Drop Merge seven columns. Drag across the board and release to drop." onPointerDown={beginDrag} onPointerMove={moveDrag} onPointerUp={finishDrag} onPointerCancel={() => { dragStart.current = null }}>
              {columns.map(column => <button
                className={`ff-drop-merge-column ${targetColumn === column ? 'is-selected' : ''}`}
                type="button"
                key={column}
                onClick={() => { if (suppressClick.current) { suppressClick.current = false; return } drop(column) }}
                disabled={busy || isPaused || game.phase !== 'playing'}
                aria-label={`Drop the current tile into column ${column + 1}`}>
                <span className="ff-drop-merge-column-stack">
                  {Array.from({ length: game.rows }, (_, row) => {
                    const value = boardTiles[(row * game.columns) + column]
                    const isAnimatingSource = mergeStep?.sources.some(source => source.row === row && source.column === column)
                    return <span className="ff-drop-merge-column-slot" key={row}>
                      {value && !isAnimatingSource ? <span className={`ff-drop-merge-tile ${tileClass(value)} ${tileTone(value)}`} aria-label={`Tile ${value}`}>{tileLabel(value)}</span> : null}
                    </span>
                  })}
                  {falling?.column === column && <span className={`ff-drop-merge-falling-tile ${tileClass(falling.value)} ${tileTone(falling.value)}`} style={fallingPosition(falling.row, game.rows, falling.duration)} aria-hidden="true">{tileLabel(falling.value)}</span>}
                </span>
              </button>)}
              {mergeStep && <span className="ff-drop-merge-merge-layer" aria-hidden="true">{mergeStep.sources.map((source, index) => <span className={`ff-drop-merge-merge-tile ff-drop-merge-tile ${tileClass(source.value)} ${tileTone(source.value)}`} style={mergePosition(source, mergeStep.target)} key={`${source.row}-${source.column}-${index}`}>{tileLabel(source.value)}</span>)}</span>}
            </div>
            {game.phase !== 'playing' && <div className="ff-drop-merge-overlay"><small>Drop blocked</small><strong>Run complete</strong><span>{game.score.toLocaleString()} points · {game.bestCombo}× best combo</span><button type="button" onClick={newGame} disabled={busy}>Play again</button></div>}
            {isPaused && game.phase === 'playing' && <div className="ff-drop-merge-paused-overlay"><small>Timed drop paused</small><strong>PAUSED</strong><button type="button" onClick={() => setIsPaused(false)}>Resume</button><span>Press P or Enter to resume</span></div>}
          </div>
        </section>

        <aside className="ff-drop-merge-sidebar">
          <section className="ff-drop-merge-queue"><div><small>Coming next</small><strong className={`ff-drop-merge-queue-tile ${tileClass(game.nextTile)}`}>{tileLabel(game.nextTile)}</strong></div><div className="ff-drop-merge-next"><small>Current tile</small><strong>{tileLabel(game.currentTile)}</strong></div></section>
          <section className={`ff-drop-merge-surge ${game.lastEvent === 'big-tile-reached' ? 'is-active' : ''}`}><div className="ff-drop-merge-surge-top"><small>Big tile bonus</small><strong>{game.smallestTileClearCount}</strong></div><p>Make a {game.nextBigTile.toLocaleString()} tile to clear the smallest tile tier and shift future tiles upward.</p>{game.lastEvent === 'big-tile-reached' && <div className="ff-drop-merge-surge-event"><b>all {tileLabel(game.removedTile)}s cleared</b><span>→</span><b>new tiles start at {tileLabel(game.removedTile * 2)}</b></div>}</section>
          <section className="ff-drop-merge-how"><small>How to play</small><p>Tap a column, or drag across the board and release, to drop immediately. Otherwise, the box descends through the top lane and auto-drops into the selected column.</p><p>Every touching group of matching numbers merges together: three 2s make 8, four 2s make 16. A drop into a full column ends the run.</p><span>Seven columns · timed drops · persistent best tile · 2 through 32 in the opening queue</span></section>
        </aside>
      </div> : <div className="ff-drop-merge-loading">{error ?? 'Building your columns…'}</div>}

      {error && <div className="ff-drop-merge-error" role="alert"><strong>{error}</strong><button type="button" onClick={newGame} disabled={busy}>Try again</button></div>}
    </main>
  </div>
}

function messageForError(reason: unknown): string { return reason instanceof DropMergeGatewayError ? reason.message : 'The Drop Merge table is unavailable. Start a new local run.' }
function wait(milliseconds: number): Promise<void> { return new Promise(resolve => window.setTimeout(resolve, milliseconds)) }
function fallingPosition(row: number, rows: number, duration: number): CSSProperties { return { '--fall-row': row, '--board-rows': rows, animationDuration: `${duration}ms` } as CSSProperties }
function previewStyle(game: DropMergeGameState, timerRemaining: number, isPaused: boolean): CSSProperties {
  const elapsed = timerRemaining > 0 ? Math.max(0, game.dropTimerMilliseconds - timerRemaining) : 0
  return { animationDelay: elapsed > 0 ? `-${elapsed}ms` : undefined, animationPlayState: isPaused ? 'paused' : 'running' }
}
function mergePosition(source: DropMergeMergeStep['sources'][number], target: DropMergeMergeStep['target']): CSSProperties {
  const columnShift = target.column - source.column
  const rowShift = target.row - source.row
  return {
    gridColumn: source.column + 1,
    gridRow: source.row + 1,
    '--merge-x': `calc(${columnShift * 100}% + ${columnShift * 0.34}rem)`,
    '--merge-y': `${rowShift * 100}%`,
  } as CSSProperties
}
function lowestEmptyRow(tiles: readonly number[], columns: number, column: number): number {
  for (let row = Math.floor(tiles.length / columns) - 1; row >= 0; row--) {
    if (tiles[(row * columns) + column] === 0) return row
  }
  return -1
}
function readBestTile(): number {
  if (typeof window === 'undefined') return 0
  try { const value = Number(window.localStorage.getItem(bestTileKey)); return Number.isSafeInteger(value) && value > 0 ? value : 0 } catch { return 0 }
}
