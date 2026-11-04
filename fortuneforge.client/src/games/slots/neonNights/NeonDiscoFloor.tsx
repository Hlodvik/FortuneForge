import { useEffect, useRef } from 'react'

export function NeonDiscoFloor() {
  const videoRef = useRef<HTMLVideoElement>(null)

  useEffect(() => {
    const video = videoRef.current
    if (!video) return
    video.playbackRate = 0.25
    void video.play().catch(() => undefined)
    return () => video.pause()
  }, [])

  return <video
    ref={videoRef}
    className="neon-disco-floor"
    src={`${import.meta.env.BASE_URL}assets/slots/neon-nights/light-up-dance-floor-v1.mp4`}
    autoPlay
    loop
    muted
    playsInline
    preload="auto"
    aria-hidden="true"
  />
}
