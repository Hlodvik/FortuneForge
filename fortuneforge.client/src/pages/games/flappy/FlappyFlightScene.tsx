import { useId } from 'react'
import { FlappyRules, type FlappySimulationState } from '@fortuneforge/games-flappy'

/** Presentation only: dimensions, bird center and openings come from the unchanged replay. */
export function FlappyFlightScene({ game, reducedMotion }: { game: FlappySimulationState; reducedMotion: boolean }) {
  const id = useId().replaceAll(':', '')
  const rotation = reducedMotion ? 0 : Math.max(-20, Math.min(55, game.birdVelocity * 4.5))
  const wingRotation = reducedMotion ? 4 : [-18, 4, 20][Math.floor(game.tick / 5) % 3]
  return <svg aria-label="Flier and obstacles" role="img" preserveAspectRatio="xMinYMid slice" viewBox={`0 0 ${game.width} ${game.height}`}>
    <defs>
      <linearGradient id={`${id}-sky`} x2="0" y2="1"><stop stopColor="#41bad4" /><stop offset=".6" stopColor="#7bd4d7" /><stop offset="1" stopColor="#d8e8bd" /></linearGradient>
      <linearGradient id={`${id}-pipe`}><stop stopColor="#406d16" /><stop offset=".18" stopColor="#a9de42" /><stop offset=".48" stopColor="#e1f277" /><stop offset=".72" stopColor="#79b72c" /><stop offset="1" stopColor="#315b12" /></linearGradient>
      <linearGradient id={`${id}-bird`} x2="0" y2="1"><stop stopColor="#fff070" /><stop offset=".58" stopColor="#f9b72e" /><stop offset="1" stopColor="#e36a20" /></linearGradient>
      <linearGradient id={`${id}-ground`} x2="0" y2="1"><stop stopColor="#dbee67" /><stop offset=".28" stopColor="#81b834" /><stop offset=".31" stopColor="#f0df99" /><stop offset="1" stopColor="#cba567" /></linearGradient>
      <pattern id={`${id}-ground-stripe`} width="24" height="18" patternUnits="userSpaceOnUse" patternTransform="skewX(-22)"><rect width="12" height="18" fill="#fff2a8" opacity=".35" /></pattern>
      <filter id={`${id}-shadow`} x="-60%" y="-60%" width="220%" height="220%"><feDropShadow dx="2" dy="3" stdDeviation="2" floodColor="#173c43" floodOpacity=".55" /></filter>
    </defs>
    <rect width={game.width} height={game.height} fill={`url(#${id}-sky)`} />
    <circle className="flappy-scene-sun" cx={game.width * .78} cy={game.height * .16} r={game.height * .072} />
    <g className="flappy-scene-clouds"><path d="M38 125c13-29 53-29 68 0 25-11 49 5 49 27H-8c0-15 12-27 28-27 11 0 21 5 27 13Z" /><path d="M430 202c12-25 48-25 61 0 22-10 43 4 43 23H386c0-13 10-23 25-23 9 0 18 4 24 11Z" /></g>
    <path className="flappy-scene-hills flappy-scene-hills--far" d={`M0 ${game.height}V${game.height - 86}Q80 ${game.height - 138} 170 ${game.height - 88}T350 ${game.height - 93}T540 ${game.height - 74}T800 ${game.height - 86}V${game.height}Z`} />
    <path className="flappy-scene-city" d={`M0 ${game.height - 30}V${game.height - 78}h42v-35h32v22h35v-58h45v45h32v-25h48v58h38v-88h52v63h43v-30h36v64h55v-52h46v31h37v-74h53v68h45v-38h51v82h57v-50h43v50H800v60Z`} />
    <path className="flappy-scene-hills" d={`M0 ${game.height}V${game.height - 44}Q90 ${game.height - 102} 200 ${game.height - 48}T400 ${game.height - 54}T600 ${game.height - 37}T800 ${game.height - 45}V${game.height}Z`} />
    {game.obstacles.map(obstacle => <g key={obstacle.id} className="flappy-scene-pipe" data-obstacle-id={obstacle.id}>
      <rect x={obstacle.x + 2} y={-3} width={obstacle.width - 4} height={obstacle.gapTop + 1} fill={`url(#${id}-pipe)`} />
      <rect className="flappy-scene-rim" x={obstacle.x - 6} y={obstacle.gapTop - 24} width={obstacle.width + 12} height={22} fill={`url(#${id}-pipe)`} />
      <rect x={obstacle.x + 2} y={obstacle.gapBottom + 2} width={obstacle.width - 4} height={game.height - obstacle.gapBottom} fill={`url(#${id}-pipe)`} />
      <rect className="flappy-scene-rim" x={obstacle.x - 6} y={obstacle.gapBottom + 2} width={obstacle.width + 12} height={22} fill={`url(#${id}-pipe)`} />
    </g>)}
    <rect className="flappy-scene-ground" y={game.height - 17} width={game.width} height="17" fill={`url(#${id}-ground)`} />
    <rect y={game.height - 14} width={game.width} height="14" fill={`url(#${id}-ground-stripe)`} />
    <g className={`flappy-scene-bird${game.phase !== 'playing' ? ' is-collided' : ''}`} transform={`translate(${FlappyRules.birdX} ${game.birdY}) rotate(${rotation})`} data-bird-y={game.birdY} data-tick={game.tick} filter={`url(#${id}-shadow)`}>
      <path className="flappy-scene-tail" d="M-17-5-30-13-28-2-33 7-17 9Z" />
      <path className="flappy-scene-crest" d="M-9-17Q-4-29 2-18Q8-28 11-15Z" />
      <path className="flappy-scene-body" d="M-22 0C-22-14-13-21 2-21 17-21 24-11 23 3 22 17 11 22-4 21-17 20-23 13-22 0Z" fill={`url(#${id}-bird)`} />
      <ellipse className="flappy-scene-belly" cx={4} cy={10} rx={13} ry={9} />
      <path className="flappy-scene-wing" d="M-19 1Q-9-7 4 1 1 9-5 15L-11 10-17 13Q-21 8-19 1Z" transform={`rotate(${wingRotation} -9 6)`} />
      <path className="flappy-scene-beak flappy-scene-beak--top" d="M18-5 32 1 18 5Z" />
      <path className="flappy-scene-beak flappy-scene-beak--bottom" d="M18 5 29 7 18 10Z" />
      <circle className="flappy-scene-eye" cx={11} cy={-8} r={9} />
      <circle className="flappy-scene-pupil" cx={14} cy={-7} r={4} />
      <circle className="flappy-scene-eye-glint" cx={15} cy={-9} r={1.35} />
      <path className="flappy-scene-brow" d="M3-17Q12-21 19-14" />
    </g>
  </svg>
}
