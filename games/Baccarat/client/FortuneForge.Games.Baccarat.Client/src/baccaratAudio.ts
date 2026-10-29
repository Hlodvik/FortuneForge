import chipLay from './assets/chip-lay-1.ogg'

const muteKey = 'fortuneforge:baccarat:muted'
let activeChip: HTMLAudioElement | null = null

export function readBaccaratMuted(): boolean {
  try { return localStorage.getItem(muteKey) === 'true' } catch { return false }
}

export function writeBaccaratMuted(muted: boolean): void {
  try { localStorage.setItem(muteKey, String(muted)) } catch { /* Optional preference. */ }
  if (muted) stopBaccaratAudio()
}

export function playBaccaratChip(muted: boolean): void {
  if (muted || typeof Audio === 'undefined') return
  try {
    stopBaccaratAudio()
    const audio = new Audio(chipLay)
    audio.volume = .18
    activeChip = audio
    const release = () => { if (activeChip === audio) activeChip = null }
    audio.addEventListener('ended', release, { once: true })
    audio.addEventListener('error', release, { once: true })
    void audio.play()?.catch(release)
  } catch { /* Browser media restrictions must not affect chip placement. */ }
}

export function stopBaccaratAudio(): void {
  if (!activeChip) return
  const audio = activeChip
  activeChip = null
  try { audio.pause(); audio.currentTime = 0 } catch { /* Media might not be ready yet. */ }
}
