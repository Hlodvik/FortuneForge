import asianDragonSource from '../../assets/slots/ambient/asian-dragon.mp3'
import carnivalLightsSource from '../../assets/slots/ambient/carnival-lights.mp3'
import casinoRockSource from '../../assets/slots/ambient/casino-rock.mp3'
import countryTrailSource from '../../assets/slots/ambient/country-trail.mp3'
import cyberNightSource from '../../assets/slots/ambient/cyber-night.mp3'
import forestJourneySource from '../../assets/slots/ambient/forest-journey.mp3'
import hauntedChimesSource from '../../assets/slots/ambient/haunted-chimes.mp3'
import nordicAtmosphereSource from '../../assets/slots/ambient/nordic-atmosphere.mp3'

export const AMBIENT_GAME_MUSIC = {
  asteroids: { source: cyberNightSource, volume: 0.04 },
  flappy: { source: forestJourneySource, volume: 0.035 },
  'horse-flight': { source: countryTrailSource, volume: 0.035 },
  snake: { source: nordicAtmosphereSource, volume: 0.035 },
  '2048': { source: casinoRockSource, volume: 0.03 },
  'drop-merge': { source: carnivalLightsSource, volume: 0.035 },
  craps: { source: casinoRockSource, volume: 0.035 },
  'liars-dice': { source: hauntedChimesSource, volume: 0.028 },
  'sic-bo': { source: asianDragonSource, volume: 0.035 },
} as const

export type AmbientGameId = keyof typeof AMBIENT_GAME_MUSIC
