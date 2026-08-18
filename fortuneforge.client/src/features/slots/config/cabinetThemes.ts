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

export type SlotCabinetTheme = {
  id: string
  chrome: 'ornate' | 'simple'
  accessibleName: string
  eyebrow: string
  title: string
  subtitle: string
  emblemImage: string
  accentImage?: string
  backdropImage?: string
  visualsBackdropImage?: string
  pageBackdropImage?: string
  palette: SlotCabinetPalette
}
