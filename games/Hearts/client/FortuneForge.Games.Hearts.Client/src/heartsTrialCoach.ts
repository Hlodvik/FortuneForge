import type { HeartsCard, HeartsMatch } from './contracts'

export type HeartsTrialCoach = Readonly<{
  kind: 'pass' | 'confirm-pass' | 'play' | 'waiting' | 'complete'
  title: string
  message: string
  why?: string
  suggestedCodes: readonly string[]
}>

/** Beginner suggestions use only the viewer's hand and already exposed cards. */
export function getTrialCoach(match: HeartsMatch, selected: readonly string[] = []): HeartsTrialCoach {
  if (match.phase === 'complete') return {
    kind: 'complete', title: 'That’s a wrap!',
    message: 'In Hearts, less is more: each heart adds 1 point, and the queen of spades adds 13. Aim for the lowest score.', suggestedCodes: [],
  }

  if (match.phase === 'passing') {
    if (match.submittedPasses.includes(match.humanSeat)) return {
      kind: 'waiting', title: 'Cards on the move!',
      message: 'The trial bots are choosing their cards. Three new cards are heading your way.',
      why: 'Passing happens before the first trick. The direction changes each hand: left, right, across, then a hand with no pass.', suggestedCodes: [],
    }
    const ownCodes = new Set(match.hand.map(card => card.code))
    const chosen = [...new Set(selected)].filter(code => ownCodes.has(code))
    if (chosen.length === 3) return {
      kind: 'confirm-pass', title: 'Three picked—send them!',
      message: `Tap Pass cards to send your three selected cards ${passDirection(match.passDirection)}. You’ll get three back.`,
      why: 'Try passing high-risk cards, or clearing a short suit. With no cards in a suit, you can discard a point card when that suit is led—if the rules allow it.', suggestedCodes: [],
    }
    const suggestedCodes = [...match.hand].sort((left, right) =>
      passRisk(right) - passRisk(left) || left.code.localeCompare(right.code)).slice(0, 3).map(card => card.code)
    return {
      kind: 'pass', title: 'Let’s pass three troublemakers',
      message: `Tap the three marked cards, then Pass cards. They’ll go ${passDirection(match.passDirection)}. You may choose different cards.`,
      why: passExplanation(match.hand.filter(card => suggestedCodes.includes(card.code))), suggestedCodes,
    }
  }

  if (!match.yourTurn || match.legalCards.length === 0) return {
    kind: 'waiting', title: `${match.players.find(player => player.seat === match.turn)?.displayName ?? 'Another player'}’s turn`,
    message: 'Play moves clockwise, one card each. Watch which suit starts the trick—you’ll follow it if you can.',
    why: 'The 2 of clubs starts the hand. After that, whoever wins a trick leads the next one. The highest card of the suit led wins; other suits cannot beat it.', suggestedCodes: [],
  }

  const legal = match.legalCards.filter(card => match.hand.some(own => own.code === card.code))
  if (!legal.length) return {
    kind: 'waiting', title: 'One moment…', message: 'Your next legal play will appear here.', suggestedCodes: [],
  }
  const lead = match.currentTrick.plays[0]?.card
  const lowest = (cards: readonly HeartsCard[]) => [...cards].sort((a, b) => rank(a) - rank(b) || a.code.localeCompare(b.code))[0]
  if (!lead) return {
    kind: 'play', title: 'You lead—set the pace!',
    message: match.completedTricks.length === 0
      ? 'The 2 of clubs kicks things off. Tap the marked card; then play moves clockwise.'
      : 'Try leading with this low card. Everyone else must follow its suit if they can.',
    why: match.completedTricks.length === 0
      ? 'Whoever holds the 2 of clubs plays first, even after passing. Point cards are restricted on this first trick when another legal choice is available.'
      : lowest(legal).suit === 'hearts'
        ? 'Hearts can lead once they’re broken, or if hearts are all you have left. Your lowest heart gives you the best chance to stay under the others.'
        : 'Leading low often helps you dodge the trick. Save some low cards for later, when hearts or the queen of spades might land on the table.',
    suggestedCodes: [lowest(legal).code],
  }

  const following = legal.filter(card => card.suit === lead.suit)
  if (!following.length) {
    const discard = [...legal].sort((a, b) => discardRisk(b) - discardRisk(a) || a.code.localeCompare(b.code))[0]
    return {
      kind: 'play', title: `No ${lead.suit}? Pick another suit!`,
      message: pointValue(discard) > 0
        ? 'You cannot follow suit. Try slipping this point card into someone else’s trick.'
        : 'You cannot follow suit. This is a handy chance to shed a high card.',
      why: pointValue(discard) === 13
        ? 'That queen of spades packs 13 points! An off-suit queen cannot win this trick, so the player winning the led suit takes those points.'
        : pointValue(discard) > 0
          ? 'Each heart is 1 point. An off-suit heart cannot win the trick; its points go to whoever wins the suit that was led.'
          : 'Off-suit cards cannot win the trick. Shedding a high card now may stop it catching unwanted points later. On the first trick, point-card discards may be restricted.',
      suggestedCodes: [discard.code],
    }
  }
  const winningRank = Math.max(...match.currentTrick.plays.filter(play => play.card.suit === lead.suit).map(play => rank(play.card)))
  const below = following.filter(card => rank(card) < winningRank).sort((a, b) => rank(b) - rank(a))
  const suggestion = below[0] ?? lowest(following)
  return {
    kind: 'play', title: `${suitName(lead.suit)} it is—follow along!`,
    message: below.length
      ? 'Try this card: it follows suit but stays below the current highest card.'
      : 'Follow the suit led with this low card. You might still win the trick—and that’s okay.',
    why: below.length
      ? 'Use a higher card that still loses when you can. That saves your tiny cards for ducking under future tricks with hearts or the queen of spades.'
      : 'You must follow suit if you can. The highest card in that suit takes all four cards and their points, then leads again. Winning a point-free trick doesn’t hurt your score.',
    suggestedCodes: [suggestion.code],
  }
}

