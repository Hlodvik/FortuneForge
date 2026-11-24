import blackjack from '../../assets/catalogue/non-slots/blackjack-title-v2.webp'
import texasHoldem from '../../assets/catalogue/non-slots/texas-holdem-title-v2.webp'
import threeCardPoker from '../../assets/catalogue/non-slots/three-card-poker-title-v2.webp'
import houseHoldem from '../../assets/catalogue/non-slots/house-holdem-title-v2.webp'
import baccarat from '../../assets/catalogue/non-slots/baccarat-title-v1.webp'
import roulette from '../../assets/catalogue/non-slots/roulette-title-v3.webp'
import craps from '../../assets/catalogue/non-slots/craps-title-v1.webp'
import dragonTiger from '../../assets/catalogue/non-slots/dragon-tiger-title-v1.webp'
import andarBahar from '../../assets/catalogue/non-slots/andar-bahar-title-v1.webp'
import hiLo from '../../assets/catalogue/non-slots/hi-lo-title-v1.webp'
import casinoWar from '../../assets/catalogue/non-slots/casino-war-title-v1.webp'
import videoPoker from '../../assets/catalogue/non-slots/video-poker-title-v1.webp'
import keno from '../../assets/catalogue/non-slots/keno-title-v1.webp'
import sicBo from '../../assets/catalogue/non-slots/sic-bo-title-v1.webp'
import mines from '../../assets/catalogue/non-slots/mines-title-v2.webp'
import solitaire from '../../assets/catalogue/non-slots/solitaire-title-v2.webp'
import freeSolitaire from '../../assets/catalogue/non-slots/free-solitaire-title-v1.webp'
import hearts from '../../assets/catalogue/non-slots/hearts-title-v2.webp'
import liarsDice from '../../assets/catalogue/non-slots/liars-dice-title-v3.webp'
import twentyFortyEight from '../../assets/catalogue/non-slots/2048-title-v1.webp'
import dropMerge from '../../assets/catalogue/non-slots/drop-merge-title-v1.webp'
import wukong from '../../assets/catalogue/slots/wukong-journey-to-the-west-title-v2.webp'
import rainbowRealm from '../../assets/catalogue/slots/rainbow-realm-title-v2.webp'
import piratesFortune from '../../assets/catalogue/slots/treasure-cove-title-v1.webp'
import godsOfOlympus from '../../assets/catalogue/slots/gods-of-olympus-title-v2.webp'
import reelRiches from '../../assets/catalogue/slots/reel-riches-title-v2.webp'
import frontierFortune from '../../assets/catalogue/slots/frontier-fortune-title.webp'
import { canonicalSlotPath, findSlotRouteMetadata } from '../../games/slots/routeMetadata'
import royalDraw from '../../assets/catalogue/slots/royal-draw-title-v2.webp'
import arcaneArchives from '../../assets/catalogue/slots/arcane-archives-title-v1.webp'
import pharaohsAscent from '../../assets/catalogue/slots/pharaohs-ascent-title-v1.webp'
import goldFoundry from '../../assets/catalogue/slots/gold-foundry-title-v1.webp'
import neonNights from '../../assets/catalogue/slots/neon-nights-title-v1.webp'
import oceanOdyssey from '../../assets/catalogue/slots/ocean-odyssey-title-v1.webp'
import crookedRoad from '../../assets/catalogue/slots/crooked-road-title-v1.webp'
import candyCarnival from '../../assets/catalogue/slots/candy-carnival-title-v2.webp'
import phantomManor from '../../assets/catalogue/slots/phantom-manor-title-v2.webp'
import nordicLegends from '../../assets/catalogue/slots/nordic-legends-title-v2.webp'
import desertTreasures from '../../assets/catalogue/slots/desert-treasures-title-v2.webp'
import sunstoneEclipse from '../../assets/catalogue/slots/sunstone-eclipse-title-v1.webp'
import dragonHoard from '../../assets/catalogue/slots/midnight-vault-catalogue-title-768x384.webp'
import jadeDragon from '../../assets/catalogue/slots/jade-dragon-title-v1.webp'

export type CataloguePresentationArt = Readonly<{
  src: string
  imageSrcSet?: string
  width: 768
  height: 384
}>

