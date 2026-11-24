// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { expect, it, vi } from 'vitest'
import { SpinButton } from './SpinButton'

it('avoids mouse focus that becomes a mute-key highlight while retaining keyboard focus and activation', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  const onSpin = vi.fn()
  try {
    await act(async () => root.render(<SpinButton onSpin={onSpin} />))
    const button = host.querySelector<HTMLButtonElement>('button')!
    const mouseDown = new MouseEvent('mousedown', { button: 0, bubbles: true, cancelable: true })
    button.dispatchEvent(mouseDown)
    expect(mouseDown.defaultPrevented).toBe(true)
    await act(async () => button.click())
    expect(onSpin).toHaveBeenCalledTimes(1)

    button.focus()
    const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })
    button.dispatchEvent(space)
    expect(space.defaultPrevented).toBe(false)
    expect(document.activeElement).toBe(button)
    await act(async () => button.click())
    expect(onSpin).toHaveBeenCalledTimes(2)

    const contextMouseDown = new MouseEvent('mousedown', { button: 2, bubbles: true, cancelable: true })
    button.dispatchEvent(contextMouseDown)
    expect(contextMouseDown.defaultPrevented).toBe(false)
  } finally {
    await act(async () => root.unmount())
    host.remove()
    vi.unstubAllGlobals()
  }
})
