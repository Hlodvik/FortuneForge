import amberPowerModule from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-amber-power-module-v2.webp'
import robotCabinetEmblem from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-cabinet-emblem-v2.webp'
import courierDrone from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-courier-drone-v2.webp'
import crimsonCombatModule from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-crimson-combat-module-v2.webp'
import electricGridCharge from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-electric-grid-charge-v2.webp'
import emeraldRepairModule from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-emerald-repair-module-v2.webp'
import fusionBattery from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-fusion-battery-v2.webp'
import fusionReactor from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-fusion-reactor-v2.webp'
import megacity from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-megacity-v2.webp'
import precisionGear from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-precision-gear-v2.webp'
import quantumMagnetArray from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-quantum-magnet-array-v2.webp'
import quantumDataChip from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-quantum-data-chip-v2.webp'
import quantumMicrochip from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-quantum-microchip-v2.webp'
import quantumPortal from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-quantum-portal-v2.webp'
import sentientAiCore from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-sentient-ai-core-v2.webp'
import sapphireLogicModule from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-sapphire-logic-module-v2.webp'
import serviceAndroid from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-service-android-v2.webp'
import titanConstructionMech from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-titan-construction-mech-v2.webp'
import tripleGoldenGears from '../../../assets/slots/games/robot-revolution/optimized/robot-revolution-triple-golden-gears-v2.webp'

export const ROBOT_REVOLUTION_SYMBOL_IMAGES = {
  '2': precisionGear, '3': fusionBattery, '4': quantumMicrochip, '5': courierDrone, '6': serviceAndroid, '7': titanConstructionMech,
  ACE: sentientAiCore, FREE: quantumPortal, POWER: fusionReactor, BOLT: electricGridCharge,
  BANANA: tripleGoldenGears, PAW: quantumMagnetArray,
  SEAL_SYNC: crimsonCombatModule, SEAL_ROWS: sapphireLogicModule, SEAL_PAW: amberPowerModule, SEAL_RAND: emeraldRepairModule,
  VALUE: quantumDataChip,
  CABINET_EMBLEM: robotCabinetEmblem, BACKDROP: megacity,
} as const
