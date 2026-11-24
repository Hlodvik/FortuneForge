import ruleContract from './assets/contracts/crooked-road-v1.json' with { type: 'json' }

export const ROAD_DIRECTIONS = ['north', 'east', 'south', 'west'] as const
export type RoadDirection = (typeof ROAD_DIRECTIONS)[number]
export type RoadTileKind = 'straight' | 'corner' | 'junction'
export type RoadDestination = 'well' | 'fort' | 'hill'

export type RoadTile = Readonly<{ id: string; kind: RoadTileKind; rotation: number }>
export type RoadTicket = Readonly<{ id: string; fundedWagerPoints: number }>
export type RoadDoor = Readonly<{
  index: number
  selected: boolean
  revealed: boolean
  prizePoints: number | null
}>
export type RoadBonus = Readonly<{
  id: string
  kind: RoadDestination
  step: number
  totalSteps: number
  awardPoints: number
  fundedWagerPoints: number
  choice: number | null
  board: readonly (readonly string[])[] | null
  held: readonly number[] | null
  respinsRemaining: number
  doors: readonly RoadDoor[]
}>
export type RoadFeatureEvent = Readonly<{ kind: RoadDestination; completed: boolean; bonus: RoadBonus }>
export type RoadState = Readonly<{
  contractVersion: 1
  revision: number
  board: readonly (RoadTile | null)[]
  hand: readonly RoadTile[]
  queuedTiles: readonly RoadTile[]
  reserveTileCounts: Readonly<Record<RoadTileKind, number>>
  funding: Readonly<{ spins: number; requiredSpins: number; wagerPointsTotal: number }>
  tickets: readonly RoadTicket[]
  activeBonus: RoadBonus | null
  reachableDestinations: readonly RoadDestination[]
  lastTravel: Readonly<{ destination: RoadDestination; path: readonly number[] }> | null
  completedJourneys: number
}>

export type RoadAction = Readonly<{
  expectedRevision: number
  action: 'place' | 'rotate' | 'remove' | 'set-off' | 'choose-spring' | 'choose-door'
  handIndex?: number
  cellIndex?: number
  rotation?: number
  destination?: RoadDestination
  choice?: number
}>

const destinationCopy = {
  well: { label: 'Wishing Well', description: 'Choose a spring, then reveal three wild ripples.' },
  fort: { label: 'Fairy Fort', description: 'Hold fairy coins; a new coin restores three respins.' },
  hill: { label: 'Hollow Hill', description: 'Choose a door at each of three treasure stages.' },
} as const

export const ROAD_DESTINATIONS = Object.fromEntries(ruleContract.road.destinations.map(target => [
  target.id, { ...destinationCopy[target.id as RoadDestination], cell: target.cell,
    exit: ROAD_DIRECTIONS[Math.log2(target.port)]! },
])) as Readonly<Record<RoadDestination, Readonly<{
  label: string; cell: number; exit: RoadDirection; description: string
}>>>
export const ROAD_ENTRY_CELL = ruleContract.road.entryCell

export function roadTilePorts(tile: Pick<RoadTile, 'kind' | 'rotation'>): readonly RoadDirection[] {
  const ports = ruleContract.road.tilePorts[tile.kind]
  return ROAD_DIRECTIONS.filter((_, port) => (ports & 1 << (port - tile.rotation + 4) % 4) !== 0)
}

