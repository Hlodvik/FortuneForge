// Edited: 2026-11-18T18:41:52-06:00 | Computer: Vinnabok
export type AudioCategory = 'effect' | 'result'
export type SlotSoundCueId = string
export type SlotPotionSoundEvent = 'bottling' | 'activation'
export type SlotResultSoundEvent = 'bonus' | 'five' | 'no-win' | 'premium' | 'single-three'
export type SlotSoundCue = {
  source: string
  baseVolume: number
  category: AudioCategory
  loop?: boolean
  gaplessLoop?: boolean
  loopDurationSeconds?: number
  enabled?: boolean
}

export type SlotSoundSet = {
  id: string
  cues: Readonly<Record<SlotSoundCueId, SlotSoundCue | undefined>>
  events: {
    ambience: SlotSoundCueId
    buttonClick?: SlotSoundCueId
    utilityButtonClick?: SlotSoundCueId
    spinButtonClick?: SlotSoundCueId
    stopButtonClick?: SlotSoundCueId
    autoSpinAmbience?: SlotSoundCueId
    leverPull: SlotSoundCueId
    reelSpin: SlotSoundCueId
    reelStop: SlotSoundCueId
    specialReelSpin?: SlotSoundCueId
    specialReelStop?: SlotSoundCueId
    specialAmbience?: SlotSoundCueId
    specialCollect?: SlotSoundCueId
    /** Explicit opt-in; legacy/excluded cabinets retain their event behavior. */
    themeSuite?: true
    betStep?: SlotSoundCueId
    arcadeExpansion?: SlotSoundCueId
    collectionArrival?: Readonly<Partial<Record<'sync' | 'rows' | 'paw' | 'rand', SlotSoundCueId>>>
    collectorWin?: SlotSoundCueId
    potion?: Readonly<Record<SlotPotionSoundEvent, readonly SlotSoundCueId[]>>
    tripleSevens?: SlotSoundCueId
    bonusComplete?: SlotSoundCueId
    results: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
    specialResults?: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
  }
}

