import ambrosiaFlame from '../../../assets/slots/games/gods-of-olympus/ambrosia-flame.png'
import amphora from '../../../assets/slots/games/gods-of-olympus/amphora.png'
import aresHelmetMedallion from '../../../assets/slots/games/gods-of-olympus/ares-helmet-medallion.png'
import athenaOwlMedallion from '../../../assets/slots/games/gods-of-olympus/athena-owl-medallion.png'
import drachmaToken from '../../../assets/slots/games/gods-of-olympus/drachma-token.png'
import goldenFleece from '../../../assets/slots/games/gods-of-olympus/golden-fleece.png'
import hermesWingMedallion from '../../../assets/slots/games/gods-of-olympus/hermes-wing-medallion.png'
import laurelWreath from '../../../assets/slots/games/gods-of-olympus/laurel-wreath.png'
import lightningBolt from '../../../assets/slots/games/gods-of-olympus/lightning-bolt.png'
import lightningVolley from '../../../assets/slots/games/gods-of-olympus/lightning-volley.png'
import olympusGates from '../../../assets/slots/games/gods-of-olympus/olympus-gates.png'
import poseidonTrident from '../../../assets/slots/games/gods-of-olympus/poseidon-trident.png'
import poseidonWaveMedallion from '../../../assets/slots/games/gods-of-olympus/poseidon-wave-medallion.png'
import thunderboltShield from '../../../assets/slots/games/gods-of-olympus/thunderbolt-shield.png'
import wingedSandal from '../../../assets/slots/games/gods-of-olympus/winged-sandal.png'
import zeusEagleCrest from '../../../assets/slots/games/gods-of-olympus/zeus-eagle-crest.png'
import zeusGauntlet from '../../../assets/slots/games/gods-of-olympus/zeus-gauntlet.png'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const GODS_OF_OLYMPUS_SYMBOLS = createThemedSymbolSet({
  id: 'gods-of-olympus-symbols-v1',
  serverSymbolSetId: 'gods-of-olympus-v1-symbols',
  symbols: {
    '2': { label: 'Olympian amphora', image: amphora },
    '3': { label: 'Golden laurel wreath', image: laurelWreath },
    '4': { label: 'Winged sandal of Hermes', image: wingedSandal },
    '5': { label: 'Golden fleece', image: goldenFleece },
    '6': { label: 'Trident of Poseidon', image: poseidonTrident },
    '7': { label: 'Lightning of Zeus', image: lightningBolt },
    ACE: { label: 'Zeus eagle wild crest', image: zeusEagleCrest },
    FREE: { label: 'Gates of Olympus free game', image: olympusGates },
    POWER: { label: 'Thunderbolt power shield', image: thunderboltShield },
    BOLT: { label: 'Ambrosia flame', image: ambrosiaFlame },
    BANANA: { label: 'Lightning volley', image: lightningVolley },
    PAW: { label: 'Gauntlet of Zeus tribute', image: zeusGauntlet },
    SEAL_SYNC: { label: 'Athena strategy medallion', image: athenaOwlMedallion },
    SEAL_ROWS: { label: 'Poseidon tide medallion', image: poseidonWaveMedallion },
    SEAL_PAW: { label: 'Ares fury medallion', image: aresHelmetMedallion },
    SEAL_RAND: { label: 'Hermes fortune medallion', image: hermesWingMedallion },
  },
  valueToken: { label: 'drachma', image: drachmaToken },
  energyEarnLabel: '+1 divine favor',
  collectorFirstValue: 'claims drachmas',
  collectorSecondValue: 'double tribute',
  collectionAwardLabels: {
    SEAL_SYNC: '10 Athena spins',
    SEAL_ROWS: '10 Poseidon spins',
    SEAL_PAW: '10 Ares spins',
    SEAL_RAND: '10 Hermes spins',
  },
})
