import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { RouletteGame } from './RouletteGame'
import type { RouletteGateway } from './contracts'

const gateway: RouletteGateway = { getStatus: async () => ({ available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, startingBalance: 1000, mode: 'test' }), openRound: async () => { throw new Error() }, placeBet: async () => { throw new Error() }, removeBet: async () => { throw new Error() }, clearBets: async () => { throw new Error() }, spin: async () => { throw new Error() } }
describe('RouletteGame', () => { it('renders a playable single-zero table without duplicating host navigation', () => { const markup = renderToStaticMarkup(createElement(RouletteGame, { gateway })); expect(markup).toContain('Roulette'); expect(markup).toContain('Single-zero table'); expect(markup).not.toContain('Other games'); expect(markup).not.toContain('Fortune Forge home'); expect(markup).toContain('0'); expect(markup).toContain('Red'); expect(markup).toContain('Tips'); expect(markup).toContain('Place bets, then spin') }) })
