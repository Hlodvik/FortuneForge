import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { commandBlackjackBotPractice, joinBlackjackBotPractice } from '../blackjack/botPracticeApi'
import { commandSolitaireBotPractice } from '../solitaire/botPracticeApi'
import { commandHoldemBotPractice } from '../texasHoldem/botPracticeApi'
import { PracticeModeNotice, PracticeQueuePanel } from './PracticeBotChrome'

describe('account-neutral bot-practice client contract', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('sends stable session and idempotency headers with the exact join body', async () => {
    const fetchMock = okFetch()
    vi.stubGlobal('fetch', fetchMock)

    await joinBlackjackBotPractice(4, 3, 'practice_join_00000001')

    const call = fetchMock.mock.calls[0]
    if (call === undefined) throw new Error('Expected one practice request.')
    const [path, init] = call
    if (init === undefined) throw new Error('Expected practice request options.')
    const headers = new Headers(init.headers)
    expect(path).toBe('/api/cards/blackjack/bot-practice/queue')
    expect(headers.get('X-Practice-Session-Id')?.length).toBeGreaterThanOrEqual(16)
    expect(headers.get('Idempotency-Key')).toBe('practice_join_00000001')
    expect(JSON.parse(String(init.body))).toEqual({
      playerCount: 4,
      difficulty: 3,
      idempotencyKey: 'practice_join_00000001',
    })
  })

  it('submits only typed Blackjack and Hold’em commands with authoritative versions', async () => {
    const fetchMock = okFetch()
    vi.stubGlobal('fetch', fetchMock)

    await commandBlackjackBotPractice('blackjack-match', 7, 'stand', 'practice_bj_0000000001')
    await commandHoldemBotPractice('holdem-match', 9, 'raise', 'practice_he_0000000001', 80)

    expect(body(fetchMock, 0)).toEqual({
      type: 'stand', expectedVersion: 7, idempotencyKey: 'practice_bj_0000000001',
    })
    expect(body(fetchMock, 1)).toEqual({
      type: 'raise', expectedVersion: 9, arguments: { raiseTo: '80' }, idempotencyKey: 'practice_he_0000000001',
    })
  })

  it('maps Solitaire moves to string arguments without client-owned result fields', async () => {
    const fetchMock = okFetch()
    vi.stubGlobal('fetch', fetchMock)

    await commandSolitaireBotPractice(
      'solitaire-match',
      12,
      { type: 'move', from: { zone: 'tableau', index: 3 }, startIndex: 5, to: { zone: 'foundation', index: 1 } },
      'practice_so_0000000001',
    )

    const request = body(fetchMock, 0)
    expect(request).toEqual({
      type: 'move',
      expectedVersion: 12,
      arguments: {
        fromZone: 'tableau', fromIndex: '3', startIndex: '5', toZone: 'foundation', toIndex: '1',
      },
      idempotencyKey: 'practice_so_0000000001',
    })
    expect(JSON.stringify(request)).not.toMatch(/score|elapsed|seed|payout|balance|identity/i)
  })

  it('discloses automation generally but never identifies individual automated seats', () => {
    const queue = {
      queueId: 'queue',
      game: 'blackjack',
      requiredPlayers: 2,
      seats: [
        { seatId: 'seat_73bd', displayName: 'NightOwl77', seat: 0, status: 'queued' },
        { seatId: 'seat_a19f', displayName: 'RiverAce21', seat: 1, status: 'queued' },
      ],
    }
    const markup = renderToStaticMarkup(createElement('div', null,
      createElement(PracticeModeNotice),
      createElement(PracticeQueuePanel, { queue }),
    ))

    expect(markup).toContain('Automated opponents may fill empty seats')
    expect(markup).toContain('NightOwl77')
    expect(markup).toContain('RiverAce21')
    expect(markup).not.toMatch(/4-star|automated seat/i)
    expect(markup).toContain('No account balance, wager ledger, payout, or house result')
  })
})

function okFetch() {
  return vi.fn(async (_path: string | URL | Request, _init?: RequestInit) => new Response(JSON.stringify({
    contractVersion: 'cards.bot.v2', kind: 'queue', queue: null, table: null,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function body(fetchMock: ReturnType<typeof okFetch>, index: number): Record<string, unknown> {
  const init = fetchMock.mock.calls[index]?.[1]
  if (init === undefined) throw new Error(`Expected practice request ${index}.`)
  return JSON.parse(String(init.body)) as Record<string, unknown>
}
