// The approved rear atlas supplies the horse's standing-size reference. Its
// first frame has 250 opaque vertical pixels in a 362-pixel cell rendered at
// 126 world units. Match the visible horse, rather than the transparent cell.
const rearReference = {
  cellHeight: 362,
  opaqueHeight: 250,
  renderedSize: 126,
} as const

const referenceVisibleHeight = Math.ceil(rearReference.renderedSize * rearReference.opaqueHeight / rearReference.cellHeight)

function scaleCellToVisibleHeight(cellWidth: number, cellHeight: number, opaqueHeight: number) {
  const scale = referenceVisibleHeight / opaqueHeight
  return {
    width: Math.round(cellWidth * scale),
    height: Math.round(cellHeight * scale),
  }
}

const running = scaleCellToVisibleHeight(444, 300, 277)
const terminalCellSize = Math.round(444 * running.height / 300)

export const horseSpriteScale = {
  rear: { width: rearReference.renderedSize, height: rearReference.renderedSize },
  // Median opaque height of the eight running frames is approximately 277 px.
  // The source crop is 444×300; its horizontal stretch is a galloping pose,
  // not a larger horse.
  running,
  // The lowered dash pose is intentionally shorter, but its ~124-unit
  // visible width is within a few units of the gallop's ~121-unit width.
  dash: { width: 132, height: 112 },
  // The terminal atlases use the same source-pixel scale as the new run atlas;
  // a fallen pose becomes lower naturally instead of shrinking the horse.
  terminal: { width: terminalCellSize, height: terminalCellSize },
  jump: { width: terminalCellSize, height: terminalCellSize },
  referenceVisibleHeight,
} as const

/** Every ground-collision pose finishes with its visible bottom on the struck platform. */
export function terminalGroundBaseline(startY: number, platformY: number, frame: number, progress?: readonly number[]): number {
  const index = Math.min(7, Math.max(0, Math.floor(frame)))
  return startY + (platformY - startY) * (progress?.[index] ?? index / 7)
}

const poseAtlas = {
  sheetWidth: 1774,
  sheetHeight: 887,
  columnStarts: [0, 444, 887, 1331],
  columnWidths: [444, 443, 444, 443],
  rowStarts: [0, 444],
  rowHeights: [444, 443],
} as const

// Inclusive opaque-pixel bottoms, measured inside each frame at alpha >= 32.
// Anchoring these to the ground keeps the hooves planted as each pose falls.
const terminalOpaqueBottoms = {
  crash: [436, 436, 436, 433, 355, 351, 351, 351],
  slip: [401, 407, 406, 407, 392, 394, 395, 399],
  trip: [437, 441, 441, 443, 348, 368, 364, 373],
  skid: [389, 437, 437, 437, 363, 363, 363, 372],
} as const

const jumpOpaqueBottoms = [411, 411, 411, 411, 320, 332, 358, 358] as const

function atlasFrameGeometry(frame: number, opaqueBottoms: readonly number[]) {
  const index = Math.min(7, Math.max(0, Math.floor(frame)))
  const column = index % 4
  const row = Math.floor(index / 4)
  const sourceWidth = poseAtlas.columnWidths[column]
  const sourceHeight = poseAtlas.rowHeights[row]

  return {
    index,
    sourceX: poseAtlas.columnStarts[column],
    sourceY: poseAtlas.rowStarts[row],
    sourceWidth,
    sourceHeight,
    sheetWidth: poseAtlas.sheetWidth,
    sheetHeight: poseAtlas.sheetHeight,
    groundOffset: sourceHeight - opaqueBottoms[index] - 1,
  }
}

export function terminalSpriteGeometry(reaction: 'crash' | 'slip' | 'trip' | 'skid', frame: number) {
  return atlasFrameGeometry(frame, terminalOpaqueBottoms[reaction])
}

export function jumpSpriteGeometry(frame: number) {
  return atlasFrameGeometry(frame, jumpOpaqueBottoms)
}
