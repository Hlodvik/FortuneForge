import horseMountainBackground from './horse-flight-mountain-background-v1.png?no-inline'
import horseMedievalMidground from './horse-flight-medieval-midground-4x-v1.png?no-inline'
import horseHauntedBackground from './horse-flight-haunted-background-v1.png?no-inline'
import horseHauntedMidground from './horse-flight-haunted-midground-v1.png?no-inline'
import { currentRulesVersion, draftMultiBiomeRulesVersion, type HorseRulesVersion } from './horseFlightSimulation'
import './horseBiomeScenery.css'

export type HorseWorldBiome =
  | 'mountain'
  | 'castle-town'
  | 'dark-forest'
  | 'battleground'
  | 'haunted'
  | 'desert'
  | 'sky'

export type HorseBiomeSection =
  | Readonly<{ phase: 'main'; biome: HorseWorldBiome }>
  | Readonly<{
      phase: 'transition'
      biome: HorseWorldBiome
      from: HorseWorldBiome
      to: HorseWorldBiome
      blend: number
    }>

export type HorseBiomeSceneryLayer = 'background' | 'midground' | 'foreground'

type HorseBiomeSceneryProps = Readonly<{
  section: HorseBiomeSection
  layer: HorseBiomeSceneryLayer
  distance?: number
  tick?: number
  rulesVersion?: HorseRulesVersion
}>

const sceneWidth = 960
const sceneHeight = 460

/** Place each layer independently; decorative foreground is drawn behind the horse actor. */
export function HorseBiomeScenery({ section, layer, distance = 0, tick = 0, rulesVersion = currentRulesVersion }: HorseBiomeSceneryProps) {
  if (section.phase === 'main') {
    return <g className="ff-horse-biome-scenery" data-scenery-layer={layer} data-biome={section.biome} aria-hidden="true">
      <BiomeLayer biome={section.biome} layer={layer} distance={distance} tick={tick} rulesVersion={rulesVersion} />
    </g>
  }

  const blend = Math.max(0, Math.min(1, section.blend))
  return <g className="ff-horse-biome-scenery" data-scenery-layer={layer} data-biome={section.biome} data-transition={`${section.from}-to-${section.to}`} aria-hidden="true">
    <g opacity={1 - blend}><BiomeLayer biome={section.from} layer={layer} distance={distance} tick={tick} rulesVersion={rulesVersion} /></g>
    <g opacity={blend}><BiomeLayer biome={section.to} layer={layer} distance={distance} tick={tick} rulesVersion={rulesVersion} /></g>
    <TransitionLandmark from={section.from} to={section.to} layer={layer} blend={blend} />
  </g>
}

function BiomeLayer({ biome, layer, distance, tick, rulesVersion }: Readonly<{ biome: HorseWorldBiome; layer: HorseBiomeSceneryLayer; distance: number; tick: number; rulesVersion: HorseRulesVersion }>) {
  if (biome === 'mountain' || biome === 'haunted') {
    if (layer === 'background') {
      const image = biome === 'mountain' ? horseMountainBackground : horseHauntedBackground
      const passage = (tick % 30_000) / 15_000
      const sweep = passage <= 1 ? passage : 2 - passage
      return <image className="ff-horse-biome-scenery__legacy-image" href={image} x={-660 + sweep * 660} y="0" width="1620" height={sceneHeight} preserveAspectRatio="none" />
    }
    if (layer === 'midground') {
      const sweep = (tick % 62_726) / 31_363
      const position = sweep <= 1 ? sweep : 2 - sweep
      const x = -5520 * position
      if (biome === 'haunted') {
        // The graveyard occupies source rows 300–469. Seat those graves at the track,
        // and repeat at the medieval strip's aspect ratio instead of stretching 3×.
        return <g>{[0, 1, 2, 3].map(copy => <image key={copy} className="ff-horse-biome-scenery__legacy-image" href={horseHauntedMidground} x={x + copy * 1620} y="162" width="1620" height={sceneHeight} preserveAspectRatio="none" />)}</g>
      }
      return <image className="ff-horse-biome-scenery__legacy-image" href={horseMedievalMidground} x={x} y="0" width="6480" height={sceneHeight} preserveAspectRatio="none" />
    }
  }

  // The near pines belong to world positions, not a repeated 960px texture.
  if (biome === 'mountain' && layer === 'foreground') return <MountainForeground distance={distance} />

  const speed = layer === 'background' ? 0.035 : layer === 'midground' ? 0.12 : 0.34
  const shift = ((distance * speed) % sceneWidth + sceneWidth) % sceneWidth
  return <g className={`ff-horse-biome-scenery__${layer}`}>
    {[0, 1].map(copy => <g key={copy} transform={`translate(${copy * sceneWidth - shift} 0)`}>
      <BiomeTile biome={biome} layer={layer} rulesVersion={rulesVersion} />
    </g>)}
  </g>
}

