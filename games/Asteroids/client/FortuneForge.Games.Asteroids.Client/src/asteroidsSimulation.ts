/** Pure browser mirror of FortuneForge.Games.Asteroids.AsteroidsEngine. */
export const AsteroidsControl = { None: 0, Thrust: 1, TurnLeft: 2, TurnRight: 4, Fire: 8 } as const
export type AsteroidsControl = number
export type AsteroidsPhase = 'playing' | 'game-over'
export type AsteroidSize = 'tiny' | 'small' | 'medium' | 'large' | 'huge'
export type AsteroidKind = 'drifter' | 'hunter'
export type PowerUpType = 'shield' | 'rapid-fire' | 'extra-life'
export type AsteroidsVector = Readonly<{ x: number; y: number }>
export type AsteroidsShipSimulation = Readonly<{ position: AsteroidsVector; velocity: AsteroidsVector; angle: number; invulnerabilityTicks: number; thrustTicks: number }>
export type AsteroidSimulation = Readonly<{ id: number; position: AsteroidsVector; velocity: AsteroidsVector; radius: number; size: AsteroidSize; hitPoints: number; spriteVariant: number; kind: AsteroidKind }>
export type BulletSimulation = Readonly<{ id: number; position: AsteroidsVector; velocity: AsteroidsVector; remainingTicks: number }>
export type PowerUpSimulation = Readonly<{ id: number; position: AsteroidsVector; velocity: AsteroidsVector; type: PowerUpType; remainingTicks: number }>
export type AsteroidsSimulationState = Readonly<{
  width: number; height: number; seed: number; randomState: number; ship: AsteroidsShipSimulation
  asteroids: readonly AsteroidSimulation[]; bullets: readonly BulletSimulation[]; powerUps: readonly PowerUpSimulation[]
  nextEntityId: number; fireCooldownTicks: number; rapidFireTicks: number; score: number; bestScore: number
  lives: number; wave: number; tick: number; phase: AsteroidsPhase; event: string; scoreGained: number; message: string
}>

const maxShipSpeed = 7, thrustPower = 0.22, bulletSpeed = 10, bulletLifetime = 80, fireCooldown = 8, rapidFireCooldown = 2
const rapidFireTicks = 300, shieldTicks = 240, powerUpLifetime = 2_147_483_647, powerUpRadius = 18, shipRadius = 12
const shipInvulnerabilityTicks = 120, thrustVisualTicks = 3, maxAsteroidSpeed = 3.08
export const hunterAcceleration = 0.035, hunterMaxSpeed = 2.6, hunterScoreBonus = 75

export function foldPaidSeedHex(seedHex: string): number {
  if (!/^[0-9a-f]{16}$/.test(seedHex)) throw new Error('Asteroids paid seed must be 16 lowercase hexadecimal characters.')
  const seed = BigInt(`0x${seedHex}`)
  if (seed === 0n) throw new Error('Asteroids paid seed must be non-zero.')
  return (Number(seed & 0xffff_ffffn) ^ Number(seed >> 32n)) >>> 0
}

export function startAsteroidsSimulation(seed: number, width = 800, height = 600, bestScore = 0): AsteroidsSimulationState {
  if (!Number.isInteger(seed) || seed < 0 || seed > 0xffff_ffff || width < 400 || width > 1600 || height < 300 || height > 1200 || bestScore < 0) throw new Error('Asteroids simulation start parameters are invalid.')
  let random = normalizeSeed(seed)
  let nextEntityId = 1
  const ship: AsteroidsShipSimulation = { position: vector(width / 2, height / 2), velocity: vector(0, 0), angle: -Math.PI / 2, invulnerabilityTicks: 0, thrustTicks: 0 }
  const spawned = spawnWave(1, ship.position, width, height, random, nextEntityId)
  random = spawned.random; nextEntityId = spawned.nextEntityId
  return { width, height, seed, randomState: random, ship, asteroids: spawned.asteroids, bullets: [], powerUps: [], nextEntityId, fireCooldownTicks: 0, rapidFireTicks: 0, score: 0, bestScore, lives: 3, wave: 1, tick: 0, phase: 'playing', event: 'started', scoreGained: 0, message: '' }
}