const artwork = (src: string, imageSrcSet?: string): CataloguePresentationArt => Object.freeze({ src, width: 768, height: 384, ...(imageSrcSet ? { imageSrcSet } : {}) })

// Presentation data only: importing artwork must never import a playable game or engine.
const artByRoute: ReadonlyMap<string, CataloguePresentationArt> = new Map([
  ['/cards/blackjack', artwork(blackjack)],
  ['/cards/texas-holdem', artwork(texasHoldem)],
  ['/cards/three-card-poker', artwork(threeCardPoker)],
  ['/cards/house-holdem', artwork(houseHoldem)],
  ['/games/baccarat', artwork(baccarat)],
  ['/games/roulette', artwork(roulette)],
  ['/games/craps', artwork(craps)],
  ['/games/dragon-tiger', artwork(dragonTiger)],
  ['/games/andar-bahar', artwork(andarBahar)],
  ['/games/hi-lo', artwork(hiLo)],
  ['/games/casino-war', artwork(casinoWar)],
  ['/games/video-poker', artwork(videoPoker)],
  ['/games/keno', artwork(keno)],
  ['/games/sic-bo', artwork(sicBo)],
  ['/games/mines', artwork(mines)],
  ['/cards/solitaire', artwork(solitaire)],
  ['/cards/free-solitaire', artwork(freeSolitaire)],
  ['/cards/hearts', artwork(hearts)],
  ['/games/liars-dice', artwork(liarsDice)],
  ['/games/2048', artwork(twentyFortyEight)],
  ['/games/drop-merge', artwork(dropMerge)],
  ['/slots/wukong', artwork(wukong)],
  ['/slots/rainbow-realm', artwork(rainbowRealm)],
  ['/slots/pirates-fortune', artwork(piratesFortune)],
  ['/slots/gods-of-olympus', artwork(godsOfOlympus)],
  ['/slots/reel-riches', artwork(reelRiches)],
  ['/slots/frontier-fortune', artwork(frontierFortune)],
  ['/slots/royal-draw', artwork(royalDraw)],
  ['/slots/arcane-archives', artwork(arcaneArchives)],
  ['/slots/pharaohs-ascent', artwork(pharaohsAscent)],
  ['/slots/gold-foundry', artwork(goldFoundry)],
  ['/slots/neon-nights', artwork(neonNights)],
  ['/slots/jade-dragon', artwork(jadeDragon)],
  ['/slots/ocean-odyssey', artwork(oceanOdyssey)],
  ['/slots/crooked-road', artwork(crookedRoad)],
  ['/slots/candy-carnival', artwork(candyCarnival)],
  ['/slots/phantom-manor', artwork(phantomManor)],
  ['/slots/nordic-legends', artwork(nordicLegends)],
  ['/slots/desert-treasures', artwork(desertTreasures)],
  ['/slots/sunstone-eclipse', artwork(sunstoneEclipse)],
  ['/slots/dragon-hoard', artwork(dragonHoard, `/assets/slots/midnight-vault/v1/presentation/midnight-vault-catalogue-title-384x192.webp 384w, ${dragonHoard} 768w, /assets/slots/midnight-vault/v1/presentation/midnight-vault-catalogue-title-1024x512.webp 1024w`)],
])

const cardDemoAliases: ReadonlyMap<string, string> = new Map([
  ['/demo/cards/blackjack', '/cards/blackjack'],
  ['/demo/cards/texas-holdem', '/cards/texas-holdem'],
])

/** Selected title artwork for landing/home cards; unknown routes keep their existing fallback. */
export function getCataloguePresentationArt(route?: string | null): CataloguePresentationArt | undefined {
  if (!route) return undefined
  const pathname = route.split(/[?#]/, 1)[0].replace(/\/+$/, '')
  const slotPath = pathname.startsWith('/slots/') ? canonicalSlotPath(pathname) : null
  const slotRoute = slotPath === null ? null : findSlotRouteMetadata(slotPath)
  const canonical = cardDemoAliases.get(pathname) ?? slotRoute?.playPath ?? slotPath ?? pathname
  return artByRoute.get(canonical)
}