function BiomeTile({ biome, layer, rulesVersion }: Readonly<{ biome: HorseWorldBiome; layer: HorseBiomeSceneryLayer; rulesVersion: HorseRulesVersion }>) {
  switch (biome) {
    case 'mountain': return null
    case 'castle-town': return layer === 'background' ? <CastleBackground /> : layer === 'midground' ? <CastleMidground /> : <CastleForeground />
    case 'dark-forest': return layer === 'background' ? <DarkForestBackground /> : layer === 'midground' ? <DarkForestMidground /> : <DarkForestForeground />
    case 'battleground': return layer === 'background' ? <BattlegroundBackground /> : layer === 'midground' ? <BattlegroundMidground /> : <BattlegroundForeground />
    case 'haunted': return layer === 'foreground' && rulesVersion === draftMultiBiomeRulesVersion ? <HauntedForeground /> : null
    case 'desert': return layer === 'background' ? <DesertBackground /> : layer === 'midground' ? <DesertMidground /> : <DesertForeground />
    case 'sky': return layer === 'background' ? <SkyBackground /> : layer === 'midground' ? <SkyMidground /> : <SkyForeground />
  }
}

function PixelCloud({ x, y, color = '#e8d9bc', opacity = 1 }: Readonly<{ x: number; y: number; color?: string; opacity?: number }>) {
  return <g transform={`translate(${x} ${y})`} fill={color} opacity={opacity}>
    <rect x="20" y="0" width="53" height="8" /><rect x="8" y="8" width="97" height="9" />
    <rect x="0" y="17" width="130" height="8" /><rect x="14" y="25" width="91" height="5" />
  </g>
}

function CastleBackground() {
  return <g>
    <rect width={sceneWidth} height={sceneHeight} fill="#8faabe" />
    <rect y="0" width={sceneWidth} height="96" fill="#a6c1cf" />
    <rect y="96" width={sceneWidth} height="92" fill="#d8cbad" />
    <rect y="188" width={sceneWidth} height="110" fill="#9b9e9a" />
    <PixelCloud x={42} y={52} /><PixelCloud x={475} y={30} color="#f1e4c9" opacity={0.86} />
    <path d="M0 273 95 224 163 242 275 202 361 244 481 192 572 244 688 217 790 237 864 202 960 248V460H0Z" fill="#667b80" />
    <path d="M0 320 106 281 197 306 304 269 430 300 556 267 670 296 785 259 882 290 960 272V460H0Z" fill="#586a6f" />
    <g fill="#59616a"><rect x="95" y="252" width="24" height="79" /><rect x="90" y="243" width="34" height="12" /><rect x="240" y="269" width="19" height="59" /><rect x="236" y="261" width="27" height="10" /><rect x="505" y="240" width="28" height="95" /><rect x="499" y="230" width="40" height="13" /><rect x="772" y="253" width="25" height="83" /><rect x="765" y="243" width="39" height="12" /></g>
    <path d="M0 336H960V460H0Z" fill="#596069" />
    {Array.from({ length: 31 }, (_, i) => <rect key={i} x={i * 32} y={i % 3 === 0 ? 326 : 330} width="22" height="12" fill="#737782" />)}
  </g>
}

function CastleHouse({ x, y, width, roof = '#553b3b' }: Readonly<{ x: number; y: number; width: number; roof?: string }>) {
  const bodyHeight = 455 - y
  return <g transform={`translate(${x} ${y})`}>
    <rect x="0" y="19" width={width} height={bodyHeight - 19} fill="#bfae91" />
    <path d={`M-8 22 12 0H${width - 13}L${width + 9} 22Z`} fill={roof} />
    <rect x="0" y="20" width={width} height="5" fill="#312b32" />
    <rect x="8" y="27" width="7" height={bodyHeight - 27} fill="#704e3d" /><rect x={width - 15} y="27" width="7" height={bodyHeight - 27} fill="#704e3d" />
    <rect x="15" y="61" width={width - 30} height="7" fill="#704e3d" />
    <path d={`M15 27 ${width - 15} 68M${width - 15} 27 15 68`} fill="none" stroke="#76533d" strokeWidth="5" />
    <rect x={width / 2 - 11} y="82" width="22" height="35" fill="#443b3a" /><rect x={width / 2 - 7} y="85" width="14" height="23" fill="#af9c73" />
    <rect x="20" y="38" width="15" height="16" fill="#384a55" /><rect x="22" y="40" width="11" height="12" fill="#e7c484" />
    <rect x={width - 35} y="38" width="15" height="16" fill="#384a55" /><rect x={width - 33} y="40" width="11" height="12" fill="#e7c484" />
  </g>
}