export function advanceAsteroidsFrame(state: AsteroidsSimulationState, controls: AsteroidsControl): AsteroidsSimulationState {
  validateControl(controls)
  if (state.phase === 'game-over') throw new Error('This Asteroids game is over. Start a new game to play again.')
  let next = state
  if ((controls & AsteroidsControl.TurnLeft) !== 0) next = rotate(next, -0.10)
  else if ((controls & AsteroidsControl.TurnRight) !== 0) next = rotate(next, 0.10)
  if ((controls & AsteroidsControl.Thrust) !== 0) next = thrust(next)
  if ((controls & AsteroidsControl.Fire) !== 0) next = fire(next)
  return tick(next)
}

export function replayAsteroidsSimulation(seed: number, totalSteps: number, commands: readonly Readonly<{ step: number; control: AsteroidsControl }>[]): AsteroidsSimulationState {
  if (!Number.isInteger(totalSteps) || totalSteps < 1) throw new Error('Asteroids replay duration is invalid.')
  let priorStep = -1, control: AsteroidsControl = AsteroidsControl.None, index = 0
  for (const command of commands) {
    validateControl(command.control)
    if (!Number.isInteger(command.step) || command.step < 0 || command.step >= totalSteps || command.step <= priorStep || command.control === control) throw new Error('Asteroids replay commands must be canonical transitions.')
    priorStep = command.step; control = command.control
  }
  let state = startAsteroidsSimulation(seed)
  control = AsteroidsControl.None
  for (let step = 0; step < totalSteps; step++) {
    if (state.phase === 'game-over') {
      if (index < commands.length) throw new Error('Asteroids replay commands cannot follow game over.')
      return state
    }
    if (commands[index]?.step === step) control = commands[index++]!.control
    state = advanceAsteroidsFrame(state, control)
  }
  return state
}

