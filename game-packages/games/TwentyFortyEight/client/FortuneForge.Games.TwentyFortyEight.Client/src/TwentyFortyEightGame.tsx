import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react'
import { TwentyFortyEightGatewayError } from './httpTwentyFortyEightGateway'
import type { TwentyFortyEightDirection, TwentyFortyEightGameState, TwentyFortyEightGateway } from './contracts'
import { directionLabel, planTileMotion, tileClass, tileLabel, type TileMotion } from './twentyFortyEightHelpers'

export type TwentyFortyEightGameProps = Readonly<{ gateway: TwentyFortyEightGateway; backHref?: string; playerName?: string; tableLabel?: string }>

const directions: readonly TwentyFortyEightDirection[] = ['up', 'left', 'down', 'right']
const tileMotionDuration = 180
const bestScoreKey = 'fortuneforge:2048:best-score'
const swipeThreshold = 28

export function TwentyFortyEightGame({ gateway, backHref = '/', playerName = 'Player', tableLabel = 'Local free play' }: TwentyFortyEightGameProps) {
  const [game, setGame] = useState<TwentyFortyEightGameState | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tileMotion, setTileMotion] = useState<readonly TileMotion[]>([])
  const [revealingTiles, setRevealingTiles] = useState<readonly number[]>([])
  const [bestScore, setBestScore] = useState(readBestScore)
  const swipeStart = useRef<{ x: number; y: number; pointerId: number } | null>(null)

  const run = useCallback(async (action: () => Promise<TwentyFortyEightGameState>) => {
    if (busy) return
    setBusy(true)
    setError(null)
    setTileMotion([])
    setRevealingTiles([])
    try { setGame(await action()) } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }, [busy])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(status => status.available ? gateway.startGame(undefined, controller.signal) : Promise.reject(new Error('The 2048 service is unavailable.')))
      .then(setGame)
      .catch((reason: unknown) => { if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason)) })
    return () => controller.abort()
  }, [gateway])

  useEffect(() => {
    if (!game || game.score <= bestScore) return
    setBestScore(game.score)
    try { window.localStorage.setItem(bestScoreKey, String(game.score)) } catch { /* private storage may be unavailable */ }
  }, [bestScore, game])

  const move = useCallback((direction: TwentyFortyEightDirection) => {
    if (!game || busy) return
    void (async () => {
      setBusy(true)
      setError(null)
      setTileMotion([])
      setRevealingTiles([])
      try {
        const nextGame = await gateway.move(game.gameId, direction)
        const motion = nextGame.lastEvent === 'no-move' ? [] : planTileMotion(game.tiles, game.size, direction)
        setGame(nextGame)
        setTileMotion(motion)
        setRevealingTiles([...new Set(motion.map(tile => tile.to))])
        if (motion.length > 0) await wait(tileMotionDuration)
      } catch (reason) {
        setError(messageForError(reason))
      } finally {
        setTileMotion([])
        setRevealingTiles([])
        setBusy(false)
      }
    })()
  }, [busy, game, gateway])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const direction = directionForKey(event.key)
      if (!direction || !game || busy) return
      event.preventDefault()
      move(direction)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [busy, game, move])

  const newGame = () => { void run(() => game ? gateway.reset(game.gameId) : gateway.startGame()) }
  const undo = () => { if (game) void run(() => gateway.undo(game.gameId)) }
  const continueGame = () => { if (game) void run(() => gateway.continueGame(game.gameId)) }
  const beginSwipe = (event: ReactPointerEvent<HTMLElement>) => {
    swipeStart.current = { x: event.clientX, y: event.clientY, pointerId: event.pointerId }
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }
  const finishSwipe = (event: ReactPointerEvent<HTMLElement>) => {
    const start = swipeStart.current
    swipeStart.current = null
    if (!start || start.pointerId !== event.pointerId) return
    const horizontal = event.clientX - start.x
    const vertical = event.clientY - start.y
    if (Math.max(Math.abs(horizontal), Math.abs(vertical)) < swipeThreshold) return
    move(Math.abs(horizontal) > Math.abs(vertical) ? (horizontal > 0 ? 'right' : 'left') : (vertical > 0 ? 'down' : 'up'))
  }

  return <div className="ff-2048-page">
    <header className="ff-2048-header"><a className="ff-2048-brand" href={backHref} aria-label="Fortune Forge home"><span aria-hidden="true">✦</span><strong>Fortune Forge</strong></a><a className="ff-2048-games" href={backHref}>Other games</a><div className="ff-2048-account"><strong>{playerName}</strong><span>{tableLabel}</span></div></header>
    <main className="ff-2048-main">
      <section className="ff-2048-title"><div><small>Number combo game</small><h1>2048</h1><p>Slide matching tiles together until you reach the golden 2048.</p></div><div className="ff-2048-actions"><button type="button" onClick={newGame} disabled={busy}>{busy ? 'Working…' : 'New game'}</button><button type="button" onClick={undo} disabled={busy || !game?.canUndo}>Undo</button></div></section>
      {game ? <div className="ff-2048-gameplay">
        <section className="ff-2048-stats" aria-live="polite"><div><small>Score</small><strong>{game.score.toLocaleString()}</strong></div><div><small>Best score</small><strong>{bestScore.toLocaleString()}</strong></div><div><small>Best tile</small><strong>{game.highestTile.toLocaleString()}</strong></div><div><small>Moves</small><strong>{game.moves}</strong></div></section>
        <section className="ff-2048-board-wrap" aria-label="2048 board. Swipe to move tiles." onPointerDown={beginSwipe} onPointerUp={finishSwipe} onPointerCancel={() => { swipeStart.current = null }}><div className="ff-2048-board" style={{ '--board-size': game.size } as CSSProperties}>{Array.from({ length: game.size * game.size }, (_, index) => <div className="ff-2048-cell" key={index} />)}<div className="ff-2048-tile-layer">{game.tiles.map((value, index) => value ? <div className={`ff-2048-tile ${tileClass(value)}${revealingTiles.includes(index) ? ' ff-2048-tile--revealing' : ''}`} key={index} style={tilePosition(index, game.size)} aria-label={`Tile ${value}`}>{tileLabel(value)}</div> : null)}{tileMotion.map((tile, index) => <div className={`ff-2048-tile ${tileClass(tile.value)} ff-2048-tile--moving`} key={`${tile.from}-${tile.to}-${index}`} style={motionPosition(tile, game.size)} aria-hidden="true">{tileLabel(tile.value)}</div>)}</div></div>{game.phase !== 'playing' && <div className="ff-2048-overlay"><small>{game.phase === 'won' ? 'You made it' : 'Board locked'}</small><strong>{game.phase === 'won' ? '2048 reached!' : 'No moves remain'}</strong><span>{game.phase === 'won' ? 'You won. Continue beyond 2048 or begin a fresh board.' : 'Try a new board and find a different path.'}</span><div>{game.phase === 'won' && <button type="button" onClick={continueGame} disabled={busy}>Keep playing</button>}<button type="button" onClick={newGame} disabled={busy}>New game</button>{game.canUndo && <button type="button" onClick={undo} disabled={busy}>Undo</button>}</div></div>}</section>
        <p className="ff-2048-message" aria-live="polite">{game.message}</p>
        <section className="ff-2048-controls" aria-label="Move controls">{directions.map(direction => <button type="button" key={direction} onClick={() => move(direction)} disabled={busy || game.phase !== 'playing'} aria-label={`Move ${directionLabel(direction)}`}>{direction === 'up' ? '↑' : direction === 'right' ? '→' : direction === 'down' ? '↓' : '←'}<span>{directionLabel(direction)}</span></button>)}</section>
        <p className="ff-2048-help">Swipe the board, use keyboard arrows, or tap the controls. Undo restores exactly your previous legal move.</p>
      </div> : <div className="ff-2048-loading">{error ?? 'Building your board…'}</div>}
      {error && <div className="ff-2048-error" role="alert"><strong>{error}</strong><button type="button" onClick={newGame} disabled={busy}>Try again</button></div>}
    </main>
  </div>
}

