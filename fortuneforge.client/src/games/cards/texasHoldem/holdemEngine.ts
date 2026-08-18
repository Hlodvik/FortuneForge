import {
  createShuffledDeck,
  seededChance,
  type CardRank,
  type PlayingCardModel,
} from '../shared/cards'

export const SMALL_BLIND = 10
export const BIG_BLIND = 20

export type HoldemDealer = 'player' | 'opponent'
export type HoldemStage = 'preflop' | 'flop' | 'turn' | 'river'
export type HoldemAction = 'fold' | 'check-call' | 'bet-raise'
export type HoldemWinner = 'player' | 'opponent' | 'tie'

export type HandEvaluation = Readonly<{
  category: number
  name: string
  tiebreak: readonly number[]
}>

export type HoldemResult = Readonly<{
  winner: HoldemWinner
  playerHand: HandEvaluation | null
  opponentHand: HandEvaluation | null
  summary: string
  potWon: number
}>

export type HoldemGame = Readonly<{
  seed: number
  deck: readonly PlayingCardModel[]
  playerHole: readonly PlayingCardModel[]
  opponentHole: readonly PlayingCardModel[]
  community: readonly PlayingCardModel[]
  playerChips: number
  opponentChips: number
  pot: number
  dealer: HoldemDealer
  stage: HoldemStage
  toCall: number
  status: 'playing' | 'complete'
  message: string
  result: HoldemResult | null
}>

const handNames = [
  'High Card',
  'One Pair',
  'Two Pair',
  'Three of a Kind',
  'Straight',
  'Flush',
  'Full House',
  'Four of a Kind',
  'Straight Flush',
] as const

function cardValue(rank: CardRank): number {
  return rank === 1 ? 14 : rank
}

function evaluateFive(cards: readonly PlayingCardModel[]): HandEvaluation {
  const values = cards.map((card) => cardValue(card.rank)).sort((left, right) => right - left)
  const counts = new Map<number, number>()
  for (const value of values) counts.set(value, (counts.get(value) ?? 0) + 1)

  const groups = [...counts.entries()].sort((left, right) =>
    right[1] - left[1] || right[0] - left[0],
  )
  const isFlush = cards.every((card) => card.suit === cards[0]?.suit)
  const uniqueValues = [...new Set(values)]
  if (uniqueValues[0] === 14) uniqueValues.push(1)
  let straightHigh = 0
  for (let index = 0; index <= uniqueValues.length - 5; index += 1) {
    const run = uniqueValues.slice(index, index + 5)
    if (run.every((value, offset) => offset === 0 || run[offset - 1]! - value === 1)) {
      straightHigh = run[0]!
      break
    }
  }

  let category = 0
  let tiebreak: number[] = values
  if (isFlush && straightHigh > 0) {
    category = 8
    tiebreak = [straightHigh]
  } else if (groups[0]?.[1] === 4) {
    category = 7
    tiebreak = [groups[0][0], groups.find((group) => group[1] === 1)![0]]
  } else if (groups[0]?.[1] === 3 && groups[1]?.[1] === 2) {
    category = 6
    tiebreak = [groups[0][0], groups[1][0]]
  } else if (isFlush) {
    category = 5
    tiebreak = values
  } else if (straightHigh > 0) {
    category = 4
    tiebreak = [straightHigh]
  } else if (groups[0]?.[1] === 3) {
    category = 3
    tiebreak = [groups[0][0], ...groups.filter((group) => group[1] === 1).map((group) => group[0])]
  } else if (groups[0]?.[1] === 2 && groups[1]?.[1] === 2) {
    category = 2
    const pairs = groups.filter((group) => group[1] === 2).map((group) => group[0]).sort((a, b) => b - a)
    tiebreak = [pairs[0]!, pairs[1]!, groups.find((group) => group[1] === 1)![0]]
  } else if (groups[0]?.[1] === 2) {
    category = 1
    tiebreak = [groups[0][0], ...groups.filter((group) => group[1] === 1).map((group) => group[0])]
  }

  return { category, name: handNames[category]!, tiebreak }
}

