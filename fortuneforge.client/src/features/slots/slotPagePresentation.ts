import type { SlotSymbolId } from './types/slots'

export const creditFormatter = new Intl.NumberFormat('en-US')

export const formatRand = (amount: number) => `R${creditFormatter.format(amount)}`

export const sealLabels: Readonly<
  Record<string, { label: string; shortLabel: string; symbol: SlotSymbolId }>
> = {
  sync: { label: 'Sync reels', shortLabel: 'Sync', symbol: 'SEAL_SYNC' },
  rows: { label: '+2 rows', shortLabel: '+2 rows', symbol: 'SEAL_ROWS' },
  paw: { label: 'Monkey paw odds', shortLabel: 'Paws', symbol: 'SEAL_PAW' },
  rand: { label: 'Rand column', shortLabel: 'Rand', symbol: 'SEAL_RAND' },
}
