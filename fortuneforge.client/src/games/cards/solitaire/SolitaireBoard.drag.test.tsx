// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { playCardAudio } from '../shared/cardAudio'
import { SolitaireBoard } from './SolitaireBoard'
import type { SolitaireGame } from './solitaireTypes'

vi.mock('../shared/cardAudio', () => ({ playCardAudio: vi.fn() }))
const ten = { id: 'ten-spades', suit: 'spades', rank: 10, isFaceUp: true } as const
const nine = { id: 'nine-hearts', suit: 'hearts', rank: 9, isFaceUp: true } as const
const jack = { id: 'jack-diamonds', suit: 'diamonds', rank: 11, isFaceUp: true } as const
const current: SolitaireGame = { stock: [], waste: [], foundations: [[], [], [], []], tableau: [[ten, nine], [jack], [], [], [], [], []], drawCount: 3, score: 0, moves: 0, message: 'Your move' }
let host: HTMLDivElement, root: Root, captured: Set<number>
const onCommand = vi.fn()
const source = () => host.querySelector<HTMLButtonElement>('[aria-label="Move or select Ten of spades"]')!
const destination = () => host.querySelector<HTMLButtonElement>('[aria-label="Move or select Jack of diamonds"]')!
const board = () => host.querySelector<HTMLElement>('.solitaire-board')!

async function renderBoard(busy = false, game = current) {
  await act(async () => root.render(<SolitaireBoard game={game} busy={busy} onCommand={onCommand} />))
  for (const card of host.querySelectorAll<HTMLButtonElement>('[data-solitaire-drag-source]')) {
    card.setPointerCapture = vi.fn(id => { captured.add(id) })
    card.hasPointerCapture = vi.fn(id => captured.has(id))
    card.releasePointerCapture = vi.fn(id => { captured.delete(id) })
  }
  const target = host.querySelector<HTMLElement>('[data-solitaire-drop-zone="tableau"][data-solitaire-drop-index="1"]')!
  target.getBoundingClientRect = () => new DOMRect(100, 100, 80, 120)
}
async function pointer(type: string, target = source(), id = 1, primary = true, x = 10, y = 10, button = 0) {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, button, clientX: x, clientY: y })
  Object.defineProperties(event, { pointerId: { value: id }, isPrimary: { value: primary } })
  await act(async () => target.dispatchEvent(event))
}
async function drag() { await pointer('pointerdown'); await pointer('pointermove', source(), 1, true, 120, 120) }
function expectCancelled() {
  expect(host.querySelector('.is-dragging')).toBeNull()
  expect(board().style.getPropertyValue('--solitaire-drag-x')).toBe('')
  expect(onCommand).not.toHaveBeenCalled(); expect(playCardAudio).not.toHaveBeenCalled()
}
beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  Object.defineProperty(document, 'hidden', { configurable: true, value: false })
  captured = new Set(); host = document.createElement('div'); document.body.append(host); root = createRoot(host)
  await renderBoard()
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.clearAllMocks(); vi.unstubAllGlobals() })

describe('Solitaire drag ownership and cancellation', () => {
  it('keeps the current legal move command, card rendering and sound for a primary drag', async () => {
    expect(source().draggable).toBe(false)
    await drag(); expect(host.querySelector('.is-dragging')).toBeTruthy()
    await pointer('pointerup', source(), 1, true, 120, 120)
    expect(onCommand).toHaveBeenCalledExactlyOnceWith({ type: 'move', from: { zone: 'tableau', index: 0 }, startIndex: 0, to: { zone: 'tableau', index: 1 } })
    expect(playCardAudio).toHaveBeenCalledExactlyOnceWith('move'); expect(captured.size).toBe(0)
  })
  it.each([[false, 0], [true, 2]] as const)('rejects a non-primary or secondary-button start (%s,%i)', async (primary, button) => {
    await pointer('pointerdown', source(), 1, primary, 10, 10, button)
    await pointer('pointermove', source(), 1, primary, 120, 120, button)
    await pointer('pointerup', source(), 1, primary, 120, 120, button)
    expectCancelled(); expect(source().setPointerCapture).not.toHaveBeenCalled()
  })
  it('does not let another pointer replace or cancel the first drag owner', async () => {
    await pointer('pointerdown')
    await pointer('pointerdown', destination(), 2, true)
    await pointer('pointermove', destination(), 2, true, 150, 150)
    await pointer('pointercancel', destination(), 2)
    expect(destination().setPointerCapture).not.toHaveBeenCalled()
    await pointer('pointermove', source(), 1, true, 120, 120)
    expect(host.querySelector('.is-dragging')).toBeTruthy()
    await pointer('pointerup', source(), 1, true, 120, 120)
    expect(onCommand).toHaveBeenCalledExactlyOnceWith({ type: 'move', from: { zone: 'tableau', index: 0 }, startIndex: 0, to: { zone: 'tableau', index: 1 } })
  })
  it('clears a preview after lost capture and ignores the following pointerup', async () => {
    await drag(); captured.delete(1); await pointer('lostpointercapture')
    await pointer('pointerup', source(), 1, true, 120, 120); expectCancelled()
  })
  it('does not turn a cancelled drag into an automatic click move on release', async () => {
    await drag(); await act(async () => window.dispatchEvent(new Event('blur')))
    const up = new MouseEvent('pointerup', { bubbles: true, cancelable: true, button: 0, clientX: 120, clientY: 120 })
    Object.defineProperties(up, { pointerId: { value: 1 }, isPrimary: { value: true } })
    await act(async () => { source().dispatchEvent(up); source().click() })
    expectCancelled()
    await pointer('pointerdown')
    await act(async () => source().click())
    expect(onCommand).toHaveBeenCalledTimes(1)
  })
  it('clears a preview and owned capture on blur', async () => {
    await drag(); await act(async () => window.dispatchEvent(new Event('blur')))
    await pointer('pointerup', source(), 1, true, 120, 120); expectCancelled(); expect(captured.size).toBe(0)
  })
  it('cancels on hidden visibility without cancelling an unrelated visible event', async () => {
    await drag(); await act(async () => document.dispatchEvent(new Event('visibilitychange')))
    expect(host.querySelector('.is-dragging')).toBeTruthy()
    Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    await act(async () => document.dispatchEvent(new Event('visibilitychange')))
    await pointer('pointerup', source(), 1, true, 120, 120); expectCancelled(); expect(captured.size).toBe(0)
  })
  it('does not queue a stale drop after the board becomes busy', async () => {
    await drag(); await renderBoard(true)
    await pointer('pointerup', source(), 1, true, 120, 120); expectCancelled()
    await renderBoard(false); await drag(); await pointer('pointerup', source(), 1, true, 120, 120)
    expect(onCommand).toHaveBeenCalledTimes(1)
  })
  it('cancels an old source when the authoritative game changes', async () => {
    await drag(); await renderBoard(false, { ...current, score: 1 })
    await pointer('pointerup', source(), 1, true, 120, 120); expectCancelled()
  })
})