function CastleMidground() {
  return <g>
    <rect y="407" width={sceneWidth} height="53" fill="#686572" opacity=".75" />
    <CastleHouse x={15} y={296} width={128} roof="#5d3a3d" />
    <CastleHouse x={174} y={311} width={119} roof="#624a47" />
    <CastleHouse x={342} y={278} width={151} roof="#493a45" />
    <CastleHouse x={559} y={311} width={133} roof="#603b40" />
    <CastleHouse x={771} y={289} width={144} roof="#4a3942" />
    <g fill="#9c927e"><rect x="146" y="339" width="32" height="116" /><rect x="141" y="328" width="42" height="14" /><rect x="710" y="325" width="45" height="130" /><rect x="703" y="314" width="59" height="14" /></g>
    <g fill="#4d4850"><rect x="149" y="324" width="26" height="6" /><rect x="714" y="309" width="37" height="6" /></g>
    <g fill="#d1b889"><rect x="153" y="358" width="18" height="21" /><rect x="719" y="342" width="27" height="27" /></g>
    <g fill="#35434d"><rect x="158" y="362" width="8" height="13" /><rect x="725" y="346" width="15" height="19" /></g>
    <path d="M0 451H960V459H0Z" fill="#45434c" />
    {Array.from({ length: 28 }, (_, i) => <rect key={i} x={i * 35 + 6} y={445 - (i % 3) * 2} width="24" height="3" fill="#a59880" opacity=".7" />)}
  </g>
}

function CastleForeground() {
  return <g>
    <path d="M0 452H960V460H0Z" fill="#3e4149" opacity=".8" />
    {[80, 320, 600, 880].map((x, i) => <g key={x} transform={`translate(${x} 0)`}>
      <rect x="-7" y={i % 2 ? 416 : 407} width="14" height={i % 2 ? 40 : 49} fill="#403d43" />
      <rect x="-10" y={i % 2 ? 412 : 403} width="20" height="6" fill="#9d917a" />
      <rect x="-4" y={i % 2 ? 385 : 377} width="8" height="27" fill="#4c4140" />
      <rect x="-9" y={i % 2 ? 378 : 370} width="18" height="10" fill="#d9ae64" />
      <rect x="-5" y={i % 2 ? 380 : 372} width="10" height="6" fill="#ffe0a0" />
    </g>)}
    <g transform="translate(478 0)"><rect x="0" y="421" width="66" height="30" fill="#754d35" /><rect x="-4" y="416" width="74" height="8" fill="#9c6e43" /><rect x="11" y="427" width="8" height="19" fill="#ad8053" /><rect x="45" y="427" width="8" height="19" fill="#ad8053" /><circle cx="14" cy="453" r="8" fill="#292a30" /><circle cx="53" cy="453" r="8" fill="#292a30" /></g>
    <path d="M0 453h130m65 0h160m140 0h170m93 0h202" stroke="#c4a67a" strokeWidth="3" opacity=".7" />
  </g>
}

function DarkForestBackground() {
  return <g>
    <rect width={sceneWidth} height={sceneHeight} fill="#1b2638" />
    <rect y="0" width={sceneWidth} height="130" fill="#172033" />
    <rect y="130" width={sceneWidth} height="115" fill="#283349" />
    <rect x="637" y="64" width="54" height="54" fill="#c4c5c9" opacity=".7" />
    <rect x="643" y="61" width="19" height="13" fill="#253047" />
    <PixelCloud x={90} y={104} color="#3c4960" opacity={0.7} /><PixelCloud x={504} y={161} color="#4b526a" opacity={0.48} />
    <path d="M0 355 76 313 118 336 218 295 316 345 391 297 476 328 552 289 646 338 729 302 796 329 879 283 960 328V460H0Z" fill="#243c43" />
    {Array.from({ length: 29 }, (_, i) => <path key={i} d={`M${i * 35 - 8} 390l18-${55 + i % 4 * 14} 18 ${55 + i % 4 * 14}Z`} fill={i % 3 ? '#172d36' : '#1a3540'} />)}
  </g>
}

function TwistedTree({ x, scale = 1, color = '#18252e' }: Readonly<{ x: number; scale?: number; color?: string }>) {
  return <g transform={`translate(${x} 455) scale(${scale})`} fill={color}>
    <path d="M-12 0-7-117-30-168-23-176-3-145 0-225 13-222 13-150 45-195 54-190 21-129 22 0Z" />
    <path d="M-4-167-52-204-66-200-19-150M13-173 70-224 83-216 27-153" stroke={color} strokeWidth="10" fill="none" />
    <path d="M-12 0-51 17h63L17 0Z" />
  </g>
}

function DarkForestMidground() {
  return <g>
    <rect y="394" width={sceneWidth} height="66" fill="#182d35" opacity=".85" />
    <TwistedTree x={43} scale={1.18} /><TwistedTree x={245} scale={0.93} color="#1c2d35" />
    <TwistedTree x={490} scale={1.22} /><TwistedTree x={734} scale={1.05} color="#1c2d35" />
    <TwistedTree x={938} scale={0.97} />
    {[131, 322, 571, 834].map(x => <g key={x} fill="#b8bc75"><rect x={x} y="389" width="5" height="3" /><rect x={x + 7} y="391" width="3" height="2" /></g>)}
    <path d="M0 429h960" stroke="#6c7481" strokeWidth="12" opacity=".12" />
  </g>
}

