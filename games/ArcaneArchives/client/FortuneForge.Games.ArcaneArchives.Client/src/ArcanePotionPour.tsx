import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import type { PotionArtwork } from './potionArtwork'

type Point = { x: number; y: number }
type BottleImages = { full: HTMLImageElement; empty: HTMLImageElement }
type PourProps = {
  art: PotionArtwork
  reels: Element | null
  origin: Point | null
  onImpact: () => void
  onComplete: () => void
}

const artworkSize = 224
const impactTime = 1.35
const finishTime = 2.61
const clamp = (value: number, minimum: number, maximum: number) => Math.max(minimum, Math.min(maximum, value))
const interpolate = (a: number, b: number, progress: number) => a + (b - a) * progress

function ease(start: number, end: number, time: number) {
  const phase = clamp((time - start) / (end - start), 0, 1)
  return phase * phase * (3 - 2 * phase)
}

async function loadImage(src: string) {
  const image = new Image()
  image.src = src
  await image.decode()
  return image
}

function paintBottle(
  context: CanvasRenderingContext2D, images: BottleImages, art: PotionArtwork,
  center: Point, size: number, angle: number, openness: number, drain: number,
) {
  const [mouthX, mouthY, lip] = art.mouth
  const scale = size / artworkSize
  const uncorked = openness > 0.001
  const cut = uncorked ? mouthY : 0

  function body(image: HTMLImageElement) {
    context.save()
    context.translate(center.x, center.y)
    context.rotate(-angle)
    context.drawImage(image, 0, cut, artworkSize, artworkSize - cut,
      -size / 2, cut * scale - size / 2, size, size - cut * scale)
    context.restore()
  }

  if (drain <= 0 || drain >= 1) {
    body(drain <= 0 ? images.full : images.empty)
  } else {
    const level = center.y + size * (interpolate(...art.drain, drain) - 0.5)
    for (const [image, top, height] of [[images.empty, -size, level + size],
      [images.full, level, context.canvas.height]] as const) {
      context.save()
      context.beginPath()
      context.rect(0, top, context.canvas.width, height)
      context.clip()
      body(image)
      context.restore()
    }
  }
  if (!uncorked) return

  context.save()
  context.translate(center.x, center.y)
  context.rotate(-angle)
  const x = (mouthX - artworkSize / 2) * scale
  const y = (mouthY - artworkSize / 2) * scale
  context.beginPath()
  context.ellipse(x, y, lip * scale, Math.max(1.1, 3.2 * scale), 0, 0, Math.PI * 2)
  context.fillStyle = '#211423'
  context.fill()
  context.lineWidth = Math.max(1, scale * 2)
  context.strokeStyle = '#d7af84'
  context.stroke()
  context.translate(-size * .096 * openness, -size * .10 * openness)
  context.drawImage(images.full, 0, 0, artworkSize, mouthY, -size / 2, -size / 2, size, mouthY * scale)
  context.restore()
}

function paintStream(
  context: CanvasRenderingContext2D, origin: Point, target: Point, time: number,
  size: number, art: PotionArtwork,
) {
  const head = ease(.93, impactTime, time)
  const tail = ease(1.56, 1.86, time)
  if (head <= tail) return
  const dx = target.x - origin.x
  const dy = target.y - origin.y
  const strength = ease(.93, 1.14, time) * (1 - ease(1.57, 1.86, time))
  const phase = (time - .9) * 20
  const points = Array.from({ length: 36 }, (_, index) => {
    const travel = index / 35
    const t = interpolate(tail, head, travel)
    const back = 1 - t
    const x = origin.x + dx * (3 * back * back * t * .28 + 3 * back * t * t * .71 + t ** 3)
    const y = origin.y + dy * (3 * back * back * t * .08 + 3 * back * t * t * .65 + t ** 3)
      + Math.sin(t * 22 - phase) * Math.sin(t * Math.PI) * size * .013
    const taper = ease(0, .10, travel) * (1 - .68 * ease(.91, 1, travel))
    const thickness = size * (.016 + taper * .023) * (.58 + .42 * strength)
      * (1 + .23 * Math.sin(t * 28 - phase * 1.13))
    return { x, y, thickness }
  })

  function ribbon(top: number, bottom: number, color: string, opacity: number) {
    context.beginPath()
    points.forEach((point, index) => {
      const y = point.y + point.thickness * top
      if (index === 0) context.moveTo(point.x, y)
      else context.lineTo(point.x, y)
    })
    for (let i = points.length - 1; i >= 0; i--) {
      const point = points[i]
      context.lineTo(point.x, point.y + point.thickness * bottom)
    }
    context.closePath()
    context.globalAlpha = opacity
    context.fillStyle = color
    context.fill()
  }

  context.save()
  ribbon(-1, 1, art.liquid[0], .94)
  ribbon(-.83, .71, art.liquid[1], .94)
  ribbon(-.59, -.19, art.liquid[2], .84)
  for (let index = 0; index < 7; index++) {
    const age = (time - 1.10 - index * .068) * (2.5 + index * .08)
    if (age <= 0 || age >= 1) continue
    const point = points[Math.min(35, 12 + index * 3)]
    const x = point.x - age * size * (.07 + (index % 3) * .026)
    const y = point.y + (age ** 1.7) * size * (.12 + (index % 4) * .033)
    const radius = Math.max(.8, size * .017) * (1 - age * .55)
    context.beginPath()
    context.globalAlpha = Math.sin(Math.PI * age) * .76
    context.fillStyle = art.liquid[index % 3]
    context.ellipse(x, y, radius, radius * .62, -.2, 0, Math.PI * 2)
    context.fill()
  }
  context.restore()
}

