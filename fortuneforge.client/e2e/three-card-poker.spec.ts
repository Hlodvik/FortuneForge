import { expect, test, type Page } from '@playwright/test'
import type { ThreeCardPokerPlaySession, ThreeCardPokerSession } from '../src/games/cards/threeCardPoker/threeCardPokerApi'

for (const viewport of [
  { name: 'desktop', width: 1280, height: 720, action: 'play' },
  { name: 'mobile', width: 390, height: 844, action: 'fold' },
  { name: 'small-phone', width: 320, height: 667, action: 'fold' },
  { name: 'landscape', width: 844, height: 390, action: 'play' },
] as const) {
  test(`Three Card Poker queues, wagers and ${viewport.action}s in a ${viewport.name} viewport`, async ({ page }) => {
    await page.setViewportSize(viewport)
    const fixture = await mockTable(page)
    await page.goto('/cards/three-card-poker')
    await expect(page.getByRole('button', { name: 'Join live table' })).toBeVisible()
    await page.getByRole('button', { name: 'Join live table' }).click()
    await expect(page.getByRole('heading', { name: 'Finding table…' })).toBeVisible()
    await expect(page.getByLabel('Ante amount')).toBeVisible()
    await page.getByLabel('Ante amount').fill('5')
    await page.getByLabel('Pair Plus amount').fill('1')
    const wagerButton = page.getByRole('button', { name: 'Wager', exact: true })
    await expect(wagerButton).toBeInViewport()
    await expect(page.getByRole('button', { name: 'Leave table' })).toBeInViewport()
    await page.screenshot({ path: `../artifacts/three-card-poker-ui/${viewport.name}-betting.png` })
    await wagerButton.click()
    await expect(page.getByRole('button', { name: 'Play R5.00', exact: true })).toBeEnabled()
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(3)
    await expect(page.locator('.ff-playing-card--back')).toHaveCount(9)
    await page.screenshot({ path: `../artifacts/three-card-poker-ui/${viewport.name}-active.png` })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width)
    expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(viewport.height)
    await page.getByRole('button', { name: viewport.action === 'play' ? 'Play R5.00' : 'Fold', exact: true }).click()
    await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'settled')
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(12)
    expect(fixture.wagers).toEqual([{ ante: 5, pairPlus: 1, expectedVersion: 2 }])
    expect(fixture.actions).toEqual([{ type: viewport.action, expectedVersion: 3 }])
    await page.getByRole('button', { name: 'How to play Three Card Poker' }).click()
    await expect(page.getByRole('dialog')).toContainText('It remains eligible when you fold.')
    await page.getByRole('button', { name: 'Close how to play' }).click()
    await page.getByRole('button', { name: 'Leave table' }).click()
    await expect(page.getByRole('button', { name: 'Join live table' })).toBeVisible()
  })
}

async function mockTable(page: Page) {
  const date = new Date().toISOString()
  const deadline = new Date(Date.now() + 60_000).toISOString()
  const base = '/api/cards/three-card-poker/table'
  const fixture = { wagers: [] as unknown[], actions: [] as unknown[] }
  let balance = 100
  let session: ThreeCardPokerSession = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 }
  const table: ThreeCardPokerPlaySession = {
    contractVersion: 'cards.three-card-poker.table.v1', kind: 'table', version: 2,
    table: {
      tableId: 'table-1', phase: 'betting', round: 1, dealer: { cards: [], rank: null },
      seats: ['Ada', 'Mina', 'Leo'].map((displayName, seat) => ({ seatId: `seat-${seat}`, displayName, seat, status: seat === 0 ? 'awaiting-wager' : 'ready', ante: seat === 0 ? 0 : 5, pairPlus: 0, playWager: 0, totalWager: 0, payout: 0, anteBonusPayout: 0, pairPlusPayout: 0, hand: { cards: [], rank: null }, isCurrentPlayer: seat === 0 })),
      activeSeat: 0, legalActions: [], createdAtUtc: date, updatedAtUtc: date, actionDeadlineAtUtc: null,
      wagerDeadlineAtUtc: deadline, transition: 'human-wager', nextTransitionAtUtc: null,
      remainingActionMilliseconds: 0, remainingWagerMilliseconds: 60_000, remainingTransitionMilliseconds: 0,
    },
  }
  const cards = [{ hidden: false, rank: 'Q', suit: 'spades' }, { hidden: false, rank: '6', suit: 'hearts' }, { hidden: false, rank: '4', suit: 'diamonds' }] as const
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    const send = (json: unknown) => route.fulfill({ json })
    if (path === '/api/accounts/me') return send({ userId: 'table-player', playerName: 'Ada', email: 'ada@example.test', createdAtUtc: date, role: 'Player', balances: { slotsCredits: balance, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    if (path === `${base}/status`) return send({ available: true, minimumWager: .5, maximumWager: 100, wagerIncrement: .5, minimumStartOccupancy: 3, tableCapacity: 5, humanGraceSeconds: 5, actionDeadlineSeconds: 60, dealerRule: 'Queen high or better qualifies', pairPlusAllowed: true, anteBonusPaytable: 'Straight 1:1; trips 4:1; straight flush 5:1', pairPlusPaytable: 'Pair 1:1; flush 3:1; straight 6:1; trips 30:1; straight flush 40:1', pairPlusPaysOnFold: true })
    if (path === `${base}/queue` && method === 'POST') {
      session = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'queue', version: 1, ticketId: 'ticket-1', position: 1, joinedAtUtc: date, humanGraceEndsAtUtc: deadline, players: [] }
    } else if (path === `${base}/snapshot` && session.kind === 'queue') {
      session = table
    } else if (path.endsWith('/wagers')) {
      const request = route.request().postDataJSON() as { ante: number; pairPlus: number }
      fixture.wagers.push(request)
      balance -= request.ante + request.pairPlus
      session = { ...table, version: 3, table: { ...table.table, phase: 'active', transition: null, actionDeadlineAtUtc: deadline, wagerDeadlineAtUtc: null, legalActions: ['play', 'fold'], dealer: { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null }, seats: table.table.seats.map((seat) => ({ ...seat, status: 'playing', ante: 5, pairPlus: seat.isCurrentPlayer ? 1 : 0, totalWager: seat.isCurrentPlayer ? 6 : 5, hand: seat.isCurrentPlayer ? { cards, rank: 'high-card' } : { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null } })) } }
    } else if (path.endsWith('/actions') && session.kind === 'table') {
      const request = route.request().postDataJSON() as { type: string }
      fixture.actions.push(request)
      if (request.type === 'play') balance -= 5
      session = { ...session, version: 4, table: { ...session.table, phase: 'settled', transition: null, activeSeat: null, legalActions: [], dealer: { cards, rank: 'high-card' }, seats: session.table.seats.map((seat) => ({ ...seat, hand: { cards, rank: 'high-card' }, status: 'completed', outcome: request.type === 'play' ? 'push' : 'fold', lastAction: request.type, playWager: request.type === 'play' ? 5 : 0, totalWager: request.type === 'play' ? seat.totalWager + 5 : seat.totalWager })) } }
    } else if (path.endsWith('/leave')) {
      session = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 }
    }
    if (path.startsWith(base)) return send({ session, balanceCredits: balance })
    if (path === '/api/cards/history') return send([])
    if (path.endsWith('/session')) return send({ kind: 'idle' })
    return send({})
  })
  return fixture
}