function rotate(state: AsteroidsSimulationState, amount: number): AsteroidsSimulationState { return { ...state, ship: { ...state.ship, angle: state.ship.angle + amount }, event: 'rotated', scoreGained: 0, message: amount < 0 ? 'Rotated left.' : 'Rotated right.' } }
function thrust(state: AsteroidsSimulationState): AsteroidsSimulationState {
  const velocity = clampSpeed(add(state.ship.velocity, scale(forward(state.ship.angle), thrustPower)), maxShipSpeed)
  return { ...state, ship: { ...state.ship, velocity, thrustTicks: thrustVisualTicks }, event: 'thrusted', scoreGained: 0, message: 'Thrusters engaged.' }
}
function fire(state: AsteroidsSimulationState): AsteroidsSimulationState {
  if (state.fireCooldownTicks > 0) return { ...state, event: 'no-op', scoreGained: 0, message: 'Weapons are cooling down.' }
  const direction = forward(state.ship.angle)
  const bullet: BulletSimulation = { id: state.nextEntityId, position: clampToField(add(state.ship.position, scale(direction, 18)), state.width, state.height, 0), velocity: add(state.ship.velocity, scale(direction, bulletSpeed)), remainingTicks: bulletLifetime }
  return { ...state, bullets: [...state.bullets, bullet], nextEntityId: state.nextEntityId + 1, fireCooldownTicks: state.rapidFireTicks > 0 ? rapidFireCooldown : fireCooldown, event: 'fired', scoreGained: 0, message: 'Photon torpedo fired.' }
}
function tick(state: AsteroidsSimulationState): AsteroidsSimulationState {
  const requested = add(state.ship.position, state.ship.velocity), position = clampToField(requested, state.width, state.height, shipRadius), decelerated = scale(state.ship.velocity, 0.995)
  let ship: AsteroidsShipSimulation = { ...state.ship, position, velocity: vector(position.x === requested.x ? decelerated.x : 0, position.y === requested.y ? decelerated.y : 0), invulnerabilityTicks: Math.max(0, state.ship.invulnerabilityTicks - 1), thrustTicks: Math.max(0, state.ship.thrustTicks - 1) }
  let asteroids = state.asteroids.map(asteroid => moveAsteroid(asteroid, ship.position, state.width, state.height))
  const moving = state.bullets.map(bullet => ({ previous: bullet.position, value: { ...bullet, position: add(bullet.position, bullet.velocity), remainingTicks: bullet.remainingTicks - 1 } })).filter(bullet => bullet.value.remainingTicks > 0 && inside(bullet.value.position, state.width, state.height))
  let powerUps = state.powerUps.map(powerUp => ({ ...powerUp, position: add(powerUp.position, powerUp.velocity) })).filter(powerUp => inside(powerUp.position, state.width, state.height))
  let random = state.randomState, nextEntityId = state.nextEntityId, scoreGained = 0, hitCount = 0, destroyedCount = 0
  const bullets: BulletSimulation[] = []
  for (const bullet of moving) {
    const hitIndex = asteroids.findIndex(asteroid => segmentIntersectsCircle(bullet.previous, bullet.value.position, asteroid.position, asteroid.radius))
    if (hitIndex < 0) { bullets.push(bullet.value); continue }
    const hit = asteroids[hitIndex]!; hitCount++
    if (hit.hitPoints > 1) asteroids[hitIndex] = { ...hit, hitPoints: hit.hitPoints - 1 }
    else {
      asteroids.splice(hitIndex, 1); scoreGained += scoreFor(hit.size) + (hit.kind === 'hunter' ? hunterScoreBonus : 0); destroyedCount++
      const splitResult = split(hit, random, nextEntityId); random = splitResult.random; nextEntityId = splitResult.nextEntityId; asteroids.push(...splitResult.asteroids)
      const powerResult = trySpawnPowerUp(hit.position, random, nextEntityId, powerUps); random = powerResult.random; nextEntityId = powerResult.nextEntityId; powerUps = powerResult.powerUps
    }
  }
  let lives = state.lives, phase: AsteroidsPhase = 'playing', rapidTicks = Math.max(0, state.rapidFireTicks - 1)
  let event = hitCount > 0 ? 'hit' : 'ticked', message = destroyedCount > 0 ? `Destroyed ${destroyedCount} asteroid${destroyedCount === 1 ? '' : 's'}.` : hitCount > 0 ? `Damaged ${hitCount} asteroid${hitCount === 1 ? '' : 's'}.` : ''
  const collected = powerUps.filter(powerUp => distance(ship.position, powerUp.position) <= powerUpRadius + shipRadius)
  if (collected.length > 0) {
    for (const powerUp of collected) {
      if (powerUp.type === 'shield') ship = { ...ship, invulnerabilityTicks: Math.max(ship.invulnerabilityTicks, shieldTicks) }
      else if (powerUp.type === 'rapid-fire') rapidTicks = Math.max(rapidTicks, rapidFireTicks)
      else lives = Math.min(5, lives + 1)
    }
    powerUps = powerUps.filter(powerUp => !collected.includes(powerUp)); event = 'power-up-collected'; message = powerMessage(collected.at(-1)!.type)
  }
  if (ship.invulnerabilityTicks === 0 && asteroids.some(asteroid => distance(ship.position, asteroid.position) <= asteroid.radius + shipRadius)) {
    lives = Math.max(0, lives - 1); ship = { ...ship, velocity: vector(0, 0), invulnerabilityTicks: shipInvulnerabilityTicks, thrustTicks: 0 }; bullets.length = 0
    if (lives === 0) { phase = 'game-over'; event = 'game-over'; message = 'The ship was destroyed. Game over.' } else { event = 'damaged'; message = `Ship damaged. ${lives} ${lives === 1 ? 'life' : 'lives'} remaining.` }
  }
  let wave = state.wave
  if (phase === 'playing' && asteroids.length === 0) {
    wave++; const spawned = spawnWave(wave, ship.position, state.width, state.height, random, nextEntityId); asteroids = spawned.asteroids; random = spawned.random; nextEntityId = spawned.nextEntityId
    const bonus = wave * 100, hunters = hunterCountFor(wave); scoreGained += bonus; event = 'wave-cleared'; message = hunters > 0 ? `Wave ${wave - 1} cleared. Wave ${wave} incoming · ${hunters} hunter${hunters === 1 ? '' : 's'} tracking · +${bonus} points.` : `Wave ${wave - 1} cleared. Wave ${wave} incoming · +${bonus} points.`
  }
  const score = state.score + scoreGained
  return { ...state, randomState: random, ship, asteroids, bullets, powerUps, nextEntityId, fireCooldownTicks: Math.max(0, state.fireCooldownTicks - 1), rapidFireTicks: rapidTicks, score, bestScore: Math.max(state.bestScore, score), lives, wave, tick: state.tick + 1, phase, event, scoreGained, message }
}

