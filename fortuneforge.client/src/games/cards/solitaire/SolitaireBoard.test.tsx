// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SolitaireBoard } from './SolitaireBoard'
import type { SolitaireGame } from './solitaireTypes'

const ten = { id: 'ten-spades', suit: 'spades', rank: 10, isFaceUp: true } as const
const nine = { id: 'nine-hearts', suit: 'hearts', rank: 9, isFaceUp: true } as const
const jack = { id: 'jack-diamonds', suit: 'diamonds', rank: 11, isFaceUp: true } as const

let host: HTMLDivElement
let root: Root | null

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(async () => {
  if (root !== null) await act(async () => root!.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

describe('Solitaire card clicks', () => {
  it('moves a clicked black ten and its face-up run onto an available red jack', async () => {
    const onCommand = vi.fn()
    await act(async () => root!.render(<SolitaireBoard game={game([[ten, nine], [jack]])} busy={false} onCommand={onCommand} />))

    const card = host.querySelector<HTMLButtonElement>('[aria-label="Move or select Ten of spades"]')
    expect(card).not.toBeNull()
    await act(async () => card!.click())

    expect(onCommand).toHaveBeenCalledTimes(1)
    expect(onCommand).toHaveBeenCalledWith({
      type: 'move',
      from: { zone: 'tableau', index: 0 },
      startIndex: 0,
      to: { zone: 'tableau', index: 1 },
    })
  })

  it('keeps a card selected when no automatic destination exists', async () => {
    const onCommand = vi.fn()
    await act(async () => root!.render(<SolitaireBoard game={game([[ten]])} busy={false} onCommand={onCommand} />))

    const card = host.querySelector<HTMLButtonElement>('[aria-label="Move or select Ten of spades"]')
    await act(async () => card!.click())

    expect(onCommand).not.toHaveBeenCalled()
    expect(card?.getAttribute('aria-pressed')).toBe('true')
    expect(host.textContent).not.toContain('Selected Ten of spades')
    expect([...host.querySelectorAll<HTMLButtonElement>('[aria-label^="Move selected card to foundation"]')]
      .every((foundation) => foundation.disabled)).toBe(true)
    expect([...host.querySelectorAll<HTMLButtonElement>('[aria-label^="Move selected cards to empty tableau"]')]
      .every((column) => column.disabled)).toBe(true)
  })

  it('responds to a hint request from the board toolbar', async () => {
    const currentGame = game([[ten], [jack]])
    const onCommand = vi.fn()
    await act(async () => root!.render(<SolitaireBoard game={currentGame} busy={false} hintRequestId={0} onCommand={onCommand} />))
    expect(host.querySelectorAll('.is-hinted')).toHaveLength(0)

    await act(async () => root!.render(<SolitaireBoard game={currentGame} busy={false} hintRequestId={1} onCommand={onCommand} />))
    expect(host.querySelectorAll('.is-hinted').length).toBeGreaterThan(0)
    expect(onCommand).not.toHaveBeenCalled()
  })
})

function game(tableau: SolitaireGame['tableau']): SolitaireGame {
  return {
    stock: [], waste: [], foundations: [[], [], [], []],
    tableau: [...tableau, ...Array.from({ length: 7 - tableau.length }, () => [])],
    drawCount: 3, score: 0, moves: 0, message: 'Your move',
  }
}
