import { useCallback, useEffect, useRef } from 'react'
import type { useSlotAudio } from '@fortuneforge/slot-client-core'

type MusicAudio = Pick<ReturnType<typeof useSlotAudio>, 'preferences' | 'preloadLoop' | 'startLoop' | 'replaceLoop' | 'stopLoop'>
const base = 'ambience'
const percussion = 'percussion-ambience'

/** Arrangement progression is cosmetic and counts unique server-confirmed spins in this visit. */
export function usePharaohMusic(accountId: string | undefined, audio: MusicAudio) {
  const session = useRef({ accountId, started: false, spins: new Set<string>() })
  const { preloadLoop, startLoop, replaceLoop, stopLoop } = audio
  const play = useCallback(() => {
    const current = session.current
    if (current.accountId !== accountId || !current.started) return
    if (current.spins.size >= 4) replaceLoop(percussion, base, 350)
    else startLoop(base, 350)
  }, [accountId, startLoop, replaceLoop])
  const begin = useCallback(() => {
    if (session.current.accountId !== accountId) return
    session.current.started = true
    preloadLoop(base)
    preloadLoop(percussion)
    play()
  }, [accountId, preloadLoop, play])
  const confirmSpin = useCallback((spinId: string) => {
    const current = session.current
    if (current.accountId !== accountId) return
    if (current.spins.size < 4) current.spins.add(spinId)
    play()
  }, [accountId, play])

  useEffect(() => {
    session.current = { accountId, started: false, spins: new Set() }
    return () => { stopLoop(base); stopLoop(percussion) }
  }, [accountId, stopLoop])
  useEffect(() => {
    if (audio.preferences.mode === 'all') play()
  }, [audio.preferences.mode, play])
  return { begin, confirmSpin }
}