function DarkForestForeground() {
  return <g fill="#111e28" opacity=".94">
    <path d="M0 459v-23l24-15 10 24 23-18 14 28h20l10-16 21 18 24-19 13 21Zm306 0 25-24 18 21 24-32 24 34 31-16 18 17Zm626 0-18-25-21 22-27-18-11 21Z" />
    {[183, 451, 699, 882].map((x, i) => <g key={x} transform={`translate(${x} 459)`}>
      <path d={`M0 0-5-${39 + i % 2 * 13} 2-${69 + i % 2 * 13} 8-${39 + i % 2 * 13} 16 0Z`} />
      <path d="M4-31-20-57M7-23 28-53" stroke="#111e28" strokeWidth="7" fill="none" />
    </g>)}
  </g>
}

function BattlegroundBackground() {
  return <g>
    <rect width={sceneWidth} height={sceneHeight} fill="#806f70" />
    <rect y="0" width={sceneWidth} height="113" fill="#665c69" />
    <rect y="113" width={sceneWidth} height="113" fill="#a17a70" />
    <rect x="676" y="135" width="66" height="63" fill="#e0a071" opacity=".8" />
    <PixelCloud x={17} y={69} color="#555564" opacity={0.8} /><PixelCloud x={351} y={114} color="#65616a" opacity={0.72} /><PixelCloud x={747} y={81} color="#524f5d" opacity={0.68} />
    <path d="M0 316 101 271 206 299 318 257 435 307 537 263 630 290 756 248 863 297 960 272V460H0Z" fill="#686365" />
    <path d="M0 368 90 331 190 350 320 313 420 340 550 306 660 336 775 308 872 337 960 310V460H0Z" fill="#544e4d" />
    <g fill="#484342"><rect x="135" y="278" width="9" height="55" /><rect x="128" y="277" width="41" height="7" /><rect x="514" y="275" width="13" height="58" /><rect x="507" y="269" width="37" height="9" /><rect x="831" y="278" width="12" height="51" /></g>
  </g>
}

function BattlegroundMidground() {
  return <g>
    <path d="M0 413h960v47H0Z" fill="#554b48" opacity=".72" />
    <path d="M39 411 114 346l70 65Zm192 0 75-56 55 56Zm422 0 80-69 68 69Z" fill="#38373c" />
    <path d="M52 408h117m74 0h103m318 0h122" stroke="#a39176" strokeWidth="6" />
    <path d="M415 455v-130h13v130Zm-12-130h92l-17 36h-75Z" fill="#3f3a3b" />
    <path d="M428 328h68l-14 32h-54Z" fill="#934f46" />
    <path d="M563 453v-103h9v103Zm9-97h59l-10 31h-49Z" fill="#433d3f" />
    <path d="M573 357h56l-10 26h-46Z" fill="#8e6c57" />
    {[18, 216, 350, 686, 896].map((x, i) => <g key={x} fill={i % 2 ? '#3c3a3d' : '#4a4340'}><rect x={x} y="411" width="9" height="44" /><path d={`M${x - 5} 411 ${x + 4} 389 ${x + 14} 411Z`} /></g>)}
    <rect x="803" y="432" width="78" height="21" fill="#57443d" /><circle cx="818" cy="455" r="9" fill="#25272d" /><circle cx="868" cy="455" r="9" fill="#25272d" />
  </g>
}

function BattlegroundForeground() {
  return <g>
    <path d="M0 455h960v5H0Z" fill="#3b3432" opacity=".9" />
    {[62, 270, 492, 740, 917].map((x, i) => <g key={x} transform={`translate(${x} 456) rotate(${i % 2 ? 18 : -15})`}>
      <rect x="-3" y="-60" width="6" height="60" fill="#4e372f" />
      <path d="M-7-60 0-82 7-60Z" fill="#9b998f" />
      <rect x="-13" y="-51" width="26" height="5" fill="#66594a" />
    </g>)}
    <path d="M359 456h77l-16-14h-39Zm452 0h82l-18-16h-43Z" fill="#2b292d" />
    <path d="M11 451h160m439 0h160" stroke="#a26d51" strokeWidth="3" opacity=".55" />
  </g>
}

function DesertBackground() {
  return <g>
    <rect width={sceneWidth} height={sceneHeight} fill="#d6975b" />
    <rect y="0" width={sceneWidth} height="116" fill="#86aec0" />
    <rect y="116" width={sceneWidth} height="110" fill="#e6bc78" />
    <rect x="687" y="57" width="78" height="77" fill="#ffe1a1" />
    <PixelCloud x={98} y={77} color="#f7d8a1" opacity={0.57} /><PixelCloud x={490} y={147} color="#f2c284" opacity={0.44} />
    <path d="M0 336 92 318 170 323 251 262 307 262 355 330 464 309 559 327 643 277 710 277 760 333 857 311 960 336V460H0Z" fill="#b57756" />
    <path d="M0 376q110-84 238-32 84-70 180-32 106-68 222 19 170-72 320 6v123H0Z" fill="#dbad70" />
    <path d="M0 414q140-62 310-25 195-50 365 11 165-41 285-14v74H0Z" fill="#e5b778" />
  </g>
}

