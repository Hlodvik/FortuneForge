import wukongBackflip from '../../../assets/slots/characters/wukong-backflip-staff.png'
import wukongDabUpright from '../../../assets/slots/characters/wukong-dab-on-staff.png'
import wukongIdle from '../../../assets/slots/characters/wukong-idle-staff.png'
import wukongSuccessClap from '../../../assets/slots/characters/wukong-success-clap.png'
import wukongSuccessClap25 from '../../../assets/slots/characters/wukong-success-clap-25.png'
import wukongSuccessClap50 from '../../../assets/slots/characters/wukong-success-clap-50.png'
import wukongSuccessClap75 from '../../../assets/slots/characters/wukong-success-clap-75.png'
import wukongSuccessOpen from '../../../assets/slots/characters/wukong-success-open.png'
import wukongNimbusAnimated from '../../../assets/slots/symbols/wukong/nimbus-cloud-platform-animated.webp'
import wukongNimbusStatic from '../../../assets/slots/symbols/wukong/nimbus-cloud-platform.png'
import type { MascotSet } from '../shared/mascot/mascotTypes'

export const WUKONG_MASCOT: MascotSet = {
  id: 'wukong-cloud-companion-v1',
  name: 'Wukong',
  assets: {
    platform: {
      kind: 'cloud',
      animated: wukongNimbusAnimated,
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
