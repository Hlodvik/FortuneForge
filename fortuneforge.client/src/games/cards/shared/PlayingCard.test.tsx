/// <reference types="node" />

import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { PlayingCard } from './PlayingCard'
import type { CardSuit } from './cards'

describe('PlayingCard court faces', () => {
  it('uses the approved SVG symbols for all court cards, inheriting their suit ink and retaining corner indices', () => {
    const suits: readonly CardSuit[] = ['clubs', 'diamonds', 'hearts', 'spades']
    const ranks = [11, 12, 13] as const
    const names = { 11: 'jack', 12: 'queen', 13: 'king' } as const
    const artwork = new Map<number, Set<string>>()

    for (const suit of suits) {
      for (const rank of ranks) {
        const markup = renderToStaticMarkup(createElement(PlayingCard, {
          card: { id: `${suit}-${rank}`, suit, rank },
        }))

        expect(markup).toContain('ff-playing-card__center ff-playing-card__center--court')
        expect(markup.match(/ff-playing-card__center--court/g)).toHaveLength(1)
        expect(markup).toContain('class="ff-playing-card__court-art"')
        expect(markup).toContain(`data-court-symbol="${names[rank]}"`)
        expect(markup).toContain('fill="currentColor"')
        expect(markup).toContain(`ff-playing-card--${suit === 'hearts' || suit === 'diamonds' ? 'red' : 'black'}`)
        const center = markup.match(/<span class="ff-playing-card__center ff-playing-card__center--court"[^>]*>(.*?)<\/span>/)?.[1]
        expect(center).toContain('<svg ')
        expect(center).not.toMatch(/<img|<image|href=|src=/)
        expect(center).not.toContain('<b>')
        expect(center).not.toContain('<i>')
        expect(center).not.toMatch(/#[a-f\d]{3,8}/i)
        const rankArtwork = artwork.get(rank) ?? new Set<string>()
        rankArtwork.add(center!)
        artwork.set(rank, rankArtwork)
        expect(markup.match(/class="ff-playing-card__corner(?: ff-playing-card__corner--bottom)?"/g)).toHaveLength(2)
      }
    }
    expect([...artwork.values()].map(variants => variants.size)).toEqual([1, 1, 1])
    expect(new Set([...artwork.values()].flatMap(variants => [...variants])).size).toBe(3)
  })

  it('does not add an inset border or panel behind the court illustration', () => {
    const sharedStyles = readFileSync(new URL('./playingCards.css', import.meta.url), 'utf8')
    const courtStyles = sharedStyles.match(/\.ff-playing-card__center--court\s*\{([^}]+)\}/)?.[1]
    expect(courtStyles).toBeDefined()
    expect(courtStyles).not.toMatch(/border|background|box-shadow/)
    // Hi-Lo's generic svg rule must not shrink or outline shared court symbols.
    const symbolStyles = sharedStyles.match(/\.ff-playing-card \.ff-playing-card__court-art\s*\{([^}]+)\}/)?.[1]
    expect(symbolStyles).toContain('width: 100%;')
    expect(symbolStyles).toContain('height: 100%;')
    expect(symbolStyles).toContain('fill: currentColor;')
    expect(symbolStyles).toContain('stroke: none;')
  })

  it('keeps number-card centers out of the court layout', () => {
    const markup = renderToStaticMarkup(createElement(PlayingCard, {
      card: { id: 'diamonds-10', suit: 'diamonds', rank: 10 },
    }))

    expect(markup).not.toContain('ff-playing-card__center--court')
    expect(markup).not.toContain('<img')
    expect(markup).not.toContain('<svg')
    expect(markup).toContain('<i>♦</i>')
  })

  it('does not reveal or request court artwork when a card is face down', () => {
    const markup = renderToStaticMarkup(createElement(PlayingCard, {
      card: { id: 'hearts-13', suit: 'hearts', rank: 13 }, faceDown: true,
    }))
    expect(markup).toContain('aria-label="Face-down card"')
    expect(markup).toContain('ff-playing-card__back-frame')
    expect(markup).toContain('<b>FF</b>')
    expect(markup).not.toContain('<img')
    expect(markup).not.toContain('<svg')
    expect(markup).not.toContain('data-court-symbol')
  })
})
