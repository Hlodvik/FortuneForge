import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { DiceThrow, landingLayout } from './DiceThrow'

describe('DiceThrow', () => {
  it('gives four dice distinct trajectories, positions, and stopping times', () => {
    const markup = renderToStaticMarkup(createElement(DiceThrow, { values: [1, 2, 3, 4], rollKey: 7 }))
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

  it('lays out five- and six-die hands without sharing landing positions', () => {
    for (const count of [5, 6]) {
      const landings = landingLayout(count)
      expect(landings).toHaveLength(count)
      expect(new Set(landings.map(({ x, y }) => `${x},${y}`)).size).toBe(count)
    }
  })
})