function directionForKey(key: string): TwentyFortyEightDirection | null { return key === 'ArrowUp' ? 'up' : key === 'ArrowRight' ? 'right' : key === 'ArrowDown' ? 'down' : key === 'ArrowLeft' ? 'left' : null }
function messageForError(reason: unknown): string { return reason instanceof TwentyFortyEightGatewayError ? reason.message : 'The 2048 table is unavailable. Start a new local game.' }
function wait(milliseconds: number): Promise<void> { return new Promise(resolve => window.setTimeout(resolve, milliseconds)) }
function readBestScore(): number {
  if (typeof window === 'undefined') return 0
  try { const value = Number(window.localStorage.getItem(bestScoreKey)); return Number.isSafeInteger(value) && value > 0 ? value : 0 } catch { return 0 }
}
function tilePosition(index: number, size: number): CSSProperties { return { gridColumnStart: (index % size) + 1, gridRowStart: Math.floor(index / size) + 1 } }
function motionPosition(tile: TileMotion, size: number): CSSProperties {
  const fromColumn = tile.from % size
  const fromRow = Math.floor(tile.from / size)
  const targetColumn = tile.to % size
  const targetRow = Math.floor(tile.to / size)
  return { ...tilePosition(tile.to, size), '--motion-x': gridOffset(fromColumn - targetColumn), '--motion-y': gridOffset(fromRow - targetRow) } as CSSProperties
}
function gridOffset(distance: number): string {
  if (distance === 0) return '0'
  const sign = distance < 0 ? '-' : '+'
  const gaps = Array.from({ length: Math.abs(distance) }, () => 'var(--board-gap)').join(` ${sign} `)
  return `calc(${distance * 100}% ${sign} ${gaps})`
}
