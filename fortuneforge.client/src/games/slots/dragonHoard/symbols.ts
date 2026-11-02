const ARTWORK_BASE = '/assets/slots/midnight-vault/v1'
const SYMBOL_ARTWORK_BASE = '/assets/slots/midnight-vault/v4'

const symbolSlugs = {
  '2': 'ornate-q', '3': 'ornate-j', '4': 'ornate-k', '5': 'ornate-a',
  '6': 'overflowing-chips', '7': 'ruby-ring', '8': 'cash-briefcase',
  ACE: 'front-vault', FREE: 'faberge-egg', POWER: 'loose-diamond', BANANA: 'triple-sevens',
  // Dormant shared metadata reuses approved art; these roles stay disabled.
  BOLT: 'front-vault', PAW: 'cash-briefcase',
  SEAL_SYNC: 'ruby-access', SEAL_ROWS: 'sapphire-access', SEAL_PAW: 'amber-access',
  SEAL_RAND: 'emerald-access', VALUE: 'overflowing-chips',
} as const

function symbolImage(slug: string, size: 96 | 192 | 320): string {
  return `${SYMBOL_ARTWORK_BASE}/symbols/${size}/midnight-vault-${slug}.webp`
}

// URL metadata is inert: browsers request the selected candidate, not every variant.
export const DRAGON_HOARD_SYMBOL_IMAGES = {
  ...Object.fromEntries(Object.entries(symbolSlugs).map(([id, slug]) => [id, symbolImage(slug, 96)])) as
    Record<keyof typeof symbolSlugs, string>,
  CABINET_EMBLEM: `${ARTWORK_BASE}/presentation/midnight-vault-cabinet-emblem-96.webp`,
  BACKDROP: `${ARTWORK_BASE}/presentation/midnight-vault-backdrop-1440x810.webp`,
} as const

export const DRAGON_HOARD_RESPONSIVE_IMAGES: Readonly<Record<string, Readonly<Record<96 | 192 | 320, string>>>> = Object.fromEntries(
  Object.values(symbolSlugs).map((slug) => [
    symbolImage(slug, 96),
    { 96: symbolImage(slug, 96), 192: symbolImage(slug, 192), 320: symbolImage(slug, 320) },
  ]),
)
