import { CARD_SUITS, createShuffledDeck, type CardRank, type CardSuit } from '../shared/cards'
import type {
  SolitaireCard,
  SolitaireCommand,
  SolitaireGame,
  SolitairePileReference,
} from './solitaireTypes'

type LocalFaceDownCard = Readonly<{
  isFaceUp: false
  hiddenId: string
  hiddenSuit: CardSuit
  hiddenRank: CardRank
}>

type LocalCard = SolitaireCard | LocalFaceDownCard

export class SolitaireRuleError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'SolitaireRuleError'
  }
}

export function createLocalSolitaireGame(seed: number, drawCount: 1 | 3): SolitaireGame {
  const cards: LocalCard[] = createShuffledDeck(seed).map((card) => hide(card))
  const tableau: LocalCard[][] = Array.from({ length: 7 }, () => [])
  for (let column = 0; column < 7; column += 1) {
    for (let row = 0; row <= column; row += 1) {
      const card = cards.pop()
      if (card === undefined) throw new Error('The Solitaire deck is incomplete.')
      tableau[column]!.push(row === column ? reveal(card) : card)
    }
  }
  return {
    stock: cards,
    waste: [],
    foundations: CARD_SUITS.map(() => []),
    tableau,
    drawCount,
    score: 0,
    moves: 0,
    message: 'Your move',
  } as SolitaireGame
}

export function projectRedactedDraw(game: SolitaireGame): SolitaireGame {
  if (game.stock.length === 0) {
    if (game.waste.length === 0) return game
    return {
      ...game,
      stock: game.waste.map(() => ({ isFaceUp: false })),
      waste: [],
      score: Math.max(0, game.score - 100),
      moves: game.moves + 1,
      message: game.score > 0 ? '−100 · Stock recycled' : 'Stock recycled',
    }
  }

  const drawn = Math.min(game.drawCount, game.stock.length)
  return {
    ...game,
    stock: game.stock.slice(0, -drawn),
    waste: [...game.waste, ...Array.from({ length: drawn }, () => ({ isFaceUp: false } as const))],
    moves: game.moves + 1,
    message: `Drawing ${drawn} ${drawn === 1 ? 'card' : 'cards'}…`,
  }
}

export function applyLocalSolitaireCommand(
  game: SolitaireGame,
  command: SolitaireCommand,
): SolitaireGame {
  if (command.type === 'draw') return draw(game)
  if (command.type === 'flip') return flip(game, command.column)
  if (command.type === 'move') return move(game, command.from, command.startIndex, command.to)
  throw new SolitaireRuleError('Choose a draw, flip, or move command.')
}

export function canApplyLocalSolitaireCommand(
  game: SolitaireGame,
  command: SolitaireCommand,
): boolean {
  try {
    applyLocalSolitaireCommand(game, command)
    return true
  } catch {
    return false
  }
}

export function nextAutoFoundationCommand(game: SolitaireGame): SolitaireCommand | null {
  if (game.stock.length > 0 || game.tableau.some((pile) => pile.some((card) => !card.isFaceUp))) {
    return null
  }
  const candidates: Array<Readonly<{
    from: SolitairePileReference
    startIndex: number
  }>> = []
  if (game.waste.length > 0) {
    candidates.push({ from: { zone: 'waste', index: 0 }, startIndex: game.waste.length - 1 })
  }
  game.tableau.forEach((pile, index) => {
    if (pile.length > 0) candidates.push({
      from: { zone: 'tableau', index },
      startIndex: pile.length - 1,
    })
  })
  for (const candidate of candidates) {
    const foundation = firstLegalFoundation(game, candidate.from, candidate.startIndex)
    if (foundation !== null) {
      return {
        type: 'move',
        from: candidate.from,
        startIndex: candidate.startIndex,
        to: { zone: 'foundation', index: foundation },
      }
    }
  }
  return null
}

export function autoFinishLocalSolitaire(game: SolitaireGame): Readonly<{
  game: SolitaireGame
  commands: readonly SolitaireCommand[]
}> {
  let current = game
  const commands: SolitaireCommand[] = []
  for (let move = 0; move < 52; move += 1) {
    const command = nextAutoFoundationCommand(current)
    if (command === null) break
    current = applyLocalSolitaireCommand(current, command)
    commands.push(command)
  }
  return { game: current, commands }
}

