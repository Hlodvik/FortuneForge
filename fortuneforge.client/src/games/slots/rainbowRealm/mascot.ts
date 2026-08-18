import strawberryMascot from '../../../assets/mascots/strawberry-mascot.png'
import type { MascotSet } from '../shared/mascot/mascotTypes'

const RAINBOW_REALM_MASCOT_TIMING: MascotSet['timing'] = {
  successDurationMs: 900,
  returnDurationMs: 560,
  defeatDurationMs: 1_450,
  successTimeline: [0, 0, 0, 0, 0, 0],
}

export const RAINBOW_REALM_MASCOT: MascotSet = {
  id: 'rainbow-realm-strawberry-companion-v1',
  name: 'Strawberry',
  assets: {
    idle: strawberryMascot,
    action: strawberryMascot,
    backflip: strawberryMascot,
    successPoses: [strawberryMascot],
  },
  timing: RAINBOW_REALM_MASCOT_TIMING,
}
