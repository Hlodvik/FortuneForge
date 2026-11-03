import { CLASSIC_SLOT_SYMBOL_SET_ID } from '../../../features/slots/config/symbolSets'
import ambrosiaFlame from '../../../assets/slots/games/gods-of-olympus/optimized/ambrosia-flame.png'
import amphora from '../../../assets/slots/games/gods-of-olympus/optimized/amphora.png'
import aresHelmetMedallion from '../../../assets/slots/games/gods-of-olympus/optimized/ares-helmet-medallion.png'
import athenaOwlMedallion from '../../../assets/slots/games/gods-of-olympus/optimized/athena-owl-medallion.png'
import drachmaToken from '../../../assets/slots/games/gods-of-olympus/optimized/drachma-token.png'
import hermesWingMedallion from '../../../assets/slots/games/gods-of-olympus/optimized/hermes-wing-medallion.png'
import lightningBolt from '../../../assets/slots/games/gods-of-olympus/optimized/lightning-bolt.png'
import lightningVolley from '../../../assets/slots/games/gods-of-olympus/optimized/lightning-volley.png'
import olympusGates from '../../../assets/slots/games/gods-of-olympus/optimized/olympus-gates.png'
import amethyst from '../../../assets/slots/games/gods-of-olympus/optimized/olympian-amethyst.png'
import citrine from '../../../assets/slots/games/gods-of-olympus/optimized/olympian-citrine.png'
import emerald from '../../../assets/slots/games/gods-of-olympus/optimized/olympian-emerald.png'
import ruby from '../../../assets/slots/games/gods-of-olympus/optimized/olympian-ruby.png'
import sapphire from '../../../assets/slots/games/gods-of-olympus/optimized/olympian-sapphire.png'
import poseidonTrident from '../../../assets/slots/games/gods-of-olympus/optimized/poseidon-trident.png'
import poseidonWaveMedallion from '../../../assets/slots/games/gods-of-olympus/optimized/poseidon-wave-medallion.png'
import thunderboltShield from '../../../assets/slots/games/gods-of-olympus/optimized/thunderbolt-shield.png'
import wingedSandal from '../../../assets/slots/games/gods-of-olympus/optimized/winged-sandal.png'
import zeusEagleCrest from '../../../assets/slots/games/gods-of-olympus/optimized/zeus-eagle-crest.png'
import zeusGauntlet from '../../../assets/slots/games/gods-of-olympus/optimized/zeus-gauntlet.png'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const GODS_OF_OLYMPUS_SYMBOLS = createThemedSymbolSet({
  id: 'gods-of-olympus-symbols-v1',
  serverSymbolSetId: CLASSIC_SLOT_SYMBOL_SET_ID,
  symbols: {
    '2': { label: 'Olympian amphora', image: amphora },
    '3': { label: 'Olympian emerald', image: emerald },
    '4': { label: 'Winged sandal of Hermes', image: wingedSandal },
    '5': { label: 'Olympian ruby', image: ruby },
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
  additionalSymbols: {
    '8': { label: 'Olympian sapphire', image: sapphire },
    A: { label: 'Olympian amethyst', image: amethyst },
    J: { label: 'Olympian citrine', image: citrine },
  },
  valueToken: { label: 'drachma', image: drachmaToken },
  energyEarnLabel: '+1 divine favor',
  collectorFirstValue: 'claims drachmas',
  collectorSecondValue: 'double tribute',
  freeRequiredLabel: '4+',
  freeAwardLabel: '6 Olympian Trials',
  collectionRequiredCount: 28,
  collectionAwardLabels: {
    SEAL_SYNC: '6 Athena spins',
    SEAL_ROWS: '6 Poseidon spins',
    SEAL_PAW: '6 Ares spins',
    SEAL_RAND: '6 Hermes spins',
  },
})