function fiveCardCombinations(cards: readonly PlayingCardModel[]): PlayingCardModel[][] {
  const combinations: PlayingCardModel[][] = []
  for (let first = 0; first < cards.length - 4; first += 1) {
    for (let second = first + 1; second < cards.length - 3; second += 1) {
      for (let third = second + 1; third < cards.length - 2; third += 1) {
        for (let fourth = third + 1; fourth < cards.length - 1; fourth += 1) {
          for (let fifth = fourth + 1; fifth < cards.length; fifth += 1) {
            combinations.push([
              cards[first]!, cards[second]!, cards[third]!, cards[fourth]!, cards[fifth]!,
            ])
          }
        }
      }
    }
  }
  return combinations
}

export function compareHandEvaluations(left: HandEvaluation, right: HandEvaluation): number {
  if (left.category !== right.category) return left.category - right.category
  const length = Math.max(left.tiebreak.length, right.tiebreak.length)
  for (let index = 0; index < length; index += 1) {
    const difference = (left.tiebreak[index] ?? 0) - (right.tiebreak[index] ?? 0)
    if (difference !== 0) return difference
  }
  return 0
}

export function evaluateBestHoldemHand(cards: readonly PlayingCardModel[]): HandEvaluation {
  if (cards.length < 5 || cards.length > 7) {
    throw new Error('Texas Hold’em hands must contain between five and seven cards.')
  }
  return fiveCardCombinations(cards)
    .map(evaluateFive)
    .reduce((best, candidate) => compareHandEvaluations(candidate, best) > 0 ? candidate : best)
}

function drawCards(
  deck: readonly PlayingCardModel[],
  count: number,
): { cards: PlayingCardModel[]; deck: PlayingCardModel[] } {
  const nextDeck = [...deck]
  const cards: PlayingCardModel[] = []
  for (let index = 0; index < count; index += 1) cards.push(nextDeck.pop()!)
  return { cards, deck: nextDeck }
}

export function createHoldemGame({
  seed,
  playerChips = 1_000,
  opponentChips = 1_000,
  dealer = 'player',
}: {
  seed: number
  playerChips?: number
  opponentChips?: number
  dealer?: HoldemDealer
}): HoldemGame {
  const bankroll = playerChips < BIG_BLIND || opponentChips < BIG_BLIND
    ? { player: 1_000, opponent: 1_000 }
    : { player: playerChips, opponent: opponentChips }
  let deck = createShuffledDeck(seed)
  const playerHole: PlayingCardModel[] = []
  const opponentHole: PlayingCardModel[] = []
  for (let index = 0; index < 2; index += 1) {
    let dealt = drawCards(deck, 1)
    playerHole.push(dealt.cards[0]!)
    deck = dealt.deck
    dealt = drawCards(deck, 1)
    opponentHole.push(dealt.cards[0]!)
    deck = dealt.deck
  }

  const playerBlind = dealer === 'player' ? SMALL_BLIND : BIG_BLIND
  const opponentBlind = dealer === 'opponent' ? SMALL_BLIND : BIG_BLIND
  let finalOpponentChips = bankroll.opponent - opponentBlind
  let pot = playerBlind + opponentBlind
  let toCall = BIG_BLIND - playerBlind
  let message = `You are on the button. Call R${toCall} or raise.`

  if (dealer === 'opponent') {
    const opponentCall = BIG_BLIND - opponentBlind
    finalOpponentChips -= opponentCall
    pot += opponentCall
    toCall = 0
    message = 'The dealer completes the blind. Check or raise your option.'
  }

  return {
    seed,
    deck,
    playerHole,
    opponentHole,
    community: [],
    playerChips: bankroll.player - playerBlind,
    opponentChips: finalOpponentChips,
    pot,
    dealer,
    stage: 'preflop',
    toCall,
    status: 'playing',
    message,
    result: null,
  }
}

export function holdemBetSize(stage: HoldemStage): number {
  return stage === 'preflop' || stage === 'flop' ? 40 : 80
}

function botCallsBet(game: HoldemGame): boolean {
  const chance = seededChance(game.seed, game.community.length + game.pot)
  if (game.stage === 'preflop') {
    const [first, second] = game.opponentHole
    if (!first || !second) return true
    const pair = first.rank === second.rank
    const highCard = Math.max(cardValue(first.rank), cardValue(second.rank))
    const suited = first.suit === second.suit
    return pair || highCard >= 12 || suited || chance < 0.48
  }

  const evaluation = evaluateBestHoldemHand([...game.opponentHole, ...game.community])
  return evaluation.category >= 1 || chance < (game.stage === 'river' ? 0.34 : 0.44)
}

