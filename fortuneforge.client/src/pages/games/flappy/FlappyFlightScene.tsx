import { useId } from 'react'
import { FlappyRules, type FlappySimulationState } from '@fortuneforge/games-flappy'

/** Presentation only: dimensions, bird center and openings come from the unchanged replay. */
export function FlappyFlightScene({ game, reducedMotion }: { game: FlappySimulationState; reducedMotion: boolean }) {
  const id = useId().replaceAll(':', '')
  const rotation = reducedMotion ? 0 : Math.max(-18, Math.min(48, game.birdVelocity * 4))
  return <svg aria-label="Flier and obstacles" role="img" viewBox={`0 0 ${game.width} ${game.height}`}>
    <defs>
      <linearGradient id={`${id}-sky`} x2="0" y2="1"><stop stopColor="#2b779b" /><stop offset=".64" stopColor="#205474" /><stop offset="1" stopColor="#142f49" /></linearGradient>
      <linearGradient id={`${id}-pipe`}><stop stopColor="#318060" /><stop offset=".3" stopColor="#70d3a4" /><stop offset=".7" stopColor="#43a67c" /><stop offset="1" stopColor="#236d53" /></linearGradient>
      <linearGradient id={`${id}-bird`} x2="0" y2="1"><stop stopColor="#ffe8a2" /><stop offset="1" stopColor="#f3a641" /></linearGradient>
    </defs>
    <rect width={game.width} height={game.height} fill={`url(#${id}-sky)`} />
    <circle className="flappy-scene-sun" cx={game.width * .78} cy={game.height * .17} r={game.height * .075} />
    <g className="flappy-scene-clouds"><path d="M60 130c10-23 42-23 54 0 20-9 39 4 39 21H23c0-12 9-21 22-21 9 0 17 4 21 10Z" /><path d="M416 214c10-20 39-20 50 0 18-8 35 4 35 19H380c0-11 9-19 20-19 8 0 15 4 20 9Z" /></g>
    <path className="flappy-scene-hills" d={`M0 ${game.height}V${game.height - 48}Q90 ${game.height - 110} 200 ${game.height - 50}T400 ${game.height - 57}T600 ${game.height - 38}T800 ${game.height - 48}V${game.height}Z`} />
    {game.obstacles.map(obstacle => <g key={obstacle.id} className="flappy-scene-pipe" data-obstacle-id={obstacle.id}>
      <rect x={obstacle.x + 1.5} y={-2} width={obstacle.width - 3} height={obstacle.gapTop + .5} fill={`url(#${id}-pipe)`} />
      <rect className="flappy-scene-rim" x={obstacle.x + 1.5} y={obstacle.gapTop - 15} width={obstacle.width - 3} height={13.5} />
      <rect x={obstacle.x + 1.5} y={obstacle.gapBottom + 1.5} width={obstacle.width - 3} height={game.height - obstacle.gapBottom} fill={`url(#${id}-pipe)`} />
      <rect className="flappy-scene-rim" x={obstacle.x + 1.5} y={obstacle.gapBottom + 1.5} width={obstacle.width - 3} height={13.5} />
    </g>)}
    <path className="flappy-scene-ground" d={`M0 ${game.height - 1}H${game.width}`} />
    <g className={`flappy-scene-bird${game.phase !== 'playing' ? ' is-collided' : ''}`} transform={`translate(${FlappyRules.birdX} ${game.birdY}) rotate(${rotation})`} data-bird-y={game.birdY} data-tick={game.tick}>
      <ellipse className="flappy-scene-wing" cx={-5} cy={4} rx={8} ry={5} /><ellipse className="flappy-scene-body" rx={11} ry={9} fill={`url(#${id}-bird)`} />
      <path className="flappy-scene-beak" d="M9 -1 15 2 9 5Z" /><circle className="flappy-scene-eye" cx={4} cy={-3} r={3} /><circle cx={5} cy={-3} r={1.2} fill="#15222a" />
    </g>
  </svg>
}
