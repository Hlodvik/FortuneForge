// @vitest-environment jsdom
import { act, useRef, useState } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MidnightVaultGame } from '@fortuneforge/games-midnight-vault'
import { WinHelpDialog } from '../WinHelpDialog'
import { InsufficientBalanceDialog } from '../InsufficientBalanceDialog'
import { AudioSettingsDialog } from './AudioSettingsDialog'
import { SymbolValueGuide } from './SymbolValueGuide'
import { DRAGON_HOARD_SLOT_GAME } from '../../../games/slots/dragonHoard/manifest'

let host: HTMLDivElement
let root: Root
const symbols = DRAGON_HOARD_SLOT_GAME.experience.symbols
const noop = () => undefined
type DialogKind = 'help' | 'balance' | 'audio' | 'symbols'

function Harness({ kind }: { kind: DialogKind }) {
  const [open, setOpen] = useState(false)
  const closeRef = useRef<HTMLButtonElement>(null)
  const close = () => setOpen(false)
  return <>
    {kind !== 'symbols' && <button data-launcher onClick={() => setOpen(true)}>Open</button>}
    <button data-outside>Outside control</button>
    {kind === 'help' && <WinHelpDialog isOpen={open} onClose={close} closeButtonRef={closeRef}
      help={DRAGON_HOARD_SLOT_GAME.experience.help} symbolSet={symbols} theme="midnight-vault" />}
    {kind === 'balance' && <InsufficientBalanceDialog isOpen={open} onClose={close} closeButtonRef={closeRef}
      selectedWager={5} balance={1} recoveryHref="/balance" recoveryLabel="Add Rand" theme="midnight-vault" />}
    {kind === 'audio' && <AudioSettingsDialog isOpen={open} onClose={close} onToggleMuted={noop}
      onToggleResultsOnly={noop} onVolumeChange={noop} preferences={{ volume: 50, mode: 'all' }} theme="midnight-vault" />}
    {kind === 'symbols' && <SymbolValueGuide symbolSet={symbols} showValueTokens={false}
      isOpen={open} onOpenChange={setOpen} theme="midnight-vault" />}
  </>
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe.each(['help', 'balance', 'audio', 'symbols'] as const)('%s dialog', kind => {
  it('contains forward/reverse Tab, redirects escaped focus, and restores its opener on Escape', () => {
    act(() => root.render(<Harness kind={kind} />))
    const trigger = host.querySelector<HTMLButtonElement>('[data-launcher], .symbol-value-guide__trigger')!
    trigger.focus()
    act(() => trigger.click())
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!
    expect(dialog.dataset.slotDialogTheme).toBe('midnight-vault')
    const controls = Array.from(dialog.querySelectorAll<HTMLElement>('button, input, a[href]'))
    expect(document.activeElement).toBe(controls[0])
    expect(controls[0].querySelector('svg[viewBox="0 0 24 24"]')).not.toBeNull()
    expect(controls[0].textContent).not.toContain('×')

    act(() => controls[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })))
    expect(document.activeElement).toBe(controls.at(-1))
    act(() => controls.at(-1)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })))
    expect(document.activeElement).toBe(controls[0])
    act(() => dialog.focus())
    act(() => dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })))
    expect(document.activeElement).toBe(controls.at(-1))
    act(() => host.querySelector<HTMLButtonElement>('[data-outside]')!.focus())
    expect(dialog.contains(document.activeElement)).toBe(true)

    act(() => dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })))
    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
    expect(document.activeElement).toBe(trigger)
  })
})

it('keeps the Midnight Vault info trigger visually to a circle-i with an accessible name', async () => {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: noop, removeEventListener: noop })))
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined)
  vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(noop)
  await act(async () => root.render(<MidnightVaultGame />))
  const trigger = host.querySelector<HTMLButtonElement>('.symbol-value-guide__trigger')!
  expect(trigger.getAttribute('aria-label')).toBe('Symbol values')
  expect(trigger.querySelector('svg circle[cx="12"][cy="12"][r="9"]')).not.toBeNull()
  expect(trigger.textContent).toBe('')
})
