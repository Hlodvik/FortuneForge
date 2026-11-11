import image from '../../../assets/catalogue/slots/midnight-vault-catalogue-title-768x384.webp'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

// Lobby-only metadata and card art: opening the lobby never loads the game package.
export const MIDNIGHT_VAULT_CATALOG: SlotGameCatalogDefinition = {
  id: 'dragon-hoard', title: 'Midnight Vault', shortTitle: 'Midnight Vault',
  description: 'An Art Deco heist with brilliant jewels and the Vault Breach bonus.',
  image, imagePresentation: 'contain',
}
