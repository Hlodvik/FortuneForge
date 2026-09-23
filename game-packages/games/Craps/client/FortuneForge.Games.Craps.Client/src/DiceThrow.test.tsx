import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { DiceThrow, landingLayout } from './DiceThrow'

describe('DiceThrow', () => {
  it('gives four dice distinct trajectories, positions, and stopping times', () => {
    const markup = renderToStaticMarkup(createElement(DiceThrow, {
      values: [1, 2, 3, 4],
      rollKey: 7,
    }))

    expect(markup).toContain('aria-label="Dice showing 1, 2, 3, 4"')
    expect(markup).toContain('ff-die-sprite-frame--0')
    expect(markup).toContain('ff-die-sprite-frame--1')
    expect(markup).toContain('ff-die-sprite-frame--2')
    expect(markup).toContain('ff-die-sprite-frame--3')
    expect(markup).toContain('--ff-die-duration:456ms')
    expect(markup).toContain('--ff-die-duration:533ms')
    expect(markup).toContain('--ff-die-duration:489ms')
    expect(markup).toContain('--ff-die-duration:600ms')
    expect(new Set(landingLayout(4).map(({ x, y }) => `${x},${y}`)).size).toBe(4)
  })

  it('accepts caller-supplied landing positions while the values set final faces', () => {
    const markup = renderToStaticMarkup(createElement(DiceThrow, {
      values: [6, 3],
      landingPositions: [{ x: 12, y: 24, rotation: 5 }, { x: 88, y: 76, rotation: -9 }],
    }))

    expect(markup).toContain('--ff-die-landing-x:12%')
    expect(markup).toContain('--ff-die-landing-y:24%')
    expect(markup).toContain('data-face="6"')
    expect(markup).toContain('data-face="3"')
    expect(markup).toContain('--ff-die-face-position:100%')
    expect(markup).toContain('--ff-die-face-position:40%')
  })

  it('cycles a two-die Craps roll through all four trajectories', () => {
    const firstRoll = renderToStaticMarkup(createElement(DiceThrow, { values: [2, 5], rollKey: 1 }))
    const secondRoll = renderToStaticMarkup(createElement(DiceThrow, { values: [4, 3], rollKey: 2 }))

    expect(firstRoll).toContain('data-trajectory="1"')
    expect(firstRoll).toContain('data-trajectory="2"')
    expect(secondRoll).toContain('data-trajectory="2"')
    expect(secondRoll).toContain('data-trajectory="3"')
    expect(firstRoll).not.toBe(secondRoll)
  })
})
