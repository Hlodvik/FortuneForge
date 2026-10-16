import { expect, test, type Page } from '@playwright/test'
import type { SolitaireMatchSession, SolitaireQueueSession, SolitaireSession } from '../src/games/cards/solitaire/solitaireTypes'

test('Solitaire waits for the server queue and opens the assigned match after the human grace period', async ({ page }) => {
  const fixture = await mockSolitaire(page)
  await page.goto('/cards/solitaire')
  await page.getByRole('button', { name: 'Competitive · R5', exact: true }).click()
  await expect(page.locator('.solitaire-queue')).toBeVisible()
  await expect(page.locator('.solitaire-seat-grid')).toContainText('Sam River')
  await expect(page.locator('.solitaire-seat-grid').getByText('Open seat', { exact: true })).toHaveCount(3)
  await expect(page.getByLabel('Klondike game board')).toBeVisible({ timeout: 8_000 })

  expect(fixture.joinedAt).not.toBeNull()
  expect(fixture.matchedAt! - fixture.joinedAt!).toBeGreaterThanOrEqual(5_000)
  expect(fixture.sessionReads).toBeGreaterThanOrEqual(5)
  await page.getByRole('button', { name: 'Submit game', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('Quinn Harbor')
  await expect(page.getByRole('dialog')).not.toContainText('Open seat')
})

test('Solitaire serializes queue reads and does not restore a canceled queue from an older response', async ({ page }) => {
  const fixture = await mockSolitaire(page)
  await page.goto('/cards/solitaire')
  await page.getByRole('button', { name: 'Competitive · R5', exact: true }).click()
  fixture.holdNextRead = true
  await expect.poll(() => fixture.heldRead, { timeout: 4_000 }).not.toBeNull()
  const readsWhileHeld = fixture.sessionReads
  await page.evaluate(() => {
    window.dispatchEvent(new Event('focus'))
    window.dispatchEvent(new Event('online'))
  })
  await page.waitForTimeout(1_500)
  expect(fixture.sessionReads).toBe(readsWhileHeld)

  await page.getByRole('button', { name: 'Leave queue', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Competitive · R5', exact: true })).toBeVisible()
  fixture.heldRead!()
  await page.waitForTimeout(1_500)
  await expect(page.locator('.solitaire-queue')).toHaveCount(0)
  expect(fixture.sessionReads).toBe(readsWhileHeld)
})

test('Solitaire does not replace an updated board with a read started before a move', async ({ page }) => {
  const fixture = await mockSolitaire(page, 'match')
  await page.goto('/cards/solitaire')
  await expect(page.getByRole('button', { name: 'Draw three cards, 1 remaining', exact: true })).toBeVisible()
  fixture.holdNextRead = true
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect.poll(() => fixture.heldRead).not.toBeNull()

  await page.getByRole('button', { name: 'Draw three cards, 1 remaining', exact: true }).click()
  await expect.poll(() => fixture.commandRequests).toBe(1)
  await expect(page.getByRole('button', { name: 'Recycle the waste pile', exact: true })).toBeVisible()
  fixture.heldRead!()
  await page.waitForTimeout(250)
  await expect(page.getByRole('button', { name: 'Recycle the waste pile', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Draw three cards, 1 remaining', exact: true })).toHaveCount(0)
})

async function mockSolitaire(page: Page, initialSession: 'idle' | 'match' = 'idle') {
  const now = new Date().toISOString()
  const queue: SolitaireQueueSession = {
    kind: 'queued', ticketId: 'queue-1', playerCount: 4, buyInCredits: 5,
    prizePoolCredits: 20, winnerPayoutCredits: 18, position: 1, joinedAtUtc: now,
    players: [{ playerId: 'seat-1', displayName: 'Sam River', seat: 1, status: 'queued', joinedAtUtc: now, isCurrentPlayer: true }],
  }
  const match: SolitaireMatchSession = {
    kind: 'match', matchId: 'a'.repeat(64), playerCount: 4, buyInCredits: 5,
    prizePoolCredits: 20, winnerPayoutCredits: 18, version: 1,
    startedAtUtc: now, deadlineAtUtc: new Date(Date.now() + 600_000).toISOString(),
    score: 0, moves: 0, remainingMilliseconds: 600_000, isPaused: false,
    pauseRemainingMilliseconds: 600_000, canUndo: false,
    game: { stock: [{ isFaceUp: false }], waste: [], foundations: [[], [], [], []], tableau: [[{ isFaceUp: true, id: 'hearts-7', suit: 'hearts', rank: 7 }], [], [], [], [], [], []], drawCount: 3, score: 0, moves: 0, message: 'Your move' },
    players: ['Sam River', 'Quinn Harbor', 'Morgan Vale', 'Riley Stone'].map((displayName, index) => ({ playerId: `seat-${index + 1}`, displayName, seat: index + 1, status: 'playing', joinedAtUtc: now, isCurrentPlayer: index === 0 })),
  }
  let session: SolitaireSession = initialSession === 'match' ? match : { kind: 'idle' }
  const fixture = { joinedAt: null as number | null, matchedAt: null as number | null, sessionReads: 0, commandRequests: 0, holdNextRead: false, heldRead: null as (() => void) | null }
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    const send = (json: unknown) => route.fulfill({ json })
    if (path === '/api/accounts/me') return send({ userId: 'solitaire-player', playerName: 'Sam River', email: 'sam@example.test', createdAtUtc: now, role: 'Player', balances: { slotsCredits: 100, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    if (path === '/api/solitaire/queue' && method === 'POST') {
      fixture.joinedAt = Date.now()
      session = queue
      return send({ session, balanceCredits: 95 })
    }
    if (path === '/api/solitaire/queue/queue-1' && method === 'DELETE') {
      session = { kind: 'idle' }
      return send({ session, balanceCredits: 100 })
    }
    if (path === '/api/solitaire/session') {
      fixture.sessionReads += 1
      const captured = session
      if (fixture.holdNextRead) {
        fixture.holdNextRead = false
        await new Promise<void>((resolve) => { fixture.heldRead = resolve })
        return send(captured).catch(() => undefined)
      }
      if (session.kind === 'queued' && fixture.joinedAt !== null && Date.now() - fixture.joinedAt >= 5_000) {
        fixture.matchedAt = Date.now()
        session = match
      }
      return send(session)
    }
    if (path.endsWith('/commands') && session.kind === 'match') {
      fixture.commandRequests += 1
      const command = route.request().postDataJSON() as { type: string }
      session = command.type === 'draw'
        ? { ...session, version: session.version + 1, moves: 1, canUndo: true, game: {
            ...session.game, stock: [], waste: [{ isFaceUp: true, id: 'hearts-2', suit: 'hearts', rank: 2 }], moves: 1,
          } }
        : { ...session, version: session.version + 1, players: session.players.map((player) => player.isCurrentPlayer ? { ...player, status: 'finished' } : player) }
      return send({ session, balanceCredits: 95 })
    }
    if (path === '/api/cards/history') return send([])
    return send({})
  })
  return fixture
}
