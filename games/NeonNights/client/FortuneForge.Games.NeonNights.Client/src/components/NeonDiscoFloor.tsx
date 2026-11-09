import { useEffect, useRef } from 'react'

type Point = { x: number; y: number }

const tileColors = [
  [17, 214, 251],
  [251, 36, 184],
  [155, 48, 188],
  [64, 110, 226],
  [245, 247, 252],
] as const
const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value))
const noise = (x: number, y: number) => {
  const value = Math.sin(x * 127.1 + y * 311.7) * 43758.5453
  return value - Math.floor(value)
}
const pulseCenters: Point[] = []
const pulseColorIndexes: number[] = []
const pulseEdgeBags: number[][] = []

function getPulseEdge(beat: number): number {
  const bagIndex = Math.floor(beat / 4)
  while (pulseEdgeBags.length <= bagIndex) {
    const index = pulseEdgeBags.length
    const edges = [0, 1, 2, 3] // top, right, bottom, left
    for (let position = edges.length - 1; position > 0; position -= 1) {
      const choice = Math.floor(noise(index * 17 + position * 7, index * 29 + position * 11) * (position + 1))
      const selected = edges[position]!
      edges[position] = edges[choice]!
      edges[choice] = selected
    }
    const previousBag = pulseEdgeBags[index - 1]
    if (previousBag && edges.every((edge, position) => edge === previousBag[position])) {
      [edges[1], edges[2]] = [edges[2]!, edges[1]!]
    }
    const previousLast = previousBag?.[3]
    if (edges[0] === previousLast) [edges[0], edges[1]] = [edges[1]!, edges[0]!]
    pulseEdgeBags.push(edges)
  }
  return pulseEdgeBags[bagIndex]![beat % 4]!
}

function getPulseColorIndex(beat: number): number {
  while (pulseColorIndexes.length <= beat) {
    const index = pulseColorIndexes.length
    if (index % 2 === 1) {
      pulseColorIndexes.push(4) // Snare: cool white.
      continue
    }
    const previousBass = pulseColorIndexes[index - 2]
    const choices = 4 - (previousBass === undefined ? 0 : 1)
    const pick = Math.floor(noise(index * 17 + 13, index * 5 + 7) * choices)
    pulseColorIndexes.push(previousBass === undefined || pick < previousBass ? pick : pick + 1)
  }
  return pulseColorIndexes[beat]!
}

function getPulseCenter(beat: number): Point {
  while (pulseCenters.length <= beat) {
    const index = pulseCenters.length
    const edge = getPulseEdge(index)
    const previous = pulseCenters[index - 1]
    let selected: Point | undefined
    let greatestSeparation = -1
    for (let attempt = 0; attempt < 4; attempt += 1) {
      const along = .28 + .44 * noise(index * 13 + attempt * 5 + 3, index * 7 + attempt * 11 + 11)
      const inset = (edge % 2 === 0 ? .13 : .19) + .08 * noise(index * 19 + attempt * 3 + 5, index * 23 + attempt * 13 + 17)
      const candidate = edge === 0 ? { x: along, y: inset }
        : edge === 1 ? { x: 1 - inset, y: along }
          : edge === 2 ? { x: along, y: 1 - inset }
            : { x: inset, y: along }
      const separation = previous ? Math.hypot(candidate.x - previous.x, candidate.y - previous.y) : 1
      if (separation > greatestSeparation) {
        selected = candidate
        greatestSeparation = separation
      }
      if (separation >= .52) break
    }
    pulseCenters.push(selected ?? { x: .5, y: .5 })
  }
  return pulseCenters[beat]!
}

