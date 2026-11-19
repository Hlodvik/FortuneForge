// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import contract from './site-release-contract.json' with { type: 'json' }

// Shared ownership for the site build and Hosting preservation boundary.
export const SITE_ASSET_DIRECTORY = contract.assetDirectory
export const PRESERVED_GAME_DIRECTORY = contract.preservedGameDirectory
export const SITE_GAME_PREFIXES = Object.freeze(contract.gamePrefixes)
export const SITE_OUTPUT_DIRECTORY = contract.outputDirectory