function DesertMidground() {
  return <g>
    <path d="M0 421q160-27 329 0 137-27 315 0 168-32 316 0v39H0Z" fill="#c98e5c" opacity=".85" />
    <g fill="#a77a58"><rect x="131" y="363" width="25" height="75" /><rect x="121" y="356" width="45" height="12" /><rect x="127" y="347" width="33" height="9" /><rect x="779" y="369" width="29" height="70" /><rect x="769" y="361" width="49" height="11" /><rect x="777" y="351" width="33" height="10" /></g>
    <g fill="#745c49"><rect x="139" y="378" width="9" height="31" /><rect x="789" y="386" width="9" height="32" /></g>
    {[271, 590, 903].map((x, i) => <g key={x} transform={`translate(${x} ${i % 2 ? 441 : 428})`}>
      <rect x="-3" y="-47" width="7" height="47" fill="#755b39" />
      <path d="M0-43q-21-37-44-14l33 8Zm2-1q20-39 47-17l-36 10ZM1-45q-5-33 9-41l7 38Z" fill="#426c4d" />
      <rect x="-22" y="-3" width="42" height="4" fill="#ae7449" />
    </g>)}
    <path d="M0 448h960" stroke="#e7c18b" strokeWidth="5" opacity=".55" />
  </g>
}

function DesertForeground() {
  return <g>
    <path d="M0 455h960v5H0Z" fill="#b47d50" opacity=".7" />
    {[51, 379, 712, 934].map((x, i) => <g key={x} transform={`translate(${x} 455)`} fill="#466a4a">
      <rect x="-5" y={i % 2 ? -38 : -47} width="10" height={i % 2 ? 38 : 47} />
      <rect x="-18" y="-27" width="13" height="7" /><rect x="-18" y="-40" width="7" height="18" />
      <rect x="5" y="-20" width="13" height="7" /><rect x="11" y="-31" width="7" height="15" />
    </g>)}
    <path d="M197 455l13-14 21 14Zm369 0 18-12 21 12Zm244 0 12-9 19 9Z" fill="#8c674b" />
    <path d="M106 454h57m309 0h54m341 0h41" stroke="#f5d294" strokeWidth="3" opacity=".7" />
  </g>
}

function SkyBackground() {
  return <g>
    <rect width={sceneWidth} height={sceneHeight} fill="#82b9db" />
    <rect y="0" width={sceneWidth} height="115" fill="#487fae" />
    <rect y="115" width={sceneWidth} height="105" fill="#72acd4" />
    <rect y="220" width={sceneWidth} height="120" fill="#bad0df" />
    <PixelCloud x={58} y={88} color="#d8eafa" /><PixelCloud x={381} y={180} color="#eaf4ff" opacity={0.9} /><PixelCloud x={758} y={105} color="#e0eefb" />
    <path d="M0 345q124-38 260 8 159-53 311 3 209-48 389-4v108H0Z" fill="#e6eff2" opacity=".58" />
    <path d="M0 390q160-46 326 0 177-31 338 9 177-35 296-3v64H0Z" fill="#f7f0ea" opacity=".57" />
  </g>
}

function SkyMidground() {
  return <g>
    {[51, 361, 700].map((x, i) => <g key={x} transform={`translate(${x} ${i % 2 ? 357 : 332})`}>
      <path d="M0 40 33 11h124l44 29-32 16H31Z" fill="#66798a" />
      <path d="M24 22 43 0h116l29 23Z" fill="#a4b6bf" />
      <path d="M21 49h160l-23 12H40Z" fill="#465c72" />
      <rect x="79" y="-41" width="25" height="47" fill="#d7d9d4" /><rect x="73" y="-49" width="37" height="11" fill="#6e889a" />
      <rect x="85" y="-72" width="13" height="23" fill="#f0dfbb" /><rect x="87" y="-78" width="9" height="8" fill="#7da8c3" />
    </g>)}
    <PixelCloud x={198} y={369} color="#f3f4f2" opacity={0.9} /><PixelCloud x={604} y={400} color="#fff5e9" opacity={0.85} />
  </g>
}

function SkyForeground() {
  return <g>
    <PixelCloud x={-24} y={439} color="#f9f9f0" opacity={0.7} /><PixelCloud x={296} y={443} color="#f5f7f0" opacity={0.55} /><PixelCloud x={727} y={437} color="#f8faf5" opacity={0.73} />
    {[240, 550, 893].map((x, i) => <g key={x} transform={`translate(${x} 453)`}>
      <path d="M0 0 10-44 24-46 36 0Z" fill="#6e8d9e" />
      <path d="M10-43 18-64 25-44Z" fill="#cce6ee" />
      <rect x="16" y={i % 2 ? -85 : -73} width="4" height="21" fill="#dce5e0" />
      <path d={`M20 ${i % 2 ? -84 : -72}h24l-10 13H20Z`} fill="#e8d29b" />
    </g>)}
  </g>
}

