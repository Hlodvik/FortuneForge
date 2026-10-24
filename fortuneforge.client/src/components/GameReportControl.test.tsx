// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GameReportControl } from './GameReportControl'
import { GameReportError, type sendGameReport } from '../features/reports/gameReportsApi'

let host: HTMLDivElement, root: Root
const receipt = { id: 'a'.repeat(64), status: 'open' as const, createdAtUtc: '2026-10-24T18:00:00Z' }
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
  history.replaceState(null, '', '/games/dragon-tiger?private=not-collected#private')
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals() })
const flush = () => act(async () => { await Promise.resolve(); await Promise.resolve() })
async function setup(send: typeof sendGameReport) { await act(async () => root.render(<GameReportControl gameTitle="Dragon Tiger" send={send}/>)); await act(async () => host.querySelector<HTMLButtonElement>('button[aria-label="Report this game"]')!.click()) }
async function message(text: string) { const input = host.querySelector('textarea')!; await act(async () => { Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(input, text); input.dispatchEvent(new Event('input', { bubbles: true })) }) }
async function submit() { await act(async () => host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))); await flush() }
describe('shared game report control', () => {
  it('opens an accessible form, sends the game path without query/fragment and confirms receipt', async () => {
    const send = vi.fn<typeof sendGameReport>().mockResolvedValue(receipt)
    await setup(send); expect(host.querySelector('dialog')?.hasAttribute('open')).toBe(true)
    expect(host.textContent).toContain('No screenshots or device telemetry')
    expect(host.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true)
    await message('The cards stopped responding after I clicked Deal.'); await submit()
    expect(send).toHaveBeenCalledOnce()
    expect(send.mock.calls[0][0]).toEqual({ category: 'bug', message: 'The cards stopped responding after I clicked Deal.', path: '/games/dragon-tiger' })
    expect(host.textContent).toContain('Your report has been saved')
    expect(send.mock.calls[0][1]).toMatch(/^game-report-/)
  })
  it('retries uncertain delivery with the same immutable body and key', async () => {
    const send = vi.fn<typeof sendGameReport>().mockRejectedValueOnce(new TypeError('Connection lost')).mockResolvedValueOnce(receipt)
    await setup(send); await message('A detailed suggestion for the next version.'); await submit()
    expect(host.querySelector<HTMLTextAreaElement>('textarea')!.readOnly).toBe(true)
    const first = send.mock.calls[0]
    await submit()
    expect(send.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2))
    expect(host.textContent).toContain('Your report has been saved')
  })
  it('lets the player correct a definitively rejected message', async () => {
    const send = vi.fn<typeof sendGameReport>().mockRejectedValueOnce(new GameReportError('Please remove the invalid character.', 400))
    await setup(send); await message('A report rejected by validation.'); await submit()
    expect(host.querySelector<HTMLTextAreaElement>('textarea')!.readOnly).toBe(false)
    expect(host.textContent).toContain('Please remove the invalid character.')
  })
  it('bounds a stalled send, retains its key and allows retry without a duplicate request', async () => {
    const send = vi.fn<typeof sendGameReport>().mockReturnValueOnce(new Promise(() => undefined)).mockResolvedValueOnce(receipt)
    await setup(send); await message('The deal button is stuck on my game.')
    vi.useFakeTimers(); await submit(); const first = send.mock.calls[0]
    await submit(); expect(send).toHaveBeenCalledOnce()
    await act(async () => { await vi.advanceTimersByTimeAsync(20_000) })
    expect(first[2]?.aborted).toBe(true)
    expect(host.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false)
    await submit(); expect(send.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2))
  })
})