function rank(card: HeartsCard): number {
  const names: Record<string, number> = { ace: 14, a: 14, king: 13, k: 13, queen: 12, q: 12, jack: 11, j: 11 }
  const value = card.rank.toLowerCase()
  return names[value] ?? Number(value)
}
function pointValue(card: HeartsCard) { return card.suit === 'hearts' ? 1 : card.suit === 'spades' && rank(card) === 12 ? 13 : 0 }
function passRisk(card: HeartsCard) {
  const value = rank(card)
  if (card.suit === 'spades' && value === 12) return 1000
  if (card.suit === 'spades' && value > 12) return 800 + value
  if (card.suit === 'hearts' && value >= 10) return 600 + value
  if (value >= 11) return 250 + value
  if (card.suit === 'hearts' && value >= 7) return 180 + value
  if (value >= 10) return 150 + value
  return value
}
function discardRisk(card: HeartsCard) { return pointValue(card) * 100 + rank(card) }
function passDirection(value: HeartsMatch['passDirection']) { return value === 'across' ? 'across the table' : `to the ${value}` }
function suitName(suit: string) { return suit[0]?.toUpperCase() + suit.slice(1) }
function passExplanation(cards: readonly HeartsCard[]) {
  if (cards.some(card => pointValue(card) === 13)) return 'That queen of spades packs 13 points! Passing it can lighten your load. Low cards are worth keeping for ducking under dangerous tricks later.'
  if (cards.some(card => card.suit === 'spades' && rank(card) > 12)) return 'Big spades can leave you catching the queen and her 13 points. Smaller spades help you duck under it, so they’re often worth saving.'
  if (cards.some(card => card.suit === 'hearts')) return 'Hearts add points, and high ones can win unwanted tricks. Try passing the big risks while keeping low cards to slip underneath future plays.'
  return 'High cards often win tricks, and those tricks may carry points. Passing some high cards and saving low ones can help you stay out of trouble.'
}
