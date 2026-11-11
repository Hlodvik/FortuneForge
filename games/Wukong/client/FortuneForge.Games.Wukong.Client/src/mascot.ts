import wukongBackflip from './assets/art/mascot/wukong-backflip-staff.png'
import wukongDabUpright from './assets/art/mascot/wukong-dab-on-staff.png'
import wukongIdle from './assets/art/mascot/wukong-idle-staff.png'
import wukongSuccessClap from './assets/art/mascot/wukong-success-clap.png'
import wukongSuccessClap25 from './assets/art/mascot/wukong-success-clap-25.png'
import wukongSuccessClap50 from './assets/art/mascot/wukong-success-clap-50.png'
import wukongSuccessClap75 from './assets/art/mascot/wukong-success-clap-75.png'
import wukongSuccessOpen from './assets/art/mascot/wukong-success-open.png'
import wukongNimbusStatic from './assets/art/symbols/wukong/nimbus-cloud-platform.png'
import type { MascotSet } from '@fortuneforge/slot-game-runtime'

export const WUKONG_MASCOT: MascotSet = {
  id: 'wukong-cloud-companion-v1',
  name: 'Wukong',
  assets: {
    platform: {
      kind: 'cloud',
      animated: wukongNimbusStatic,
      reducedMotion: wukongNimbusStatic,
    },
    idle: wukongIdle,
    action: wukongDabUpright,
    backflip: wukongBackflip,
    successPoses: [
      wukongSuccessOpen,
      wukongSuccessClap25,
      wukongSuccessClap50,
      wukongSuccessClap75,
      wukongSuccessClap,
    ],
  },
  timing: {
    successDurationMs: 1_050,
    returnDurationMs: 720,
    defeatDurationMs: 2_250,
    successTimeline: [
      0, 0, 0,
      1, 2, 3, 4, 4, 3, 2, 1,
      0, 1, 2, 3, 4, 4, 3, 2, 1,
      0, 1, 2, 3, 4, 4,
    ],
  },
}
