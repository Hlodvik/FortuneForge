import { expect, test, type Page } from '@playwright/test'
import type { SolitaireHistoryItem, SolitaireMatchSession, SolitaireSession } from '../src/games/cards/solitaire/solitaireTypes'

test('Solitaire opens the board immediately and history shows vacant match seats', async ({ page }) => {
  const fixture = await mockSolitaire(page)
  await page.goto('/cards/solitaire')
  await page.getByRole('button', { name: 'Competitive · R5', exact: true }).click()

  await expect(page.getByLabel('Klondike game board')).toBeVisible()
  await expect(page.locator('.solitaire-queue')).toHaveCount(0)
  expect(fixture.joinRequests).toBe(1)
  await expect(page.getByRole('button', { name: 'Match history' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Submit game', exact: true }).click()
  await page.getByRole('button', { name: 'Return', exact: true }).click()
  await page.getByRole('button', { name: 'Match history' }).click()
  await expect(page.getByRole('dialog', { name: 'Match history' })
    .getByText('Waiting for players...', { exact: true })).toHaveCount(3)
})

test('A player can finish their run and start a new one while the older match waits', async ({ page }) => {
  const fixture = await mockSolitaire(page)
  await page.goto('/cards/solitaire')
  await page.getByRole('button', { name: 'Competitive · R5', exact: true }).click()
  await page.getByRole('button', { name: 'Submit game', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('Waiting for players...')
  await page.getByRole('button', { name: 'New game', exact: true }).click()
  await page.getByRole('button', { name: 'Start · R5' }).click()

  await expect(page.getByLabel('Klondike game board')).toBeVisible()
  expect(fixture.joinRequests).toBe(2)
  await expect(page.locator('.solitaire-queue')).toHaveCount(0)
})

test('An older paid reward stays reachable and claimable through match history pages', async ({ page }) => {
  const fixture = await mockSolitaire(page)
  const older: SolitaireHistoryItem = {
    ...fixture.history,
    matchId: 'c'.repeat(64),
    status: 'settled',
    startedAtUtc: new Date(Date.now() - 86_400_000).toISOString(),
    completedAtUtc: new Date(Date.now() - 82_800_000).toISOString(),
    placement: 1,
    payoutCredits: 18,
    netCredits: 13,
    canClaim: true,
    claimStatus: 'unclaimed',
  }
  await page.route((url) => url.pathname === '/api/solitaire/history-page', (route) => {
    const cursor = new URL(route.request().url()).searchParams.get('cursor')
    return route.fulfill({ json: cursor
      ? { items: [older], nextCursor: null }
      : { items: [fixture.history], nextCursor: 'f'.repeat(64) } })
  })
  await page.route((url) => url.pathname.endsWith('/claim'), (route) =>
    route.fulfill({ json: { session: { kind: 'idle' }, balanceCredits: 113 } }))

  await page.goto('/cards/solitaire')
  await page.getByRole('button', { name: 'Match history' }).click()
  await page.getByRole('button', { name: 'Older matches' }).click()
  await expect(page.getByRole('dialog', { name: 'Match history' }).locator('li').filter({
    hasText: 'R18.00 won',
  }).first()).toBeVisible()
  await page.getByRole('button', { name: 'Claim reward' }).click()
  await expect(page.getByRole('button', { name: 'Claim reward' })).toHaveCount(0)
})

test('An old session read cannot replace a board after a move', async ({ page }) => {
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
  await expect(page.getByRole('button', { name: 'Recycle the waste pile', exact: true })).toBeVisible()
})

async function mockSolitaire(page: Page, initialSession: 'idle' | 'match' = 'idle') {
  const now = new Date().toISOString()
  const firstId = 'a'.repeat(64)
  const openSeats = [2, 3, 4].map((seat) => ({
    playerId: `open-seat-${seat}`, displayName: 'Waiting for players...', seat,
    status: 'open' as const, joinedAtUtc: now, isCurrentPlayer: false,
  }))
  const firstPlayer = {
    playerId: 'seat-1', displayName: 'Sam River', seat: 1,
    status: 'playing' as const, joinedAtUtc: now, isCurrentPlayer: true,
  }
  const match: SolitaireMatchSession = {
    kind: 'match', matchId: firstId, playerCount: 4, buyInCredits: 5,
    prizePoolCredits: 5, winnerPayoutCredits: 4.5, version: 1,
    startedAtUtc: now, deadlineAtUtc: new Date(Date.now() + 600_000).toISOString(),
    score: 0, moves: 0, remainingMilliseconds: 600_000, isPaused: false,
    pauseRemainingMilliseconds: 600_000, canUndo: false,
    game: { stock: [{ isFaceUp: false }], waste: [], foundations: [[], [], [], []],
      tableau: [[{ isFaceUp: true, id: 'hearts-7', suit: 'hearts', rank: 7 }], [], [], [], [], [], []],
      drawCount: 3, score: 0, moves: 0, message: 'Your move' },
    players: [firstPlayer, ...openSeats],
  }
  const history: SolitaireHistoryItem = {
    matchId: firstId, playerCount: 4, buyInCredits: 5, prizePoolCredits: 5,
    status: 'playing', startedAtUtc: now, completedAtUtc: null,
    players: [firstPlayer, ...openSeats], placement: 0, score: 0,
    elapsedSeconds: 0, payoutCredits: 0, netCredits: 0,
    canClaim: false, claimStatus: null, opponents: [],
  }
  let session: SolitaireSession = initialSession === 'match' ? match : { kind: 'idle' }
  const fixture = {
    joinRequests: 0,
    commandRequests: 0,
    history,
    holdNextRead: false,
    heldRead: null as (() => void) | null,
  }
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    const send = (json: unknown) => route.fulfill({ json })
    if (path === '/api/accounts/me') return send({
      userId: 'solitaire-player', playerName: 'Sam River', email: 'sam@example.test',
      createdAtUtc: now, role: 'Player', balances: { slotsCredits: 100, freeGames: 0 },
      slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
    })
    if (path === '/api/solitaire/queue' && method === 'POST') {
      fixture.joinRequests += 1
      session = fixture.joinRequests === 1 ? match : {
        ...match, matchId: 'b'.repeat(64), startedAtUtc: new Date().toISOString(),
      }
      return send({ session, balanceCredits: 100 - 5 * fixture.joinRequests })
    }
    if (path === '/api/solitaire/session') {
      const captured = session
      if (fixture.holdNextRead) {
        fixture.holdNextRead = false
        await new Promise<void>((resolve) => { fixture.heldRead = resolve })
        return send(captured).catch(() => undefined)
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
        : { ...session, version: session.version + 1,
            players: session.players.map((player) => player.isCurrentPlayer
              ? { ...player, status: 'finished' as const } : player) }
      return send({ session, balanceCredits: 95 })
    }
    if (path.endsWith('/dismiss') && method === 'POST') {
      session = { kind: 'idle' }
      return send({ session, balanceCredits: 95 })
    }
    if (path === '/api/solitaire/history-page') return send({ items: [history], nextCursor: null })
    return send({})
  })
  return fixture
}