export function ArcanePotionPour({ art, reels, origin, onImpact, onComplete }: PourProps) {
  const surface = useRef<HTMLCanvasElement>(null)
  const callbacks = useRef({ onImpact, onComplete })
  callbacks.current = { onImpact, onComplete }

  useEffect(() => {
    const canvas = surface.current
    const context = canvas?.getContext('2d')
    if (!canvas || !context || !reels) {
      callbacks.current.onComplete()
      return
    }

    let frame = 0
    let closed = false
    let impact = false
    const finish = () => {
      if (closed) return
      closed = true
      cancelAnimationFrame(frame)
      callbacks.current.onComplete()
    }
    const deadline = window.setTimeout(finish, 4500)
    void Promise.all([loadImage(art.bottle), loadImage(art.empty)]).then(([full, empty]) => {
      if (closed) return
      const start = performance.now()
      let previous = -Infinity
      function paint(now: number) {
        if (closed || !canvas || !context || !reels) return
        frame = requestAnimationFrame(paint)
        if (now - previous < 1000 / 40) return
        previous = now
        const elapsed = (now - start) / 1000
        if (elapsed >= finishTime) {
          finish()
          return
        }

        const width = document.documentElement.clientWidth
        const height = window.innerHeight
        const rim = reels.getBoundingClientRect()
        if (rim.width <= 0 || rim.bottom < 16 || rim.top > height - 16) {
          finish()
          return
        }
        const ratio = Math.min(window.devicePixelRatio || 1, 1.6)
        const pixelWidth = Math.round(width * ratio)
        const pixelHeight = Math.round(height * ratio)
        if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
          canvas.width = pixelWidth
          canvas.height = pixelHeight
        }
        context.setTransform(ratio, 0, 0, ratio, 0, 0)
        context.clearRect(0, 0, width, height)

        const liftedSize = clamp(rim.width * .20, 58, Math.min(130, width * .28))
        const hoverX = clamp(rim.right + liftedSize * .73, liftedSize * .6, width - liftedSize * .58)
        const goal = {
          x: clamp(hoverX - liftedSize * .55, Math.max(rim.left, 8), Math.min(rim.right + 3, width - 8)),
          y: clamp(rim.top + rim.height * .56, liftedSize * .65, height - 10),
        }
        const hoverY = Math.max(liftedSize * .55, goal.y - liftedSize * .23)
        const rest = origin ?? { x: hoverX, y: Math.min(height - 26, rim.bottom + liftedSize * .32) }
        const lift = ease(.05, .62, elapsed) * (1 - ease(1.89, 2.44, elapsed))
        const center = {
          x: interpolate(rest.x, hoverX, lift),
          y: interpolate(rest.y, hoverY, lift),
        }
        const size = liftedSize * interpolate(.56, 1, lift)
        const turn = .635 * ease(.39, .88, elapsed) * (1 - ease(1.72, 2.35, elapsed))
        const openness = ease(.53, .95, elapsed) * (1 - ease(1.94, 2.43, elapsed))
        const mouthX = (art.mouth[0] / artworkSize - .5) * size
        const mouthY = (art.mouth[1] / artworkSize - .5) * size
        const outlet = {
          x: center.x + mouthX * Math.cos(turn) + mouthY * Math.sin(turn),
          y: center.y - mouthX * Math.sin(turn) + mouthY * Math.cos(turn),
        }
        paintStream(context, outlet, goal, elapsed, size, art)
        context.globalAlpha = 1 - ease(2.43, finishTime, elapsed)
        paintBottle(context, { full, empty }, art, center, size, turn, openness, ease(.99, 1.78, elapsed))
        context.globalAlpha = 1
        if (!impact && elapsed >= impactTime) {
          impact = true
          callbacks.current.onImpact()
        }
      }
      frame = requestAnimationFrame(paint)
    }, finish)

    return () => {
      closed = true
      window.clearTimeout(deadline)
      cancelAnimationFrame(frame)
    }
  }, [art, origin, reels])

  return createPortal(
    <canvas className="arcane-potion-motion arcane-potion-motion__pour" ref={surface} aria-hidden="true" />,
    document.body,
  )
}
