import type { SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotSymbolGuideEntry } from '../../../features/slots/config/symbolSets'
import contracts from './engineGuideContracts'

export type SlotEngineGuideContract = {
  gameId: string
  payoutMultiplier: number
  pointValueInCents: number
  paylinePatternIds: number[]
  scatterRequiredSymbols: number
  scatterAwardedSpins: number
  collectionTarget: number
  collectionAwardedSpins: number
  usesCollections: boolean
  usesEnergy: boolean
  usesDirectValueTokens: boolean
  scatterFeatureMode: string | null
  collectionFeatureMode: string | null
}

// Exported from the existing engine provider; presentation metadata only.
// The contract test compares this snapshot with an independent engine export.
export const SLOT_ENGINE_GUIDE_CONTRACTS = contracts.games as SlotEngineGuideContract[]

const collectionModes = ['sync', 'rows', 'paw', 'rand'] as const
const sealSymbols = ['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const
const basePayouts = contracts.basePayouts as Record<string, { three: number; five: number }>

export function getSlotEngineGuideContract(gameId: string): SlotEngineGuideContract {
  const contract = SLOT_ENGINE_GUIDE_CONTRACTS.find(candidate => candidate.gameId === gameId)
  if (!contract) throw new Error(`Missing audited slot guide contract: ${gameId}`)
  return contract
}

export function formatCalibratedMultiplier(base: number, calibration: number): string {
  return `${Number((base * calibration).toFixed(6))}×`
}

function modeDescription(mode: string, usesDirectValueTokens: boolean): string {
  return mode.split('-').map(part => ({
    sync: 'linked reels',
    rows: 'two extra rows',
    paw: usesDirectValueTokens ? 'a boosted collector chance' : 'extra wilds',
    rand: usesDirectValueTokens ? 'a prize column' : 'a wild reel',
  })[part] ?? '').filter(Boolean).join(', ')
}

function collectionReward(contract: SlotEngineGuideContract, mode: string): string {
  const powers = modeDescription(contract.collectionFeatureMode ?? mode, contract.usesDirectValueTokens)
  return `${contract.collectionAwardedSpins} enhanced bonus rounds with ${powers}.`
}

/** Corrects visible instructions without changing rules, requests or results. */
export function withEngineGuide(experience: SlotExperienceSet): SlotExperienceSet {
  // These games have independent, actively maintained presentation contracts.
  if (experience.rules.gameId === 'rainbow-realm-fruits-v1' ||
      experience.rules.gameId === 'dragon-hoard-v1') return experience

  const contract = getSlotEngineGuideContract(experience.rules.gameId)
  const multiplier = (value: number) => formatCalibratedMultiplier(value, contract.payoutMultiplier)
  const symbols = experience.symbols
  const lightningCollector = experience.features.lightningCollection?.collectorSymbol
  const lineEntries = new Set(Object.keys(basePayouts))
  const guideEntries = symbols.guideEntries.flatMap((entry): SlotSymbolGuideEntry[] => {
    if (entry.symbol === 'BOLT' && !contract.usesEnergy && entry.symbol !== lightningCollector ||
        entry.symbol === 'PAW' && !contract.usesDirectValueTokens ||
        entry.symbol.startsWith('RAND_') && !contract.usesDirectValueTokens ||
        sealSymbols.includes(entry.symbol as typeof sealSymbols[number]) && !contract.usesCollections ||
        entry.symbol === 'FREE' && contract.scatterAwardedSpins === 0) return []
    if (lineEntries.has(entry.symbol)) {
      const payouts = basePayouts[entry.symbol]
      return [{ ...entry, firstLabel: entry.symbol === 'ACE' ? '3' : '3–4',
        firstValue: multiplier(payouts.three), secondLabel: '5', secondValue: multiplier(payouts.five) }]
    }
    if (entry.symbol === 'BANANA') return [{ ...entry, secondValue: multiplier(3) }]
    if (entry.symbol === 'FREE') return [{ ...entry, firstLabel: `${contract.scatterRequiredSymbols}+`,
      secondValue: `${contract.scatterAwardedSpins} bonus rounds` }]
    if (sealSymbols.includes(entry.symbol as typeof sealSymbols[number])) {
      // Pirates uses a variable trigger chance; its existing guide explains that.
      if (contract.gameId === 'pirates-fortune-v1') return [entry]
      return [{ ...entry, firstValue: `collect ${contract.collectionTarget}`,
        secondValue: `${contract.collectionAwardedSpins} enhanced rounds` }]
    }
    return [entry]
  })

  const energyNote = contract.usesEnergy
    ? ' Energy at each quarter meter improves collection odds. A full meter boosts the next paying spin by 1.5×, then resets and completes one collection.'
    : ''
  const collectionSections = new Set(['SET', 'ORBIT', 'GODS', 'SUITS', 'TACKLE', 'RUNES', 'FOSSIL', 'SEAL'])
  const tripleSymbol = symbols.definitions.BANANA?.label.toLowerCase() ?? 'triple bonus'
  const extraSections = experience.help.extraSections?.map(section => {
    if (section.badge === 'GRAB' && !contract.usesDirectValueTokens) return {
      ...section, badge: 'TRIPLE', title: symbols.definitions.BANANA?.label ?? 'Triple bonus',
      body: `Three ${tripleSymbol} symbols in a row, column, or diagonal pay ${multiplier(3)} the wager per qualifying pattern.`,
    }
    if (collectionSections.has(section.badge) && contract.usesCollections && contract.gameId !== 'pirates-fortune-v1') {
      const item = experience.features.collections?.itemLabel ?? 'matching symbols'
      const reward = contract.collectionFeatureMode
        ? `Every completed color awards the same feature: ${modeDescription(contract.collectionFeatureMode, contract.usesDirectValueTokens)}.`
        : 'Each color activates its matching collection power.'
      return { ...section, body: `Collect ${contract.collectionTarget} ${item} on any one track to unlock ${contract.collectionAwardedSpins} enhanced bonus rounds. ${reward} Bonus rounds use the locked wager shown for the feature.${energyNote}` }
    }
    return { ...section, body: section.body.replace(/pay 3× the wager/g, `pay ${multiplier(3)} the wager`) }
  })

  const collectionFeature = experience.features.collections
  const collections = collectionFeature && contract.usesCollections ? {
    ...collectionFeature,
    entries: collectionFeature.entries.map((entry, index) => ({
      ...entry, requiredCount: contract.collectionTarget,
      ...(contract.gameId !== 'pirates-fortune-v1' && contract.gameId !== 'wukong-journey-v1' ? {
        rewardDescription: `Collect ${contract.collectionTarget} to unlock ${collectionReward(contract, collectionModes[index])}`,
        progressDescription: `Land matching ${collectionFeature.itemLabel ?? 'symbols'} anywhere on the reels.`,
      } : {}),
    })),
  } : undefined
  const wild = symbols.definitions.ACE?.label ?? 'Wild symbols'
  const power = symbols.definitions.POWER?.label ?? 'Power symbols'
  const point = `R${(contract.pointValueInCents / 100).toFixed(2)}`
  const calibrationNote = contract.usesDirectValueTokens
    ? ` Collector token face values are totaled, doubled for two or more collectors, rounded to a point, then multiplied by ${contract.payoutMultiplier} and rounded again.`
    : ''

  return {
    ...experience,
    features: { ...experience.features, collections,
      energy: contract.usesEnergy ? experience.features.energy : undefined,
      moneyGrab: contract.usesDirectValueTokens ? experience.features.moneyGrab : undefined },
    help: {
      ...experience.help, paylineCount: contract.paylinePatternIds.length,
      paylinePatternIds: contract.paylinePatternIds,
      freeGames: contract.scatterAwardedSpins > 0 ? {
        ...experience.help.freeGames, requiredSymbols: contract.scatterRequiredSymbols,
        awardedSpins: contract.scatterAwardedSpins,
      } : undefined,
      fiveMatchPayoutNote: `All ${contract.paylinePatternIds.length} full payline patterns use the same symbol multipliers.`,
      fourMatchPayoutNote: `Ordinary four-symbol runs at either edge pay the three-symbol rate. Four ${wild}, or four-symbol runs using wild substitution, do not pay.`,
      extraSections,
    },
    symbols: {
      ...symbols, guideEntries,
      guideNote: `Multipliers apply to the wager and include this game's payout calibration. Each paying match is rounded to the nearest point (${point}), with half points rounded up. ${wild} pay naturally on three or five symbols and substitute only on five-symbol paylines. Natural three-symbol ${power} wins also award 1 special point; natural five-symbol wins award 2. Four-symbol wins award no special points.${calibrationNote}`,
    },
  }
}
