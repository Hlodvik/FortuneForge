import { useCallback, useRef } from 'react'
import slotBackgroundVideoMp4 from '../assets/slots/backgrounds/background-clouds-animated.mp4'
import slotBackgroundVideoWebm from '../assets/slots/backgrounds/background-clouds-animated.webm'
import { findSlotRoute } from '../games/slots'
import { AppRoutes } from './AppRoutes'
import { usePageTitle } from './usePageTitle'
import './styles/index.css'

export default function App() {
  const pathname = window.location.pathname.replace(/\/+$/, '') || '/'
  const backgroundVideoRef = useRef<HTMLVideoElement | null>(null)
  const userAgent = window.navigator?.userAgent ?? ''
  const isGoogleChrome =
    /\bChrome\//.test(userAgent) && !/\b(?:Edg|OPR)\//.test(userAgent)
  const slotRoute = findSlotRoute(pathname)
  const usesThemeSpecificSlotBackdrop =
    slotRoute?.shellBackdrop === 'theme'
  const shouldRenderDefaultShellVideo =
    slotRoute?.shellBackdrop === 'default-clouds'
  const appShellClassName = [
    'app-shell',
    pathname.startsWith('/slots/') ? 'app-shell--slot-game' : '',
    pathname === '/cards' || pathname.startsWith('/cards/') || pathname === '/demo/cards' || pathname.startsWith('/demo/cards/')
      ? 'app-shell--card-room'
      : '',
    usesThemeSpecificSlotBackdrop ? 'app-shell--themed-slot-backdrop' : '',
  ]
    .filter(Boolean)
    .join(' ')

  const handleSlotSpinStateChange = useCallback(
    (isSpinning: boolean) => {
      const video = backgroundVideoRef.current
      if (!video || !isGoogleChrome) return

      if (isSpinning) {
        video.pause()
        return
      }

      void video.play().catch(() => undefined)
    },
    [isGoogleChrome],
  )

  usePageTitle(pathname)

  return (
    <div className={appShellClassName}>
      {shouldRenderDefaultShellVideo && (
        <video
          ref={backgroundVideoRef}
          className="app-shell__background-video"
          autoPlay
          loop
          muted
          playsInline
          preload="auto"
          aria-hidden="true"
        >
          <source
            src={slotBackgroundVideoMp4}
            type="video/mp4"
            media="(prefers-reduced-motion: no-preference)"
          />
          <source
            src={slotBackgroundVideoWebm}
            type="video/webm"
            media="(prefers-reduced-motion: no-preference)"
          />
        </video>
      )}
      <AppRoutes
        pathname={pathname}
        slotRoute={slotRoute}
        onSpinStateChange={handleSlotSpinStateChange}
      />
    </div>
  )
}
