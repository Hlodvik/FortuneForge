export type MascotPhase =
  | 'idle'
  | 'performing'
  | 'success-returning'
  | 'celebrating'
  | 'returning'
  | 'defeated'

export type MascotSet = {
  id: string
  name: string
  assets: {
    platform?: {
      kind: 'cloud'
      animated: string
      reducedMotion: string
    }
    idle: string
    action: string
    backflip: string
    successPoses: readonly string[]
  }
  timing: {
    successDurationMs: number
    returnDurationMs: number
    defeatDurationMs: number
    successTimeline: readonly number[]
  }
}