const mountainPineCellWidth = 139
const mountainPineScrollRate = 0.34

/** A stable integer hash prevents the trees from reshuffling on every render. */
function mountainPineNoise(cell: number, salt: number) {
  let value = Math.imul(cell ^ salt, 0x45d9f3b)
  value = Math.imul(value ^ (value >>> 16), 0x45d9f3b)
  return ((value ^ (value >>> 16)) >>> 0) / 0x100000000
}

type PineTier = Readonly<{ y: number; left: number; right: number; lean: number }>

const pineTiers: readonly (readonly PineTier[])[] = [
  [
    { y: -146, left: 12, right: 13, lean: 0 },
    { y: -116, left: 19, right: 21, lean: -2 },
    { y: -86, left: 27, right: 27, lean: 1 },
    { y: -56, left: 34, right: 36, lean: -2 },
  ],
  [
    { y: -132, left: 14, right: 14, lean: 1 },
    { y: -103, left: 22, right: 21, lean: -2 },
    { y: -74, left: 30, right: 31, lean: 2 },
    { y: -45, left: 37, right: 34, lean: -1 },
  ],
  [
    { y: -119, left: 12, right: 13, lean: -1 },
    { y: -92, left: 18, right: 20, lean: 2 },
    { y: -65, left: 25, right: 24, lean: -2 },
    { y: -38, left: 31, right: 30, lean: 1 },
  ],
]

function pineBoughPath({ y, left, right, lean }: PineTier) {
  return `M${lean - 3} ${y}H${lean + 3}V${y + 4}H${lean + 5}V${y + 9}H${Math.round(right * .5)}V${y + 14}H${Math.round(right * .72)}V${y + 18}H${right - 5}V${y + 21}H${right}V${y + 25}H${right - 8}V${y + 28}H${right - 16}V${y + 31}H${Math.round(right * .3)}V${y + 27}H${-Math.round(left * .28)}V${y + 31}H${-left + 14}V${y + 28}H${-left + 7}V${y + 25}H${-left}V${y + 21}H${-left + 5}V${y + 18}H${-Math.round(left * .72)}V${y + 14}H${-Math.round(left * .5)}V${y + 9}H${lean - 5}V${y + 4}H${lean - 3}Z`
}

function MountainPine({ x, y, scale, variant, cell }: Readonly<{ x: number; y: number; scale: number; variant: number; cell: number }>) {
  const tiers = pineTiers[variant]
  return <g data-scenery-decoration="pine" data-world-cell={cell} transform={`translate(${Math.round(x)} ${y}) scale(${scale})`}>
    <path d="M-6 0v-86h12V0Z" fill="#233336" />
    <path d="M-2-83h3V-9h-3Z" fill="#41514d" opacity=".72" />
    <path d="M-6-8-17 0h11l6-7 6 7h11L6-8Z" fill="#102b31" />
    {tiers.map((tier, index) => <g key={tier.y}>
      <path d={pineBoughPath(tier)} fill={index % 2 ? '#102d34' : '#15363c'} />
      <path d={`M${tier.lean - 2} ${tier.y + 4}h5v5h5v4h${Math.max(1, tier.right - 25)}v4h4v3h-8v-3h-6v4h-10v-3h-7v-4h4v-4h5v-4h4Z`} fill={index % 2 ? '#285052' : '#2d5758'} />
      <path d={`M${-tier.left + 6} ${tier.y + 22}h${Math.max(7, Math.round(tier.left * .5))}v3h8v3h-7v-2h-8v2h-7Z`} fill="#0b2730" opacity=".88" />
      <rect x={tier.right - 20} y={tier.y + 24} width="10" height="3" fill="#0b2730" />
      <rect x={-tier.left + 13 + (index % 2) * 6} y={tier.y + 15} width="5" height="2" fill="#55736e" opacity=".65" />
      <rect x={tier.right - 18} y={tier.y + 12} width="4" height="2" fill="#53716d" opacity=".5" />
      <rect x={-tier.left + 5} y={tier.y + 20} width="5" height="2" fill="#42615f" opacity=".62" />
    </g>)}
  </g>
}

