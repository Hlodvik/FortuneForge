export type SlotCabinetPalette = {
  shellTop: string
  shellBottom: string
  panel: string
  trim: string
  trimBright: string
  accent: string
  glow: string
  text: string
}

export type SlotCelebrationEffect =
  | 'orchard-cascade'
  | 'olympus-storm'
  | 'reel-ripple'
  | 'frontier-dust'
  | 'royal-card-fan'
  | 'arcane-orbit'
  | 'cosmic-streak'
  | 'fossil-burst'
  | 'neon-scan'
  | 'jungle-canopy'
  | 'ocean-swell'
  | 'samurai-blossom'
  | 'candy-sprinkles'
  | 'phantom-mist'
  | 'nordic-aurora'
  | 'desert-sandstorm'
  | 'robot-circuit'
  | 'dragon-embers'

export type SlotTopbarTheme = {
  background: string
  borderColor: string
  shadowColor: string
  accentColor?: string
}

export type SlotCabinetTheme = {
  id: string
  chrome: 'ornate' | 'simple'
  accessibleName: string
  eyebrow: string
  title: string
  subtitle: string
  celebrationEffect?: SlotCelebrationEffect
  emblemImage: string
  accentImage?: string
  backdropImage?: string
  visualsBackdropImage?: string
  pageBackdropImage?: string
  topbar?: SlotTopbarTheme
  palette: SlotCabinetPalette
}
