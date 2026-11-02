import { createContext, useContext, type ReactNode } from 'react'

export type GameExperienceCoverage = 'complete' | 'partial' | 'unavailable'
type ExperienceFields = Readonly<{
  gameId: string | null
  userId: string | null
  provenance: readonly string[]
  coverage: GameExperienceCoverage
  firstPlayedAtUtc: string | null
  error: string | null
  refresh: () => void
}>

/** Unknown is intentionally distinct from a proven first-time player. */
export type GameExperience = ExperienceFields & (
  | Readonly<{ status: 'known'; hasPlayed: boolean }>
  | Readonly<{ status: 'loading' | 'unknown' | 'error' | 'guest' | 'unavailable'; hasPlayed: null }>
)

const unavailable: GameExperience = Object.freeze({
  status: 'unavailable', hasPlayed: null, gameId: null, userId: null,
  provenance: [], coverage: 'unavailable', firstPlayedAtUtc: null, error: null, refresh: () => {},
})
const Context = createContext<GameExperience>(unavailable)

/** Shared data only. A game decides separately whether it has a tutorial. */
export function GameExperienceProvider({ value, children }: { value: GameExperience; children: ReactNode }) {
  return <Context.Provider value={value}>{children}</Context.Provider>
}

export function useGameExperience(): GameExperience { return useContext(Context) }