function MountainForeground({ distance }: Readonly<{ distance: number }>) {
  const scroll = distance * mountainPineScrollRate
  const firstCell = Math.floor((scroll - 70) / mountainPineCellWidth)
  const lastCell = Math.ceil((scroll + sceneWidth + 70) / mountainPineCellWidth)
  const pines = []

  for (let cell = firstCell; cell <= lastCell; cell++) {
    // Let the opening frame show the horse clearly before the first near tree enters.
    if (cell === 0) continue
    // Slowly changing grove density makes some stretches wooded and others open.
    const grove = mountainPineNoise(Math.floor(cell / 6), 0x3187)
    if (mountainPineNoise(cell, 0x9321) > .26 + grove * .58) continue
    const worldX = cell * mountainPineCellWidth + 14 + mountainPineNoise(cell, 0x4c1d) * 112
    const x = worldX - scroll
    const y = 452 + Math.round(mountainPineNoise(cell, 0x5173) * 7)
    const scale = Math.round((.73 + mountainPineNoise(cell, 0x6881) * .37) * 100) / 100
    const variant = Math.floor(mountainPineNoise(cell, 0x2a17) * pineTiers.length)
    pines.push(<MountainPine key={cell} cell={cell} x={x} y={y} scale={scale} variant={variant} />)
    if (mountainPineNoise(cell, 0x1d54) < .22) {
      const side = mountainPineNoise(cell, 0x6718) < .5 ? -1 : 1
      pines.push(<MountainPine key={`${cell}-sapling`} cell={cell} x={x + side * (25 + mountainPineNoise(cell, 0x5511) * 25)} y={y - 3} scale={scale * .54} variant={(variant + 1) % pineTiers.length} />)
    }
  }

  return <g className="ff-horse-biome-scenery__foreground-pines">{pines}</g>
}

function HauntedForeground() {
  return <g opacity=".88">
    {[86, 429, 779].map((x, i) => <g key={x} transform={`translate(${x} 458)`}>
      <path d={`M-9 0-5-${i % 2 ? 73 : 88} 2-${i % 2 ? 84 : 101} 10-${i % 2 ? 69 : 81} 13 0Z`} fill="#292938" />
      <path d="M3-51-31-84M7-60 41-91M0-70-7-105M-20-72-43-67M29-81 43-109M-7-91-24-113" stroke="#2a2938" strokeWidth="7" fill="none" />
      <path d="M-2-72-31-84M7-60 29-77M-3-91-8-105" stroke="#616073" strokeWidth="2" fill="none" opacity=".6" />
      <path d="M-24-76h8v5h-8Zm54-13h7v5h-7Zm-20-24h6v5h-6Z" fill="#6b617d" opacity=".6" />
      <rect x="-29" y="-12" width="14" height="12" fill="#45485a" /><rect x="19" y="-9" width="19" height="9" fill="#3c4050" />
      <rect x="-25" y="-9" width="6" height="3" fill="#8a8798" /><rect x="24" y="-7" width="8" height="2" fill="#706f84" />
    </g>)}
  </g>
}

