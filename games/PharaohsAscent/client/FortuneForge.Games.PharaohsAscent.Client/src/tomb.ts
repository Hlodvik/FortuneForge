export type TombRelic = 'sun' | 'moon' | 'serpent'
export type TombRoomKind = 'coin' | 'relic' | 'key'
export type TombRelics = Readonly<Record<TombRelic, number>>
export type TombChoice = Readonly<{ id: string; kind: TombRoomKind; relic: TombRelic | null }>
export type TombPathEntry = Readonly<{ step: number; kind: TombRoomKind; relic: TombRelic | null; awardPoints: number }>
export type TombRun = Readonly<{
  id: string
  fundedWagerPoints: number
  torchesRemaining: number
  keys: number
  relics: TombRelics
  relicMultiplier: number
  roomPoints: number
  bankedPoints: number
  phase: 'explore' | 'finale'
  choices: readonly TombChoice[]
  path: readonly TombPathEntry[]
}>
export type TombResult = Readonly<{
  id: string
  fundedWagerPoints: number
  roomsOpened: number
  keys: number
  relics: TombRelics
  relicMultiplier: number
  basePoints: number
  roomPoints: number
  finalePoints: number
  payoutPoints: number
  reason: 'torches' | 'sarcophagus' | 'collect'
}>
export type TombAction = Readonly<{
  action: 'enter' | 'open-room' | 'open-sarcophagus' | 'collect' | 'dismiss'
  expectedRevision: number
  choiceId?: string | null
}>
export type TombState = Readonly<{
  contractVersion: 1
  revision: number
  funding: Readonly<{ sigils: number; requiredSigils: number; averageWagerPoints: number; minimumScarabs?: number }>
  tickets: readonly Readonly<{ id: string; fundedWagerPoints: number }>[]
  activeRun: TombRun | null
  lastRun: TombResult | null
  pendingAction: TombAction | null
}>

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const integer = (value: unknown, maximum = Number.MAX_SAFE_INTEGER): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value <= maximum
const identifier = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 160
const relic = (value: unknown): value is TombRelic => value === 'sun' || value === 'moon' || value === 'serpent'
const kind = (value: unknown): value is TombRoomKind => value === 'coin' || value === 'relic' || value === 'key'
const relics = (value: unknown): value is TombRelics => record(value) &&
  integer(value.sun, 6) && integer(value.moon, 6) && integer(value.serpent, 6)
const room = (value: unknown): value is TombChoice => record(value) && identifier(value.id) && kind(value.kind) &&
  (value.kind === 'relic' ? relic(value.relic) : value.relic === null)
const pathEntry = (value: unknown): value is TombPathEntry => record(value) && integer(value.step, 6) && value.step > 0 &&
  kind(value.kind) && (value.kind === 'relic' ? relic(value.relic) : value.relic === null) && integer(value.awardPoints)

function run(value: unknown): value is TombRun {
  if (!record(value) || !identifier(value.id) || !integer(value.fundedWagerPoints) || value.fundedWagerPoints === 0 ||
    !integer(value.torchesRemaining, 6) || !integer(value.keys, 3) || !relics(value.relics) ||
    !integer(value.relicMultiplier, 7) || value.relicMultiplier === 0 || !integer(value.roomPoints) ||
    !integer(value.bankedPoints) || !(value.phase === 'explore' || value.phase === 'finale') ||
    !Array.isArray(value.choices) || !value.choices.every(room) ||
    !Array.isArray(value.path) || value.path.length > 6 || !value.path.every(pathEntry)) return false
  if (new Set(value.choices.map(choice => choice.id)).size !== value.choices.length ||
    value.path.some((entry, index) => entry.step !== index + 1)) return false
  return value.phase === 'explore'
    ? value.choices.length === 3 && value.keys < 3 && value.torchesRemaining > 0
    : value.choices.length === 0 && value.keys === 3
}

function result(value: unknown): value is TombResult {
  return record(value) && identifier(value.id) && integer(value.fundedWagerPoints) && value.fundedWagerPoints > 0 &&
    integer(value.roomsOpened, 6) && integer(value.keys, 3) && relics(value.relics) &&
    integer(value.relicMultiplier, 7) && value.relicMultiplier > 0 && integer(value.basePoints) &&
    integer(value.roomPoints) && integer(value.finalePoints) && integer(value.payoutPoints) &&
    (value.reason === 'torches' || value.reason === 'sarcophagus' || value.reason === 'collect')
}

export function isTombAction(value: unknown): value is TombAction {
  return record(value) && integer(value.expectedRevision) &&
    (value.action === 'enter' || value.action === 'open-room' || value.action === 'open-sarcophagus' ||
      value.action === 'collect' || value.action === 'dismiss') &&
    (value.action === 'open-room' ? identifier(value.choiceId) : value.choiceId === undefined || value.choiceId === null)
}

/** Decode only server-owned expedition state; choices and awards are never generated here. */
export function decodeTombState(value: unknown): TombState | null {
  if (!record(value) || value.kind !== 'pharaoh-ascent' || !record(value.tomb)) return null
  const tomb = value.tomb
  if (tomb.contractVersion !== 1 || !integer(tomb.revision) || !record(tomb.funding) ||
    !integer(tomb.funding.requiredSigils) || tomb.funding.requiredSigils === 0 ||
    !(tomb.funding.minimumScarabs === undefined || integer(tomb.funding.minimumScarabs, 30) && tomb.funding.minimumScarabs > 0) ||
    !integer(tomb.funding.sigils) || tomb.funding.sigils >= tomb.funding.requiredSigils ||
    !integer(tomb.funding.averageWagerPoints) || !Array.isArray(tomb.tickets) ||
    !tomb.tickets.every(ticket => record(ticket) && identifier(ticket.id) && integer(ticket.fundedWagerPoints) && ticket.fundedWagerPoints > 0) ||
    new Set(tomb.tickets.map(ticket => ticket.id)).size !== tomb.tickets.length ||
    !(tomb.activeRun === null || run(tomb.activeRun)) || !(tomb.lastRun === null || result(tomb.lastRun)) ||
    !(tomb.pendingAction === null || isTombAction(tomb.pendingAction))) return null
  if (tomb.activeRun !== null && tomb.lastRun !== null) return null
  return tomb as TombState
}