export function roadPaths(board: readonly (RoadTile | null)[]): Partial<Record<RoadDestination, readonly number[]>> {
  const entry = ROAD_ENTRY_CELL
  const entryPort = ROAD_DIRECTIONS[Math.log2(ruleContract.road.entryPort)]!
  if (board.length !== 9 || !board[entry] || !roadTilePorts(board[entry]).includes(entryPort)) return {}
  const paths = new Map<number, readonly number[]>([[entry, [entry]]])
  const queue = [entry]
  const offsets = [-3, 1, 3, -1]
  for (let cursor = 0; cursor < queue.length; cursor++) {
    const cell = queue[cursor]!
    const tile = board[cell]!
    for (const direction of roadTilePorts(tile)) {
      const port = ROAD_DIRECTIONS.indexOf(direction)
      if (port === 0 && cell < 3 || port === 1 && cell % 3 === 2 ||
          port === 2 && cell >= 6 || port === 3 && cell % 3 === 0) continue
      const next = cell + offsets[port]!
      const neighbor = board[next]
      if (!neighbor || paths.has(next) || !roadTilePorts(neighbor).includes(ROAD_DIRECTIONS[(port + 2) % 4]!)) continue
      paths.set(next, [...paths.get(cell)!, next])
      queue.push(next)
    }
  }
  const result: Partial<Record<RoadDestination, readonly number[]>> = {}
  for (const destination of Object.keys(ROAD_DESTINATIONS) as RoadDestination[]) {
    const { cell, exit } = ROAD_DESTINATIONS[destination]
    const tile = board[cell]
    if (tile && paths.has(cell) && roadTilePorts(tile).includes(exit)) result[destination] = paths.get(cell)!
  }
  return result
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
function integer(value: unknown, maximum = Number.MAX_SAFE_INTEGER): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value <= maximum
}
function identifier(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= 160
}
function symbol(value: unknown): value is string {
  return typeof value === 'string' && ruleContract.symbols.some(candidate => candidate.id === value)
}
function destination(value: unknown): value is RoadDestination {
  return value === 'well' || value === 'fort' || value === 'hill'
}
function tile(value: unknown): value is RoadTile {
  return record(value) && identifier(value.id) &&
    (value.kind === 'straight' || value.kind === 'corner' || value.kind === 'junction') && integer(value.rotation, 3)
}
function bonus(value: unknown): value is RoadBonus {
  if (!record(value) || !identifier(value.id) || !destination(value.kind) ||
      !integer(value.step, 27) || !integer(value.totalSteps, 27) || value.totalSteps < value.step ||
      !integer(value.awardPoints) || !integer(value.fundedWagerPoints) || value.fundedWagerPoints === 0 ||
      value.choice !== null && !integer(value.choice, 2) || !integer(value.respinsRemaining, 3) ||
      !Array.isArray(value.doors) || value.doors.length > 3 || !value.doors.every(door =>
        record(door) && integer(door.index, 2) && typeof door.selected === 'boolean' &&
        typeof door.revealed === 'boolean' && (door.prizePoints === null || integer(door.prizePoints))) ||
      value.board !== null && (!Array.isArray(value.board) || value.board.length !== 5 ||
        !value.board.every(reel => Array.isArray(reel) && reel.length === 3 && reel.every(symbol))) ||
      value.held !== null && (!Array.isArray(value.held) || value.held.length !== 9 || !value.held.every(coin => integer(coin)))) return false
  if (value.totalSteps !== (value.kind === 'fort' ? 27 : 3) ||
      value.kind === 'fort' && value.held === null || value.kind === 'hill' && value.doors.length !== 3 ||
      value.kind === 'well' && value.step > 0 && (value.board === null || value.choice === null)) return false
  return new Set(value.doors.map(door => (door as RoadDoor).index)).size === value.doors.length
}

export function decodeRoadFeature(value: unknown): RoadFeatureEvent | null {
  return record(value) && destination(value.kind) && typeof value.completed === 'boolean' &&
    bonus(value.bonus) && value.bonus.kind === value.kind ? value as RoadFeatureEvent : null
}

export function decodeRoadState(value: unknown): RoadState | null {
  if (!record(value) || value.contractVersion !== 1 || !integer(value.revision) ||
      !Array.isArray(value.board) || value.board.length !== 9 || !value.board.every(cell => cell === null || tile(cell)) ||
      !Array.isArray(value.hand) || value.hand.length > 2 || !value.hand.every(tile) ||
      !Array.isArray(value.queuedTiles) || value.queuedTiles.length > 12 || !value.queuedTiles.every(tile) ||
      !record(value.reserveTileCounts) || !integer(value.reserveTileCounts.straight) ||
      !integer(value.reserveTileCounts.corner) || !integer(value.reserveTileCounts.junction) ||
      !record(value.funding) || value.funding.requiredSpins !== ruleContract.fundingSpins || !integer(value.funding.spins, ruleContract.fundingSpins - 1) ||
      !integer(value.funding.wagerPointsTotal) ||
      !Array.isArray(value.tickets) || value.tickets.length > 16 || !value.tickets.every(ticket =>
        record(ticket) && identifier(ticket.id) && integer(ticket.fundedWagerPoints) && ticket.fundedWagerPoints > 0) ||
      value.activeBonus !== null && !bonus(value.activeBonus) ||
      !Array.isArray(value.reachableDestinations) || value.reachableDestinations.length > 3 ||
      !value.reachableDestinations.every(destination) || !integer(value.completedJourneys)) return null
  if (value.lastTravel !== null && (!record(value.lastTravel) || !destination(value.lastTravel.destination) ||
      !Array.isArray(value.lastTravel.path) || value.lastTravel.path.length < 1 || value.lastTravel.path.length > 9 ||
      !value.lastTravel.path.every(cell => integer(cell, 8)) || value.lastTravel.path[0] !== ROAD_ENTRY_CELL ||
      new Set(value.lastTravel.path).size !== value.lastTravel.path.length ||
      !value.lastTravel.path.every((cell, index, path) => index === 0 ||
        Math.abs(Math.floor(cell / 3) - Math.floor(path[index - 1] / 3)) + Math.abs(cell % 3 - path[index - 1] % 3) === 1))) return null
  const tiles = [...value.board.filter(cell => cell !== null), ...value.hand, ...value.queuedTiles] as RoadTile[]
  if (new Set(tiles.map(cell => cell.id)).size !== tiles.length ||
      new Set(value.reachableDestinations).size !== value.reachableDestinations.length ||
      new Set(value.tickets.map(ticket => ticket.id)).size !== value.tickets.length) return null
  return value as unknown as RoadState
}
