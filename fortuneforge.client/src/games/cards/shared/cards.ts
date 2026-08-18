export const CARD_SUITS = ['clubs', 'diamonds', 'hearts', 'spades'] as const

export type CardSuit = (typeof CARD_SUITS)[number]
export type CardRank = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13

export type PlayingCardModel = Readonly<{
  id: string
  suit: CardSuit
  rank: CardRank
}>

const rankNames: Record<CardRank, string> = {
  1: 'Ace',
  2: 'Two',
  3: 'Three',
  4: 'Four',
  5: 'Five',
  6: 'Six',
  7: 'Seven',
  8: 'Eight',
  9: 'Nine',
  10: 'Ten',
  11: 'Jack',
  12: 'Queen',
  13: 'King',
}

export function cardLabel(card: PlayingCardModel): string {
  return `${rankNames[card.rank]} of ${card.suit}`
}

function randomSource(seed: number) {
  let value = seed >>> 0
  return () => {
    value += 0x6d2b79f5
    let result = value
    result = Math.imul(result ^ result >>> 15, result | 1)
    result ^= result + Math.imul(result ^ result >>> 7, result | 61)
    return ((result ^ result >>> 14) >>> 0) / 4294967296
  }
}

export function freshCardSeed(): number {
  const values = new Uint32Array(1)
  crypto.getRandomValues(values)
  return values[0] ?? Date.now()
}

export function createShuffledDeck(seed: number): PlayingCardModel[] {
  const cards: PlayingCardModel[] = []
  for (const suit of CARD_SUITS) {
    for (let rank = 1; rank <= 13; rank += 1) {
      cards.push({ id: `${suit}-${rank}`, suit, rank: rank as CardRank })
    }
  }

  const random = randomSource(seed)
  for (let index = cards.length - 1; index > 0; index -= 1) {
    const swapIndex = Math.floor(random() * (index + 1))
    const held = cards[index]!
    cards[index] = cards[swapIndex]!
    cards[swapIndex] = held
  }
  return cards
}

export function seededChance(seed: number, salt: number): number {
  return randomSource(seed ^ Math.imul(salt + 1, 0x9e3779b1))()
}