function TransitionLandmark({ from, to, layer, blend }: Readonly<{ from: HorseWorldBiome; to: HorseWorldBiome; layer: HorseBiomeSceneryLayer; blend: number }>) {
  const key = `${from}>${to}`
  const x = 550 - blend * 240
  const opacity = 0.4 + Math.sin(Math.PI * blend) * 0.6
  if (layer === 'background') {
    if (key === 'desert>sky') return <g opacity={opacity}><path d="M0 460 235 332 408 356 552 196 671 221 960 64V460Z" fill="#b8b2a0" opacity=".3" /><PixelCloud x={513} y={168} color="#f4eee4" opacity={0.75} /></g>
    if (key === 'dark-forest>battleground') return <g opacity={opacity}><rect x="349" y="102" width="61" height="101" fill="#5c5b61" opacity=".35" /><rect x="370" y="74" width="41" height="133" fill="#66616a" opacity=".28" /><PixelCloud x={340} y={78} color="#5d5a64" opacity={0.62} /></g>
    if (key === 'haunted>desert') return <g opacity={opacity}><rect y="298" width={sceneWidth} height="70" fill="#baa174" opacity=".18" /><path d="M0 430 159 392 312 416 469 383 638 411 771 391 960 418V460H0Z" fill="#bd9b6b" opacity=".33" /></g>
    return null
  }
  if (layer === 'foreground') {
    if (key === 'mountain>castle-town') return <g transform={`translate(${x} 0)`} opacity={opacity}><rect x="-123" y="411" width="19" height="45" fill="#5a5250" /><rect x="96" y="411" width="19" height="45" fill="#5a5250" /><rect x="-132" y="404" width="35" height="11" fill="#a19681" /><rect x="88" y="404" width="35" height="11" fill="#a19681" /></g>
    if (key === 'castle-town>dark-forest') return <g transform={`translate(${x} 0)`} opacity={opacity}><rect x="-103" y="423" width="26" height="34" fill="#6a605c" /><path d="M-81 458-62 429-50 456m77 0 21-30 23 30" stroke="#1c2930" strokeWidth="9" fill="none" /></g>
    if (key === 'dark-forest>battleground') return <g transform={`translate(${x} 0)`} opacity={opacity}><path d="M-85 458-46 412-30 458m30 0 28-39 16 39" fill="#25282d" /><path d="M-72 457-66 391m34 66 14-69" stroke="#3c3030" strokeWidth="7" /></g>
    if (key === 'battleground>haunted') return <g transform={`translate(${x} 0)`} opacity={opacity}><path d="M-93 458h45v-36l-22-15-23 15Zm125 0h37v-29l-19-13-18 13Z" fill="#3c3e4c" /><path d="M-70 444v-22m121 20v-15" stroke="#9ca4ad" strokeWidth="4" /></g>
    if (key === 'haunted>desert') return <g transform={`translate(${x} 0)`} opacity={opacity}><path d="M-101 457h120l-37-14-59 2Zm63 0h112l-28-14-58 2Z" fill="#c99560" /><rect x="16" y="424" width="28" height="33" fill="#70665d" /></g>
    if (key === 'desert>sky') return <g transform={`translate(${x} 0)`} opacity={opacity}><PixelCloud x={-115} y={428} color="#fcf4e6" opacity={0.9} /><PixelCloud x={70} y={417} color="#f9f5e9" opacity={0.9} /></g>
    return null
  }
  switch (key) {
    case 'mountain>castle-town': return <CastleGate x={x} opacity={opacity} />
    case 'castle-town>dark-forest': return <g transform={`translate(${x} 0)`} opacity={opacity}><rect x="-111" y="317" width="67" height="141" fill="#655c59" /><rect x="-117" y="308" width="78" height="13" fill="#a49b84" /><rect x="-82" y="337" width="12" height="113" fill="#342e35" /><TwistedTree x={83} scale={1.17} color="#263139" /><path d="M-77 393 11 373 55 399" stroke="#34303a" strokeWidth="13" fill="none" /></g>
    case 'dark-forest>battleground': return <g transform={`translate(${x} 0)`} opacity={opacity}><TwistedTree x={-77} scale={0.92} color="#282930" /><path d="M-45 449 4 376 56 449Z" fill="#433e3e" /><rect x="89" y="350" width="9" height="104" fill="#4e3d39" /><path d="M98 353h65l-12 33H98Z" fill="#8d4c47" /><path d="M-107 441h240" stroke="#5c514d" strokeWidth="10" /></g>
    case 'battleground>haunted': return <g transform={`translate(${x} 0)`} opacity={opacity}><rect x="-102" y="369" width="13" height="87" fill="#3d383d" /><path d="M-89 373h67l-16 32h-51Z" fill="#7b4143" /><rect x="8" y="379" width="79" height="77" fill="#4e4b58" /><path d="M-2 381 48 341 96 381Z" fill="#63616e" /><rect x="35" y="408" width="24" height="48" fill="#242936" /><rect x="103" y="417" width="16" height="39" fill="#56596a" /></g>
    case 'haunted>desert': return <g transform={`translate(${x} 0)`} opacity={opacity}><rect x="-100" y="380" width="41" height="75" fill="#696267" /><path d="M-104 380-79 359-55 380Z" fill="#777071" /><rect x="10" y="337" width="37" height="118" fill="#9c7860" /><path d="M4 337h49l-9-15H13Z" fill="#c39769" /><path d="M-109 444q86-27 174 0v14h-174Z" fill="#d2a570" /></g>
    case 'desert>sky': return <g transform={`translate(${x} 0)`} opacity={opacity}><path d="M-134 459 6 313 103 459Z" fill="#b39575" /><path d="M-58 440h33v-16H-9v-17H7v-17h17v-17h17" fill="none" stroke="#f1dfbd" strokeWidth="12" /><rect x="28" y="288" width="25" height="88" fill="#e3d8bd" /><path d="M22 290 41 261 60 290Z" fill="#9babb5" /><PixelCloud x={37} y={321} color="#f8f5ec" opacity={0.84} /></g>
    default: return null
  }
}

function CastleGate({ x, opacity }: Readonly<{ x: number; opacity: number }>) {
  return <g transform={`translate(${x} 0)`} opacity={opacity}>
    <rect x="-135" y="328" width="270" height="128" fill="#817d76" />
    <rect x="-146" y="316" width="292" height="16" fill="#b0a895" />
    {Array.from({ length: 9 }, (_, i) => <rect key={i} x={-136 + i * 33} y="302" width="23" height="17" fill="#b0a895" />)}
    <rect x="-119" y="337" width="65" height="119" fill="#6e6965" /><rect x="55" y="337" width="65" height="119" fill="#6e6965" />
    <path d="M-47 456v-51q0-47 47-47t47 47v51Z" fill="#303943" />
    <rect x="-41" y="402" width="82" height="8" fill="#554846" />
    <rect x="-31" y="412" width="6" height="44" fill="#8e6545" /><rect x="25" y="412" width="6" height="44" fill="#8e6545" />
    <rect x="-167" y="357" width="27" height="98" fill="#777069" /><rect x="140" y="357" width="27" height="98" fill="#777069" />
    <path d="M-17 317h34v38h-34Z" fill="#72524b" /><rect x="-12" y="326" width="24" height="18" fill="#cfb17b" />
  </g>
}
