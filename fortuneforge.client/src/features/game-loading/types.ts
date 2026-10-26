/** Lightweight presentation data; never import game packages into a loading view. */
export interface GameLoadingEntry {
  readonly id: string
  readonly title: string
  readonly routes: readonly string[]
  readonly image: string
  readonly background: string
  readonly accent: string
  readonly objectPosition?: string
}
