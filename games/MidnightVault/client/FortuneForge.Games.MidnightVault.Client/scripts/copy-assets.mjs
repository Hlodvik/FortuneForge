import { cpSync, mkdirSync } from 'node:fs'

mkdirSync(new URL('../dist/', import.meta.url), { recursive: true })
for (const path of ['assets', 'midnightVaultCelebration.css', 'midnightVaultLayout.css', 'midnightWinningPaylines.css', 'styles.css']) {
  cpSync(new URL(`../src/${path}`, import.meta.url), new URL(`../dist/${path}`, import.meta.url), { recursive: true })
}
