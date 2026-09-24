import cyberNightSource from '../../assets/slots/ambient/cyber-night.mp3'
import forestJourneySource from '../../assets/slots/ambient/forest-journey.mp3'
import hauntedChimesSource from '../../assets/slots/ambient/haunted-chimes.mp3'

export const AMBIENT_GAME_MUSIC = {
  asteroids: { source: cyberNightSource, volume: 0.04 },
  flappy: { source: forestJourneySource, volume: 0.035 },
  'liars-dice': { source: hauntedChimesSource, volume: 0.028 },
} as const

export type AmbientGameId = keyof typeof AMBIENT_GAME_MUSIC
