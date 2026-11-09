import { useLayoutEffect, useRef, useState, type ImgHTMLAttributes } from 'react'

type ResponsiveSlotImageProps = Omit<ImgHTMLAttributes<HTMLImageElement>, 'src' | 'srcSet' | 'sizes'> & {
  src: string
  imageSrcSet?: string
}

/** Opt-in responsive art. Legacy images retain their immediate, unchanged img markup. */
export function ResponsiveSlotImage({ imageSrcSet, ...props }: ResponsiveSlotImageProps) {
  return imageSrcSet
    ? <MeasuredSlotImage {...props} imageSrcSet={imageSrcSet} />
    : <img {...props} />
}

function MeasuredSlotImage({
  src,
  imageSrcSet,
  width,
  height,
  ...props
}: ResponsiveSlotImageProps & { imageSrcSet: string }) {
  const imageRef = useRef<HTMLImageElement>(null)
  const [measuredWidth, setMeasuredWidth] = useState<number | null>(null)
  const aspectRatio = typeof width === 'number' && typeof height === 'number' && width > 0 && height > 0
    ? width / height
    : 1

  useLayoutEffect(() => {
    const image = imageRef.current
    if (!image) return
    let active = true
    const measure = () => {
      if (!active) return
      let size = image.clientWidth
      // The reel's img box is rectangular. For contain, request pixels for the
      // rendered bitmap rather than the unused horizontal part of that box.
      if (image.clientHeight > 0 && getComputedStyle(image).objectFit === 'contain') {
        size = Math.min(size, image.clientHeight * aspectRatio)
      }
      if (size > 0) setMeasuredWidth(Math.ceil(size))
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(image)
    window.addEventListener('resize', measure)
    return () => {
      active = false
      observer?.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [src, imageSrcSet, aspectRatio])

  // Do not expose a fallback src or a w-srcset before sizes is known: the
  // browser otherwise assumes100vw and can fetch a second, unneeded variant.
  const ready = measuredWidth !== null
  return (
    <img
      {...props}
      width={width}
      height={height}
      ref={imageRef}
      sizes={ready ? `${measuredWidth}px` : undefined}
      srcSet={ready ? imageSrcSet : undefined}
      src={ready ? src : undefined}
    />
  )
}
