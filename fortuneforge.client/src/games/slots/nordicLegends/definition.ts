import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const NORDIC_LEGENDS_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'nordic-legends', title: 'Nordic Legends', subtitle: 'Claim the Halls of Valor',
  description: 'Sail beneath the aurora toward rune-carved peaks, thunder gods, and a warrior’s hoard.',
  serverGameId: 'nordic-legends-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 22, 23],
  presentation: 'divine-offering', collectionAriaLabel: 'Runestone offering collections', itemLabel: 'sacred runes',
  energyLabel: 'Thunder charge', actorName: 'The Valkyrie treasure chest', awardLabel: 'Valhalla war chest',
  valueToken: ['stamped silver rune', '🪙'], motif: '⚔️', accentGlyph: '❄️',
  collectionLabels: [['Crimson wolf rune', '🐺'], ['Sapphire wave rune', '🌊'], ['Amber hammer rune', '🔨'], ['Emerald world-tree rune', '🌲']],
  symbolSpecs: {
    '2': ['Carved runestone', '🪨'], '3': ['Feast-hall drinking horn', '🍺'], '4': ['Viking round shield', '🛡️'],
    '5': ['Battle axe', '🪓'], '6': ['Dragon-prow longship', '⛵'], '7': ['Winged Valkyrie', '🪽'],
    ACE: ['All-seeing Odin wild', '👁️'], FREE: ['Bifrost bridge free game', '🌈'], POWER: ['Mjolnir thunder power', '🔨'],
    BOLT: ['Aurora lightning charge', '⚡'], BANANA: ['Triple battle axes', '🪓'], PAW: ['Valkyrie treasure chest', '🧰'],
  },
  colors: { skyTop: '#102d46', skyBottom: '#3b6a78', horizon: '#274858', ground: '#091a27', primary: '#477b8e', secondary: '#80dfca', deep: '#102535', rim: '#d5b766', glow: '#8fffe2', text: '#f0fbff' },
}
