export type FlapIntent = Readonly<{ flap: boolean; preventDefault: boolean }>

export function keyboardFlapIntent(code: string, repeat: boolean, playing: boolean): FlapIntent {
  const activeSpace = playing && code === 'Space'
  return { flap: activeSpace && !repeat, preventDefault: activeSpace }
}

export function pointerFlapIntent(button: number, playing: boolean): FlapIntent {
  const flap = playing && button === 0
  return { flap, preventDefault: flap }
}
