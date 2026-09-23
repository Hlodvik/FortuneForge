import { describe, expect, it } from 'vitest'
import { LocalSnakeGateway } from './localSnakeGateway'

describe('LocalSnakeGateway', () => {
  it('starts a deterministic board and advances a run', async () => {
    const gateway = new LocalSnakeGateway()
    const game = await gateway.startGame(42)
    const moved = await gateway.tick(game.gameId)

    expect(game.body).toHaveLength(3)
    expect(game.food).not.toBeNull()
    expect(moved.moves).toBe(1)
    expect(moved.body[0]).toEqual({ x: game.body[0]!.x + 1, y: game.body[0]!.y })
  })

  it('does not allow the snake to reverse into its own body', async () => {
    const gateway = new LocalSnakeGateway()
    const game = await gateway.startGame(7)
    const turned = await gateway.turn(game.gameId, 'left')

    expect(turned.direction).toBe('right')
    expect(turned.lastEvent).toBe('no-op')
  })
})
