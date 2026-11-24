import cabinetGeometry from './assets/frontier/cabinet/cabinet-surround.geometry.json' with { type: 'json' }

export const CABINET_GEOMETRY = cabinetGeometry

export const ART = Object.freeze({
  title: new URL('./assets/frontier/title.webp', import.meta.url).href,
  town: new URL('./assets/frontier/backgrounds/frontier-town.webp', import.meta.url).href,
  cabinet: new URL('./assets/frontier/cabinet/cabinet-surround.webp', import.meta.url).href,
  cardBack: new URL('./assets/frontier/cabinet/poker-card-back.webp', import.meta.url).href,
  symbols: Object.freeze({
    J: new URL('./assets/frontier/symbols/j.webp', import.meta.url).href,
    Q: new URL('./assets/frontier/symbols/q.webp', import.meta.url).href,
    K: new URL('./assets/frontier/symbols/k.webp', import.meta.url).href,
    A: new URL('./assets/frontier/symbols/a.webp', import.meta.url).href,
    SPUR: new URL('./assets/frontier/symbols/spur.webp', import.meta.url).href,
    HAT: new URL('./assets/frontier/symbols/hat.webp', import.meta.url).href,
    POUCH: new URL('./assets/frontier/symbols/pouch.webp', import.meta.url).href,
    REVOLVER: new URL('./assets/frontier/symbols/revolver.webp', import.meta.url).href,
    BADGE: new URL('./assets/frontier/symbols/badge.webp', import.meta.url).href,
    BAR: new URL('./assets/frontier/symbols/bar.webp', import.meta.url).href,
    WILD: new URL('./assets/frontier/symbols/wild.webp', import.meta.url).href,
    WANTED: new URL('./assets/frontier/symbols/wanted.webp', import.meta.url).href,
  }),
})