function drawDiscoFloor(ctx: CanvasRenderingContext2D, width: number, height: number, time: number | null, bpm: number) {
  const floorShade = ctx.createLinearGradient(0, 0, 0, height)
  floorShade.addColorStop(0, '#080e22')
  floorShade.addColorStop(.55, '#0b1430')
  floorShade.addColorStop(1, '#160e2d')
  ctx.fillStyle = floorShade
  ctx.fillRect(0, 0, width, height)

  // A high overhead view: rows get deeper and columns spread as they approach us.
  const project = (u: number, v: number): Point => {
    const depth = Math.pow(v, 1.38)
    const halfWidth = width * (.52 + .36 * depth)
    return {
      x: width / 2 + (u - .5) * halfWidth * 2,
      y: height * (.035 + depth * 1.105),
    }
  }
  const columns = width < 600 ? 14 : width < 1100 ? 16 : 22
  const rows = height > width ? 18 : 14
  const beatInterval = 60 / bpm
  const latestBeat = time === null ? -1 : Math.floor(time / beatInterval)
  const pulses: Array<{ x: number; y: number; radius: number; strength: number; color: readonly [number, number, number]; beat: number }> = []
  // One new impact per bass/snare beat. Earlier impacts expand and fade for 2½ beats.
  for (let beat = Math.max(0, latestBeat - 2); beat <= latestBeat; beat += 1) {
    const ageInBeats = ((time ?? 0) - beat * beatInterval) / beatInterval
    if (ageInBeats < 0 || ageInBeats >= 2.5) continue
    const { x, y } = getPulseCenter(beat)
    const colorIndex = getPulseColorIndex(beat)
    const strength = ageInBeats < 2
      ? Math.pow(1 - ageInBeats / 2.4, 1.1)
      : .14 * (2.5 - ageInBeats) / .5
    pulses.push({
      x, y, strength, beat,
      radius: .025 + ([.005, 0, .01, 0, .006][colorIndex] ?? 0) + ageInBeats * .09,
      color: tileColors[colorIndex] ?? tileColors[0],
    })
  }

  for (let row = 0; row < rows; row += 1) {
    for (let column = 0; column < columns; column += 1) {
      const inset = .012
      const left = (column + inset) / columns
      const right = (column + 1 - inset) / columns
      const top = (row + inset) / rows
      const bottom = (row + 1 - inset) / rows
      const a = project(left, top)
      const b = project(right, top)
      const c = project(right, bottom)
      const d = project(left, bottom)
      const u = (column + .5) / columns
      const v = (row + .5) / rows
      let strongestLight = 0
      let tileColor: readonly [number, number, number] = tileColors[0]
      for (const pulse of pulses) {
        const dx = (u - pulse.x) * .9
        const dy = (v - pulse.y) * 1.1
        const distance = Math.hypot(dx, dy) + (noise(column + pulse.beat * 3, row + pulse.beat * 7) - .5) * .016
        const crestWidth = (pulse.beat % 2 === 0 ? .095 : .06) + pulse.radius * .045
        const crest = Math.exp(-Math.pow((distance - pulse.radius) / crestWidth, 2) * 1.35)
        const bloom = Math.exp(-Math.pow(distance / (pulse.radius + .045), 2) * 1.35)
        const stagger = pulse.beat % 2 === 0
          ? .78 + .22 * Math.sin(pulse.beat * beatInterval * 2.6 + noise(column, row) * 10)
          : .68 + .32 * Math.sin(pulse.beat * beatInterval * 2.6 + noise(column, row) * 10)
        const light = (crest * .78 + bloom * .28) * stagger * pulse.strength
        if (light > strongestLight) {
          strongestLight = light
          tileColor = pulse.color
        }
      }
      // Overlapping impacts never mix colors in an individual tile.
      const colorBoost = tileColor === tileColors[4] ? 1.5 : 1.3
      const brightness = clamp((strongestLight - .08) * 1.25 * colorBoost, 0, 1)
      const red = 10 + tileColor[0] * brightness
      const green = 19 + tileColor[1] * brightness
      const blue = 40 + tileColor[2] * brightness

      ctx.beginPath()
      ctx.moveTo(a.x, a.y)
      ctx.lineTo(b.x, b.y)
      ctx.lineTo(c.x, c.y)
      ctx.lineTo(d.x, d.y)
      ctx.closePath()
      ctx.fillStyle = `rgb(${Math.round(clamp(red, 0, 255))} ${Math.round(clamp(green, 0, 255))} ${Math.round(clamp(blue, 0, 255))})`
      if (brightness > .4) {
        ctx.shadowColor = ctx.fillStyle
        ctx.shadowBlur = brightness * 18
      }
      ctx.fill()
      ctx.shadowBlur = 0
      ctx.strokeStyle = `rgba(91, 147, 187, ${.055 + brightness * .09})`
      ctx.lineWidth = .8
      ctx.stroke()
      ctx.beginPath()
      ctx.moveTo(a.x + 2, a.y + 2)
      ctx.lineTo(b.x - 2, b.y + 2)
      ctx.strokeStyle = `rgba(216, 235, 255, ${.018 + brightness * .07})`
      ctx.lineWidth = 1
      ctx.stroke()
    }
  }

  const shine = ctx.createLinearGradient(0, height * .55, 0, height)
  shine.addColorStop(0, 'rgba(255, 255, 255, 0)')
  shine.addColorStop(.8, 'rgba(207, 128, 233, .045)')
  shine.addColorStop(1, 'rgba(17, 3, 29, .2)')
  ctx.fillStyle = shine
  ctx.fillRect(0, height * .55, width, height * .45)
}

export function NeonDiscoFloor({ bpm, getPlaybackSeconds }: { bpm: number; getPlaybackSeconds: () => number | null }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')
    if (!canvas || !ctx) return

    const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)')
    let frame = 0
    let lastDraw = -Infinity

    const render = (timestamp: number) => {
      if (timestamp - lastDraw >= 1000 / 24) {
        lastDraw = timestamp
        drawDiscoFloor(ctx, canvas.clientWidth, canvas.clientHeight, motionPreference.matches ? .48 : getPlaybackSeconds(), bpm)
      }
      if (!motionPreference.matches) frame = window.requestAnimationFrame(render)
    }
    const resize = () => {
      const pixelRatio = Math.min(window.devicePixelRatio || 1, 2)
      canvas.width = Math.max(1, Math.round(canvas.clientWidth * pixelRatio))
      canvas.height = Math.max(1, Math.round(canvas.clientHeight * pixelRatio))
      ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0)
      lastDraw = -Infinity
      if (motionPreference.matches) render(0)
    }
    const restart = () => {
      window.cancelAnimationFrame(frame)
      lastDraw = -Infinity
      render(0)
    }
    const observer = new ResizeObserver(resize)
    observer.observe(canvas)
    motionPreference.addEventListener('change', restart)
    resize()
    render(0)

    return () => {
      window.cancelAnimationFrame(frame)
      observer.disconnect()
      motionPreference.removeEventListener('change', restart)
    }
  }, [bpm, getPlaybackSeconds])

  return <canvas ref={canvasRef} className="neon-disco-floor" aria-hidden="true" />
}
