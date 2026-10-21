// @vitest-environment jsdom
import { createRef } from 'react'
import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HeartsTrialCoachBubble, trialCoachArrow } from './HeartsTrialCoachBubble'

const rect = (left: number, top: number, width: number, height: number): DOMRect => ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON() { return {} } })
afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('Trial coach arrow grounding', () => {
  it('keeps contextual Why open across opponent changes but resets it when the action changes', async () => {
    const user = userEvent.setup(), boardRef = createRef<HTMLElement>(), passRef = createRef<HTMLButtonElement>(), cards = { current: new Map<string, HTMLButtonElement>() }
    const coach = (kind: 'waiting' | 'play', name: string) => ({ kind, title: name, message: 'One card each, clockwise.', why: 'The trick winner leads next.', suggestedCodes: [] })
    const surface = (kind: 'waiting' | 'play', name: string) => <section ref={boardRef}><HeartsTrialCoachBubble coach={coach(kind, name)} boardRef={boardRef} cardRefs={cards} passRef={passRef} layoutKey="same hand" /></section>
    const rendered = render(surface('waiting', 'Trial bot 1’s turn'))
    await user.click(screen.getByText('Why?'))
    expect(screen.getByText('Why?').parentElement?.hasAttribute('open')).toBe(true)
    rendered.rerender(surface('waiting', 'Trial bot 2’s turn'))
    expect(screen.getByText('Why?').parentElement?.hasAttribute('open')).toBe(true)
    rendered.rerender(surface('play', 'Your turn'))
    expect(screen.getByText('Why?').parentElement?.hasAttribute('open')).toBe(false)
  })
  it('targets the exposed rank strip in a narrow overlapping hand, not the covered center', () => {
    const board = rect(0, 80, 320, 460), bubble = rect(35, 290, 250, 100), card = rect(44, 460, 51, 73)
    const arrow = trialCoachArrow(board, bubble, card, 19)
    expect(arrow.end).toEqual({ x: 53.5, y: 383 })
    expect(arrow.end.x).toBeGreaterThan(card.left)
    expect(arrow.end.x).toBeLessThan(card.left + 19)
    expect(arrow.end.x).not.toBe(card.left + card.width / 2)
  })
  it('uses the current measured board offset for desktop and resized layouts', () => {
    const desktop = trialCoachArrow(rect(20, 100, 1200, 650), rect(500, 450, 300, 70), rect(640, 600, 78, 111), 52)
    const phone = trialCoachArrow(rect(4, 98, 312, 430), rect(29, 260, 262, 96), rect(158, 433, 51, 73), 18)
    expect(desktop.end).toEqual({ x: 646, y: 503 })
    expect(phone.end).toEqual({ x: 163, y: 338 })
    expect(phone.path).not.toBe(desktop.path)
  })
  it('re-measures arrows against moved cards on resize and shrunk hands', () => {
    let compact = true
    const bounds = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      if (this.dataset.testid === 'board') return rect(4, 98, compact ? 312 : 620, 430)
      if (this.classList.contains('ff-hearts-trial-coach')) return rect(compact ? 29 : 180, 260, 262, 90)
      if (this.dataset.card === 'first') return rect(compact ? 40 : 200, 433, 51, 73)
      if (this.dataset.card === 'second') return rect(compact ? 58 : 260, 433, 51, 73)
      return rect(0, 0, 0, 0)
    })
    const boardRef = createRef<HTMLElement>(), cards = { current: new Map<string, HTMLButtonElement>() }, passRef = createRef<HTMLButtonElement>()
    const coach = { kind: 'play' as const, title: 'Follow clubs', message: 'Try the marked card.', suggestedCodes: ['first'] }
    const surface = (withSecond: boolean) => <section ref={boardRef} data-testid="board"><button data-card="first" ref={element => { if (element) cards.current.set('first', element); else cards.current.delete('first') }}>First card</button>{withSecond && <button data-card="second" ref={element => { if (element) cards.current.set('second', element); else cards.current.delete('second') }}>Second card</button>}<HeartsTrialCoachBubble coach={coach} boardRef={boardRef} cardRefs={cards} passRef={passRef} layoutKey={withSecond ? 'two cards' : 'one card'} /></section>
    const rendered = render(surface(true))
    const arrow = () => rendered.container.querySelector('.ff-hearts-coach-arrows>path')!
    expect(Number(arrow().getAttribute('data-target-x'))).toBe(45)
    compact = false
    act(() => window.dispatchEvent(new Event('resize')))
    expect(Number(arrow().getAttribute('data-target-x'))).toBe(221.5)
    compact = true
    rendered.rerender(surface(false))
    expect(Number(arrow().getAttribute('data-target-x'))).toBe(61.5)
    expect(bounds).toHaveBeenCalled()
  })
  it('points the confirmation hint at the Pass cards control and emits no card arrows while waiting', () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      if (this.dataset.testid === 'board') return rect(4, 98, 312, 430)
      if (this.classList.contains('ff-hearts-trial-coach')) return rect(29, 260, 262, 70)
      if (this.dataset.testid === 'pass') return rect(120, 360, 110, 40)
      return rect(0, 0, 0, 0)
    })
    const boardRef = createRef<HTMLElement>(), passRef = createRef<HTMLButtonElement>(), cards = { current: new Map<string, HTMLButtonElement>() }
    const surface = (kind: 'confirm-pass' | 'waiting') => <section ref={boardRef} data-testid="board"><button ref={passRef} data-testid="pass">Pass cards</button><HeartsTrialCoachBubble coach={{ kind, title: kind === 'waiting' ? 'Watch the other players' : 'Ready to pass', message: 'Next action.', suggestedCodes: [] }} boardRef={boardRef} cardRefs={cards} passRef={passRef} layoutKey="same hand" /></section>
    const rendered = render(surface('confirm-pass'))
    expect(rendered.container.querySelector('.ff-hearts-coach-arrows>path')?.getAttribute('data-target-x')).toBe('171')
    rendered.rerender(surface('waiting'))
    expect(screen.getByText('Watch the other players')).toBeTruthy()
    expect(rendered.container.querySelector('.ff-hearts-coach-arrows')).toBeNull()
  })
})
