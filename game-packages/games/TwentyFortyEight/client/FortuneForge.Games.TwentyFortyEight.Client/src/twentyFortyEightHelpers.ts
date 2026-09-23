import type { TwentyFortyEightDirection } from './contracts'

export function tileClass(value: number): string { return value === 0 ? 'tile-empty' : value <= 2048 ? `tile-${value}` : 'tile-super' }
export function tileLabel(value: number): string { return value === 0 ? '' : String(value) }
export function directionLabel(direction: TwentyFortyEightDirection): string { return direction[0].toUpperCase() + direction.slice(1) }

export type TileMotion = Readonly<{ from: number; to: number; value: number; merged: boolean }>

/** Maps the movement that a legal 2048 move applies before the server adds its new tile. */
export function planTileMotion(tiles: readonly number[], size: number, direction: TwentyFortyEightDirection): readonly TileMotion[] {
  const motions: TileMotion[] = []

  for (let line = 0; line < size; line += 1) {
    const positions = positionsForLine(size, direction, line)
    const occupied = positions.filter(position => tiles[position] > 0)
    let targetOffset = 0

    for (let sourceOffset = 0; sourceOffset < occupied.length;) {
      const source = occupied[sourceOffset]
      const nextSource = occupied[sourceOffset + 1]
      const target = positions[targetOffset]
      const merging = nextSource !== undefined && tiles[source] === tiles[nextSource]

      motions.push({ from: source, to: target, value: tiles[source], merged: merging })
      if (merging) {
        motions.push({ from: nextSource, to: target, value: tiles[nextSource], merged: true })
        sourceOffset += 2
      } else {
        sourceOffset += 1
      }
      targetOffset += 1
    }
  }

  return motions.filter(motion => motion.merged || motion.from !== motion.to)
}

function positionsForLine(size: number, direction: TwentyFortyEightDirection, line: number): number[] {
  const positions: number[] = []
  for (let offset = 0; offset < size; offset += 1) {
    if (direction === 'left') positions.push(line * size + offset)
    if (direction === 'right') positions.push(line * size + (size - 1 - offset))
    if (direction === 'up') positions.push(offset * size + line)
    if (direction === 'down') positions.push((size - 1 - offset) * size + line)
  }
  return positions
}