function settleFold(game: HoldemGame, winner: Exclude<HoldemWinner, 'tie'>, summary: string): HoldemGame {
  return {
    ...game,
    playerChips: game.playerChips + (winner === 'player' ? game.pot : 0),
    opponentChips: game.opponentChips + (winner === 'opponent' ? game.pot : 0),
    pot: 0,
    status: 'complete',
    message: summary,
    result: { winner, playerHand: null, opponentHand: null, summary, potWon: game.pot },
  }
}

function settleShowdown(game: HoldemGame): HoldemGame {
  const playerHand = evaluateBestHoldemHand([...game.playerHole, ...game.community])
  const opponentHand = evaluateBestHoldemHand([...game.opponentHole, ...game.community])
  const comparison = compareHandEvaluations(playerHand, opponentHand)
  const winner: HoldemWinner = comparison > 0 ? 'player' : comparison < 0 ? 'opponent' : 'tie'
  const playerShare = winner === 'player' ? game.pot : winner === 'tie' ? Math.ceil(game.pot / 2) : 0
  const opponentShare = game.pot - playerShare
  const summary = winner === 'player'
    ? `You win with ${playerHand.name}.`
    : winner === 'opponent'
      ? `The opponent wins with ${opponentHand.name}.`
      : `Split pot — both players hold ${playerHand.name}.`

  return {
    ...game,
    playerChips: game.playerChips + playerShare,
    opponentChips: game.opponentChips + opponentShare,
    pot: 0,
    status: 'complete',
    message: summary,
    result: { winner, playerHand, opponentHand, summary, potWon: game.pot },
  }
}

function advanceStreet(game: HoldemGame): HoldemGame {
  if (game.stage === 'river') return settleShowdown(game)

  const burn = drawCards(game.deck, 1)
  const revealCount = game.stage === 'preflop' ? 3 : 1
  const reveal = drawCards(burn.deck, revealCount)
  const nextStage: HoldemStage = game.stage === 'preflop'
    ? 'flop'
    : game.stage === 'flop'
      ? 'turn'
      : 'river'
  const streetName = nextStage === 'flop' ? 'The flop' : nextStage === 'turn' ? 'The turn' : 'The river'
  return {
    ...game,
    deck: reveal.deck,
    community: [...game.community, ...reveal.cards],
    stage: nextStage,
    toCall: 0,
    message: `${streetName} is out. Your opponent checks.`,
  }
}

export function playHoldemAction(game: HoldemGame, action: HoldemAction): HoldemGame {
  if (game.status !== 'playing') return game
  if (action === 'fold') return settleFold(game, 'opponent', 'You fold. The opponent takes the pot.')

  if (action === 'check-call') {
    const callAmount = Math.min(game.toCall, game.playerChips)
    return advanceStreet({
      ...game,
      playerChips: game.playerChips - callAmount,
      pot: game.pot + callAmount,
      toCall: 0,
    })
  }

  const bet = Math.min(holdemBetSize(game.stage), game.playerChips - game.toCall)
  if (bet <= 0) return playHoldemAction(game, 'check-call')
  const playerCost = game.toCall + bet
  const afterPlayerBet: HoldemGame = {
    ...game,
    playerChips: game.playerChips - playerCost,
    pot: game.pot + playerCost,
    toCall: 0,
  }
  if (!botCallsBet(afterPlayerBet)) {
    return settleFold(afterPlayerBet, 'player', `Your opponent folds. You win R${afterPlayerBet.pot}.`)
  }

  const opponentCall = Math.min(bet, afterPlayerBet.opponentChips)
  const unmatchedBet = bet - opponentCall
  return advanceStreet({
    ...afterPlayerBet,
    playerChips: afterPlayerBet.playerChips + unmatchedBet,
    opponentChips: afterPlayerBet.opponentChips - opponentCall,
    pot: afterPlayerBet.pot + opponentCall - unmatchedBet,
    message: `Your opponent calls R${opponentCall}.`,
  })
}
