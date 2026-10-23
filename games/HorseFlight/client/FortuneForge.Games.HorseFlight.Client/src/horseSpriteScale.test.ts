import { describe, expect, it } from 'vitest'
import { horseSpriteScale, jumpSpriteGeometry, terminalSpriteGeometry } from './horseSpriteScale'

describe('horse sprite visual scale', () => {
  it('matches the standing rear and running poses by opaque height', () => {
    const rearVisibleHeight = horseSpriteScale.rear.height * 250 / 362
    const runVisibleHeight = horseSpriteScale.running.height * 277 / 300

    expect(Math.abs(rearVisibleHeight - runVisibleHeight)).toBeLessThan(1)
    expect(horseSpriteScale.running.width / horseSpriteScale.running.height).toBeCloseTo(444 / 300, 1)
  })

  it('renders reaction poses at the same source-pixel scale as running', () => {
    expect(horseSpriteScale.terminal.width / 444).toBeCloseTo(horseSpriteScale.running.height / 300, 2)
    expect(horseSpriteScale.terminal.height / 444).toBeCloseTo(horseSpriteScale.running.height / 300, 2)
    expect(horseSpriteScale.jump.width / 444).toBeCloseTo(horseSpriteScale.running.height / 300, 2)
  })

  it('keeps the crouching dash horse the same width without making it as tall as a gallop', () => {
    const dashVisibleWidth = horseSpriteScale.dash.width * 255 / 271
    const runVisibleWidth = horseSpriteScale.running.width * 381 / 444
    const dashVisibleHeight = horseSpriteScale.dash.height * 154 / 224
    const runVisibleHeight = horseSpriteScale.running.height * 277 / 300

    expect(Math.abs(dashVisibleWidth - runVisibleWidth)).toBeLessThan(5)
    expect(dashVisibleHeight).toBeLessThan(runVisibleHeight)
  })

  it('crops each replacement jump frame and anchors its hooves to game.horseY', () => {
    const opaqueBottoms = [411, 411, 411, 411, 320, 332, 358, 358]
    for (let frame = 0; frame < 8; frame += 1) {
      const geometry = jumpSpriteGeometry(frame)
      expect(geometry.sourceX + geometry.sourceWidth).toBeLessThanOrEqual(geometry.sheetWidth)
      expect(geometry.sourceY + geometry.sourceHeight).toBeLessThanOrEqual(geometry.sheetHeight)
      expect(opaqueBottoms[frame] + geometry.groundOffset).toBe(geometry.sourceHeight - 1)
    }
  })

  it.each(['crash', 'slip', 'trip', 'skid'] as const)('keeps every %s frame cropped and grounded', reaction => {
    for (let frame = 0; frame < 8; frame += 1) {
      const geometry = terminalSpriteGeometry(reaction, frame)
      const localBottom = ({
        crash: [436, 436, 436, 433, 355, 351, 351, 351],
        slip: [401, 407, 406, 407, 392, 394, 395, 399],
        trip: [437, 441, 441, 443, 348, 368, 364, 373],
        skid: [389, 437, 437, 437, 363, 363, 363, 372],
      } as const)[reaction][frame]

      expect(geometry.sourceX + geometry.sourceWidth).toBeLessThanOrEqual(geometry.sheetWidth)
      expect(geometry.sourceY + geometry.sourceHeight).toBeLessThanOrEqual(geometry.sheetHeight)
      expect(localBottom + geometry.groundOffset).toBe(geometry.sourceHeight - 1)
    }
  })
})
