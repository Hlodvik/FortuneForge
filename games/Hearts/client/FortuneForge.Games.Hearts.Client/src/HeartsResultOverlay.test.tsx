// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import { HeartsResultOverlay } from './HeartsResultOverlay'

afterEach(cleanup)

describe('Hearts result overlay', () => {
  it('announces the labelled result, traps both Tab directions and does not dismiss on Escape', async () => {
    const user = userEvent.setup()
    render(<><button>Outside</button><HeartsResultOverlay label="Final Hearts result"><h2>You win!</h2><button>Results</button><button>Back to tables</button></HeartsResultOverlay></>)
    const dialog = screen.getByRole('dialog', { name: 'Final Hearts result' })
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(document.activeElement).toBe(dialog)
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Results' }))
    await user.tab({ shift: true })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Back to tables' }))
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Results' }))
    await user.keyboard('{Escape}')
    expect(screen.getByRole('dialog')).toBe(dialog)
    screen.getByRole('button', { name: 'Outside' }).focus()
    expect(document.activeElement).toBe(dialog)
  })

  it('keeps a pending result focused, discovers new actions and skips hidden/disabled controls', async () => {
    const user = userEvent.setup()
    const surface = (pending: boolean) => <HeartsResultOverlay label="Trial complete">{pending ? <p role="status">Confirming your trial…</p> : <><button disabled>Unavailable</button><span hidden><button>Hidden</button></span><span style={{ display: 'none' }}><button>Not displayed</button></span><button>Retry confirmation</button></>}</HeartsResultOverlay>
    const result = render(surface(true)), dialog = screen.getByRole('dialog')
    await user.tab()
    expect(document.activeElement).toBe(dialog)
    await user.tab({ shift: true })
    expect(document.activeElement).toBe(dialog)
    result.rerender(surface(false))
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Retry confirmation' }))
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Retry confirmation' }))
    result.rerender(surface(true))
    await waitFor(() => expect(document.activeElement).toBe(dialog))
  })

  it('restores a connected enabled trigger without altering page inert state', () => {
    const result = render(<main className="ff-hearts-page"><button>History</button></main>)
    const trigger = screen.getByRole('button', { name: 'History' })
    trigger.focus()
    result.rerender(<main className="ff-hearts-page"><button>History</button><HeartsResultOverlay label="Final result"><button>Back to tables</button></HeartsResultOverlay></main>)
    expect(document.activeElement).toBe(screen.getByRole('dialog'))
    expect(trigger.closest('[inert]')).toBeNull()
    result.rerender(<main className="ff-hearts-page"><button>History</button></main>)
    expect(document.activeElement).toBe(trigger)
  })

  it('uses an enabled lobby action when the former card no longer exists', () => {
    const result = render(<main className="ff-hearts-page"><button>Play a card</button></main>)
    screen.getByRole('button').focus()
    result.rerender(<main className="ff-hearts-page"><HeartsResultOverlay label="Final result"><button>Back to tables</button></HeartsResultOverlay></main>)
    result.rerender(<main className="ff-hearts-page"><div className="ff-hearts-lobby"><button disabled>Play competitive</button><button>Play free</button></div></main>)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Play free' }))
  })

  it('yields to a later Help dialog, then regains its focus trap once Help closes', async () => {
    const user = userEvent.setup()
    const surface = (help: boolean) => <><HeartsResultOverlay label="Final result"><button>Back to tables</button></HeartsResultOverlay>{help && <div role="dialog" aria-modal="true" aria-label="How to play"><button>Close Help</button></div>}</>
    const result = render(surface(false))
    result.rerender(surface(true))
    const close = screen.getByRole('button', { name: 'Close Help' })
    act(() => close.focus())
    expect(document.activeElement).toBe(close)
    result.rerender(surface(false))
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Back to tables' }))
  })

  it('does not steal focus when a result arrives underneath an open Help dialog', () => {
    const surface = (complete: boolean) => <>{complete && <HeartsResultOverlay label="Final result"><button>Back to tables</button></HeartsResultOverlay>}<div key="help" role="dialog" aria-modal="true" aria-label="How to play"><button>Close Help</button></div></>
    const result = render(surface(false)), close = screen.getByRole('button', { name: 'Close Help' })
    close.focus()
    result.rerender(surface(true))
    expect(document.activeElement).toBe(close)
  })
})