export function firstLegalFoundation(
  game: SolitaireGame,
  from: SolitairePileReference,
  startIndex: number,
): number | null {
  const source = pileAt(game, from)
  const moving = source.slice(startIndex)
  if (!canLift(from, source, startIndex, moving)) return null
  for (let index = 0; index < game.foundations.length; index += 1) {
    if (canPlace({ zone: 'foundation', index }, moving, game.foundations[index]!)) return index
  }
  return null
}

export function isLocalSolitaireWon(game: SolitaireGame): boolean {
  return game.foundations.every((foundation) => foundation.length === 13)
}

function draw(game: SolitaireGame): SolitaireGame {
  if (game.stock.length === 0) {
    if (game.waste.length === 0) {
      throw new SolitaireRuleError('There are no cards to draw or recycle.')
    }
    return {
      ...game,
      stock: [...game.waste].reverse().map((card) => hide(card)),
      waste: [],
      score: Math.max(0, game.score - 100),
      moves: game.moves + 1,
      message: game.score > 0 ? '−100 · Stock recycled' : 'Stock recycled',
    }
  }
  const stock = [...game.stock]
  const drawn: SolitaireCard[] = []
  while (drawn.length < game.drawCount && stock.length > 0) {
    const card = stock.pop()
    if (card === undefined) break
    drawn.push(reveal(card as LocalCard))
  }
  return {
    ...game,
    stock,
    waste: [...game.waste, ...drawn],
    moves: game.moves + 1,
    message: `Drew ${drawn.length} ${drawn.length === 1 ? 'card' : 'cards'}`,
  }
}

function flip(game: SolitaireGame, column: number): SolitaireGame {
  if (!Number.isInteger(column) || column < 0 || column > 6) {
    throw new SolitaireRuleError('Choose a tableau column from 0 through 6.')
  }
  const pile = game.tableau[column]!
  const top = pile[pile.length - 1]
  if (top === undefined || top.isFaceUp) {
    throw new SolitaireRuleError('Only the top face-down tableau card can be flipped.')
  }
  const revealed = reveal(top as LocalCard)
  return {
    ...game,
    tableau: replace(game.tableau, column, [...pile.slice(0, -1), revealed]),
    score: game.score + 5,
    moves: game.moves + 1,
    message: '+5 · Card revealed',
  }
}

function move(
  game: SolitaireGame,
  from: SolitairePileReference,
  startIndex: number,
  to: SolitairePileReference,
): SolitaireGame {
  validatePile(from, true)
  validatePile(to, false)
  if (from.zone === to.zone && from.index === to.index) {
    throw new SolitaireRuleError('A card cannot move onto its current pile.')
  }
  const source = pileAt(game, from)
  if (!Number.isInteger(startIndex) || startIndex < 0 || startIndex >= source.length) {
    throw new SolitaireRuleError('The selected source card does not exist.')
  }
  const moving = source.slice(startIndex)
  if (!canLift(from, source, startIndex, moving)) {
    throw new SolitaireRuleError('The selected cards are not a movable Klondike run.')
  }
  const destination = pileAt(game, to)
  if (!canPlace(to, moving, destination)) {
    throw new SolitaireRuleError('Those cards cannot be placed on that pile.')
  }

  const remaining = source.slice(0, startIndex)
  const revealed = from.zone === 'tableau'
    && remaining.length > 0
    && !remaining[remaining.length - 1]!.isFaceUp
  // A competitive snapshot intentionally hides a face-down identity. Keep it
  // face-down during the short optimistic interval; the accepted server
  // response reveals it. Free games carry the hidden local identity.
  if (revealed && hasHiddenIdentity(remaining[remaining.length - 1]!)) {
    remaining[remaining.length - 1] = reveal(remaining[remaining.length - 1]! as LocalCard)
  }
  let updated = replacePile(game, from, remaining)
  updated = replacePile(updated, to, [...destination, ...moving])

  let delta = 0
  const reasons: string[] = []
  if (to.zone === 'foundation') {
    delta += 10
    reasons.push('Card home')
  }
  if (from.zone === 'waste' && to.zone === 'tableau') {
    delta += 5
    reasons.push('Waste to tableau')
  }
  if (from.zone === 'foundation' && to.zone === 'tableau') {
    delta -= 15
    reasons.push('Foundation card returned')
  }
  if (revealed) {
    delta += 5
    reasons.push('Card revealed')
  }
  const prefix = delta > 0 ? `+${delta}` : delta < 0 ? `−${Math.abs(delta)}` : ''
  return {
    ...updated,
    score: Math.max(0, game.score + delta),
    moves: game.moves + 1,
    message: delta === 0 ? 'Nice move' : `${prefix} · ${reasons.join(' & ')}`,
  }
}

