import type { SlotResponsiveImages } from '@fortuneforge/slot-client-core'

import q96 from './assets/symbols/96/midnight-vault-ornate-q.webp'
import j96 from './assets/symbols/96/midnight-vault-ornate-j.webp'
import k96 from './assets/symbols/96/midnight-vault-ornate-k.webp'
import a96 from './assets/symbols/96/midnight-vault-ornate-a.webp'
import chips96 from './assets/symbols/96/midnight-vault-overflowing-chips.webp'
import ring96 from './assets/symbols/96/midnight-vault-ruby-ring.webp'
import case96 from './assets/symbols/96/midnight-vault-cash-briefcase.webp'
import wild96 from './assets/symbols/96/midnight-vault-front-vault.webp'
import bonus96 from './assets/symbols/96/midnight-vault-faberge-egg.webp'
import diamond96 from './assets/symbols/96/midnight-vault-loose-diamond.webp'
import sevens96 from './assets/symbols/96/midnight-vault-triple-sevens.webp'
import ruby96 from './assets/symbols/96/midnight-vault-ruby-access.webp'
import sapphire96 from './assets/symbols/96/midnight-vault-sapphire-access.webp'
import amber96 from './assets/symbols/96/midnight-vault-amber-access.webp'
import emerald96 from './assets/symbols/96/midnight-vault-emerald-access.webp'

import q192 from './assets/symbols/192/midnight-vault-ornate-q.webp'
import j192 from './assets/symbols/192/midnight-vault-ornate-j.webp'
import k192 from './assets/symbols/192/midnight-vault-ornate-k.webp'
import a192 from './assets/symbols/192/midnight-vault-ornate-a.webp'
import chips192 from './assets/symbols/192/midnight-vault-overflowing-chips.webp'
import ring192 from './assets/symbols/192/midnight-vault-ruby-ring.webp'
import case192 from './assets/symbols/192/midnight-vault-cash-briefcase.webp'
import wild192 from './assets/symbols/192/midnight-vault-front-vault.webp'
import bonus192 from './assets/symbols/192/midnight-vault-faberge-egg.webp'
import diamond192 from './assets/symbols/192/midnight-vault-loose-diamond.webp'
import sevens192 from './assets/symbols/192/midnight-vault-triple-sevens.webp'
import ruby192 from './assets/symbols/192/midnight-vault-ruby-access.webp'
import sapphire192 from './assets/symbols/192/midnight-vault-sapphire-access.webp'
import amber192 from './assets/symbols/192/midnight-vault-amber-access.webp'
import emerald192 from './assets/symbols/192/midnight-vault-emerald-access.webp'

import q320 from './assets/symbols/320/midnight-vault-ornate-q.webp'
import j320 from './assets/symbols/320/midnight-vault-ornate-j.webp'
import k320 from './assets/symbols/320/midnight-vault-ornate-k.webp'
import a320 from './assets/symbols/320/midnight-vault-ornate-a.webp'
import chips320 from './assets/symbols/320/midnight-vault-overflowing-chips.webp'
import ring320 from './assets/symbols/320/midnight-vault-ruby-ring.webp'
import case320 from './assets/symbols/320/midnight-vault-cash-briefcase.webp'
import wild320 from './assets/symbols/320/midnight-vault-front-vault.webp'
import bonus320 from './assets/symbols/320/midnight-vault-faberge-egg.webp'
import diamond320 from './assets/symbols/320/midnight-vault-loose-diamond.webp'
import sevens320 from './assets/symbols/320/midnight-vault-triple-sevens.webp'
import ruby320 from './assets/symbols/320/midnight-vault-ruby-access.webp'
import sapphire320 from './assets/symbols/320/midnight-vault-sapphire-access.webp'
import amber320 from './assets/symbols/320/midnight-vault-amber-access.webp'
import emerald320 from './assets/symbols/320/midnight-vault-emerald-access.webp'

import backdrop1440 from './assets/presentation/midnight-vault-backdrop-1440x810.webp'
import backdrop1672 from './assets/presentation/midnight-vault-backdrop-1672x941.webp'
import emblem from './assets/presentation/midnight-vault-cabinet-emblem-96.webp'
import wordmark384 from './assets/presentation/midnight-vault-wordmark-384.webp'
import wordmark768 from './assets/presentation/midnight-vault-wordmark-768.webp'
import wordmark1024 from './assets/presentation/midnight-vault-wordmark-1024.webp'

const responsive = (small: string, medium: string, large: string): SlotResponsiveImages =>
  ({ 96: small, 192: medium, 320: large })

export const MIDNIGHT_IMAGES = {
  '2': q96, '3': j96, '4': k96, '5': a96, '6': chips96, '7': ring96, '8': case96,
  ACE: wild96, FREE: bonus96, POWER: diamond96, BANANA: sevens96,
  SEAL_SYNC: ruby96, SEAL_ROWS: sapphire96, SEAL_PAW: amber96, SEAL_RAND: emerald96,
  BACKDROP: backdrop1440, BACKDROP_LARGE: backdrop1672, CABINET_EMBLEM: emblem,
  WORDMARK: wordmark384, WORDMARK_768: wordmark768, WORDMARK_1024: wordmark1024,
} as const

export const MIDNIGHT_RESPONSIVE_IMAGES = {
  '2': responsive(q96, q192, q320), '3': responsive(j96, j192, j320),
  '4': responsive(k96, k192, k320), '5': responsive(a96, a192, a320),
  '6': responsive(chips96, chips192, chips320), '7': responsive(ring96, ring192, ring320),
  '8': responsive(case96, case192, case320), ACE: responsive(wild96, wild192, wild320),
  FREE: responsive(bonus96, bonus192, bonus320), POWER: responsive(diamond96, diamond192, diamond320),
  BANANA: responsive(sevens96, sevens192, sevens320),
  SEAL_SYNC: responsive(ruby96, ruby192, ruby320), SEAL_ROWS: responsive(sapphire96, sapphire192, sapphire320),
  SEAL_PAW: responsive(amber96, amber192, amber320), SEAL_RAND: responsive(emerald96, emerald192, emerald320),
} as const

export const MIDNIGHT_WORDMARK_SRC_SET = `${wordmark384} 384w, ${wordmark768} 768w, ${wordmark1024} 1024w`
export const MIDNIGHT_BACKDROP_IMAGE_SET = `image-set(url("${backdrop1440}") 1x, url("${backdrop1672}") ${1672 / 1440}x)`