function spawnWave(wave: number, ship: AsteroidsVector, width: number, height: number, random: number, nextEntityId: number) {
  const asteroids: AsteroidSimulation[] = [], hunterCount = hunterCountFor(wave)
  for (let index = 0; index < Math.min(18, 4 + wave); index++) {
    const size = initialSize(wave, index), radius = radiusFor(size); let position = vector(0, 0)
    for (let attempt = 0; ; attempt++) { const x = randomRange(random, radius, width - radius); random = x.random; const y = randomRange(random, radius, height - radius); random = y.random; position = vector(x.value, y.value); if (distance(position, ship) >= 150 || attempt >= 64) break }
    const angleResult = randomRange(random, 0, Math.PI * 2); random = angleResult.random
    const range = size === 'huge' ? [0.12, 0.55] : size === 'large' ? [0.25, 1.05] : size === 'medium' ? [0.5, 1.65] : size === 'small' ? [0.9, 2.35] : [1.3, 3.02]
    const base = randomRange(random, range[0], range[1]); random = base.random
    const speed = Math.min(maxAsteroidSpeed, base.value + Math.min(0.6, Math.max(0, wave - 1) * 0.06))
    asteroids.push({ id: nextEntityId++, position, velocity: vector(Math.cos(angleResult.value) * speed, Math.sin(angleResult.value) * speed), radius, size, hitPoints: hitPointsFor(size), spriteVariant: (wave * 3 + index * 2) % 5, kind: index < hunterCount ? 'hunter' : 'drifter' })
  }
  return { asteroids, random, nextEntityId }
}
function split(hit: AsteroidSimulation, random: number, nextEntityId: number) {
  if (hit.size === 'tiny') return { asteroids: [] as AsteroidSimulation[], random, nextEntityId }
  const size: AsteroidSize = hit.size === 'huge' ? 'large' : hit.size === 'large' ? 'medium' : hit.size === 'medium' ? 'small' : 'tiny', variants = splitVariants(hit.spriteVariant), asteroids: AsteroidSimulation[] = [], baseAngle = Math.atan2(hit.velocity.y, hit.velocity.x)
  for (let index = 0; index < 2; index++) { const drift = randomRange(random, -0.18, 0.18); random = drift.random; const speed = randomRange(random, 0.25, 0.85); random = speed.random; const velocityLength = Math.min(maxAsteroidSpeed, Math.max(0.45, length(hit.velocity) + speed.value)); const angle = baseAngle + (index === 0 ? -0.65 : 0.65) + drift.value; asteroids.push({ id: nextEntityId++, position: hit.position, velocity: vector(Math.cos(angle) * velocityLength, Math.sin(angle) * velocityLength), radius: radiusFor(size), size, hitPoints: hitPointsFor(size), spriteVariant: variants[index]!, kind: 'drifter' }) }
  return { asteroids, random, nextEntityId }
}
function trySpawnPowerUp(position: AsteroidsVector, random: number, nextEntityId: number, powerUps: readonly PowerUpSimulation[]) {
  const chance = randomRange(random, 0, 1); random = chance.random; if (chance.value > 0.18) return { powerUps: [...powerUps], random, nextEntityId }
  const kind = randomRange(random, 0, 1); random = kind.random; const type: PowerUpType = kind.value < 0.4 ? 'shield' : kind.value < 0.75 ? 'rapid-fire' : 'extra-life'
  const angle = randomRange(random, 0, Math.PI * 2); random = angle.random
  return { powerUps: [...powerUps, { id: nextEntityId++, position, velocity: vector(Math.cos(angle.value) * 0.8, Math.sin(angle.value) * 0.8), type, remainingTicks: powerUpLifetime }], random, nextEntityId }
}
function moveAsteroid(asteroid: AsteroidSimulation, ship: AsteroidsVector, width: number, height: number): AsteroidSimulation { let velocity = asteroid.velocity; if (asteroid.kind === 'hunter') { const pursuit = vector(ship.x - asteroid.position.x, ship.y - asteroid.position.y), pursuitLength = length(pursuit); if (pursuitLength > 0) velocity = clampSpeed(add(velocity, scale(pursuit, hunterAcceleration / pursuitLength)), hunterMaxSpeed) } const requested = add(asteroid.position, velocity), position = clampToField(requested, width, height, asteroid.radius); return { ...asteroid, position, velocity: vector(position.x === requested.x ? velocity.x : -velocity.x, position.y === requested.y ? velocity.y : -velocity.y) } }
function validateControl(control: number): void { const known = AsteroidsControl.Thrust | AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight | AsteroidsControl.Fire; if (!Number.isInteger(control) || (control & ~known) !== 0 || (control & (AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight)) === (AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight)) throw new Error('Asteroids control contains unknown or contradictory bits.') }
function normalizeSeed(seed: number): number { return seed === 0 ? 0xA341316C : seed >>> 0 }
function nextRandom(value: number): number { value >>>= 0; if (value === 0) value = 0xA341316C; value = (value ^ (value << 13)) >>> 0; value = (value ^ (value >>> 17)) >>> 0; return (value ^ (value << 5)) >>> 0 }
function randomRange(random: number, minimum: number, maximum: number) { random = nextRandom(random); return { value: minimum + (random / 0xffff_ffff) * (maximum - minimum), random } }
function vector(x: number, y: number): AsteroidsVector { return { x, y } }
function add(a: AsteroidsVector, b: AsteroidsVector): AsteroidsVector { return vector(a.x + b.x, a.y + b.y) }
function scale(value: AsteroidsVector, factor: number): AsteroidsVector { return vector(value.x * factor, value.y * factor) }
function length(value: AsteroidsVector): number { return Math.sqrt(value.x * value.x + value.y * value.y) }
function distance(a: AsteroidsVector, b: AsteroidsVector): number { return length(vector(a.x - b.x, a.y - b.y)) }
function forward(angle: number): AsteroidsVector { return vector(Math.cos(angle), Math.sin(angle)) }
function clampSpeed(value: AsteroidsVector, maximum: number): AsteroidsVector { const current = length(value); return current <= maximum || current === 0 ? value : scale(value, maximum / current) }
function clampToField(position: AsteroidsVector, width: number, height: number, padding: number): AsteroidsVector { return vector(Math.min(Math.max(position.x, padding), width - padding), Math.min(Math.max(position.y, padding), height - padding)) }
function inside(position: AsteroidsVector, width: number, height: number): boolean { return position.x >= 0 && position.x <= width && position.y >= 0 && position.y <= height }
function segmentIntersectsCircle(start: AsteroidsVector, end: AsteroidsVector, center: AsteroidsVector, radius: number): boolean { const segment = vector(end.x - start.x, end.y - start.y), squared = segment.x * segment.x + segment.y * segment.y; if (squared === 0) return distance(start, center) <= radius; const toCenter = vector(center.x - start.x, center.y - start.y), projection = Math.min(Math.max((toCenter.x * segment.x + toCenter.y * segment.y) / squared, 0), 1); return distance(add(start, scale(segment, projection)), center) <= radius }
function initialSize(wave: number, index: number): AsteroidSize { return (wave + index) % 5 === 0 ? 'huge' : (wave + index) % 5 === 1 ? 'large' : (wave + index) % 5 === 2 ? 'medium' : (wave + index) % 5 === 3 ? 'small' : 'tiny' }
function hunterCountFor(wave: number): number { return wave < 2 ? 0 : Math.min(3, Math.floor(wave / 2)) }
function radiusFor(size: AsteroidSize): number { return size === 'huge' ? 52 : size === 'large' ? 40 : size === 'medium' ? 29 : size === 'small' ? 20 : 13 }
function hitPointsFor(size: AsteroidSize): number { return size === 'huge' ? 10 : size === 'large' ? 8 : size === 'medium' ? 6 : size === 'small' ? 4 : 2 }
function scoreFor(size: AsteroidSize): number { return size === 'huge' ? 20 : size === 'large' ? 35 : size === 'medium' ? 55 : size === 'small' ? 80 : 110 }
function splitVariants(variant: number): readonly [number, number] { const values: Record<number, readonly [number, number]> = { 0: [4, 2], 1: [5, 6], 2: [3, 0], 3: [2, 4], 4: [0, 3], 5: [2, 4], 6: [4, 3] }; const value = values[variant]; if (value === undefined) throw new Error('Unknown asteroid sprite variant.'); return value }
function powerMessage(type: PowerUpType): string { return type === 'shield' ? 'Shield activated.' : type === 'rapid-fire' ? 'Rapid fire online.' : 'Extra life secured.' }