function canLift(
  from: SolitairePileReference,
  source: readonly SolitaireCard[],
  startIndex: number,
  moving: readonly SolitaireCard[],
): boolean {
  if (from.zone !== 'tableau') {
    return startIndex === source.length - 1 && moving.length === 1 && moving[0]?.isFaceUp === true
  }
  if (moving.length === 0 || moving.some((card) => !card.isFaceUp)) return false
  for (let index = 0; index < moving.length - 1; index += 1) {
    const upper = moving[index]!
    const lower = moving[index + 1]!
    if (!upper.isFaceUp || !lower.isFaceUp
      || upper.rank !== lower.rank + 1
      || isRed(upper.suit) === isRed(lower.suit)) return false
  }
  return true
}

function canPlace(
  to: SolitairePileReference,
  moving: readonly SolitaireCard[],
  destination: readonly SolitaireCard[],
): boolean {
  const lead = moving[0]
  if (lead === undefined || !lead.isFaceUp) return false
  if (to.zone === 'foundation') {
    if (moving.length !== 1) return false
    const top = destination[destination.length - 1]
    return top === undefined
      ? lead.rank === 1
      : top.isFaceUp && top.suit === lead.suit && lead.rank === top.rank + 1
  }
  const top = destination[destination.length - 1]
  return top === undefined
    ? lead.rank === 13
    : top.isFaceUp && top.rank === lead.rank + 1 && isRed(top.suit) !== isRed(lead.suit)
}

function pileAt(game: SolitaireGame, pile: SolitairePileReference): readonly SolitaireCard[] {
  validatePile(pile, true)
  if (pile.zone === 'waste') return game.waste
  if (pile.zone === 'foundation') return game.foundations[pile.index]!
  return game.tableau[pile.index]!
}

function replacePile(
  game: SolitaireGame,
  pile: SolitairePileReference,
  cards: readonly SolitaireCard[],
): SolitaireGame {
  if (pile.zone === 'waste') return { ...game, waste: cards }
  if (pile.zone === 'foundation') {
    return { ...game, foundations: replace(game.foundations, pile.index, cards) }
  }
  return { ...game, tableau: replace(game.tableau, pile.index, cards) }
}

function replace<T>(values: readonly T[], index: number, value: T): readonly T[] {
  return values.map((current, currentIndex) => currentIndex === index ? value : current)
}

function validatePile(pile: SolitairePileReference, allowWaste: boolean) {
  if (pile.zone === 'waste') {
    if (allowWaste && pile.index === 0) return
    throw new SolitaireRuleError('The waste pile reference is invalid.')
  }
  if (pile.zone === 'foundation' && pile.index >= 0 && pile.index < 4) return
  if (pile.zone === 'tableau' && pile.index >= 0 && pile.index < 7) return
  throw new SolitaireRuleError('The requested pile reference is invalid.')
}

function hide(card: LocalCard | { id: string, suit: CardSuit, rank: CardRank }): LocalFaceDownCard {
  if (!("isFaceUp" in card)) {
    return {
      isFaceUp: false,
      hiddenId: card.id,
      hiddenSuit: card.suit,
      hiddenRank: card.rank,
    }
  }
  if (!card.isFaceUp && hasHiddenIdentity(card)) return card
  if (!card.isFaceUp) {
    throw new SolitaireRuleError('A hidden server card has no local identity.')
  }
  return {
    isFaceUp: false,
    hiddenId: card.id,
    hiddenSuit: card.suit,
    hiddenRank: card.rank,
  }
}

function reveal(card: LocalCard): SolitaireCard {
  if (card.isFaceUp) return card
  if (!hasHiddenIdentity(card)) {
    throw new SolitaireRuleError('Wait for the server to reveal this card.')
  }
  return {
    id: card.hiddenId,
    suit: card.hiddenSuit,
    rank: card.hiddenRank,
    isFaceUp: true,
  }
}

function hasHiddenIdentity(card: SolitaireCard | LocalFaceDownCard): card is LocalFaceDownCard {
  return !card.isFaceUp && 'hiddenId' in card
}

function isRed(suit: CardSuit): boolean {
  return suit === 'diamonds' || suit === 'hearts'
}
