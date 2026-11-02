import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useSlotDialog } from '../slots/hooks/useSlotDialog'
import { installGameNavigationHandler } from './gameLeaveNavigation'
import './GameLeaveGuard.css'

export type GameLeaveDetails = Readonly<{ title: string; description: string }>
type Register = (id: string, details: GameLeaveDetails) => () => void
const Registration = createContext<Register | null>(null)

/** Register accepted or unresolved participation, not local chip placement. */
export function useGameLeaveGuard(details: GameLeaveDetails | null) {
  const register = useContext(Registration)
  const id = useId()
  const title = details?.title, description = details?.description
  useEffect(() => {
    if (register && title && description) return register(id, { title, description })
  }, [register, id, title, description])
}

/** The gate only presents navigation choices. It never calls a game endpoint. */
export function GameLeaveGuardProvider({ children, navigate = href => window.location.assign(href) }: {
  children: ReactNode
  navigate?: (href: string) => void
}) {
  const [registrations, setRegistrations] = useState(() => new Map<string, GameLeaveDetails>())
  const [pending, setPending] = useState<{ run: () => void } | null>(null)
  const allowNextUnload = useRef(false)
  const permitTimer = useRef<number | null>(null)
  const slotRoute = typeof window !== 'undefined' && /^\/slots(?:\/|$)/.test(window.location.pathname)
  const details = [...registrations.values()].at(-1) ?? null
  const active = details !== null && !slotRoute
  const register = useCallback<Register>((id, value) => {
    setRegistrations(current => new Map(current).set(id, value))
    return () => setRegistrations(current => {
      const next = new Map(current); next.delete(id); return next
    })
  }, [])
  const ask = useCallback((run: () => void) => setPending(current => current ?? { run }), [])

  useEffect(() => {
    if (!active) return
    return installGameNavigationHandler(ask)
  }, [active, ask])

  useEffect(() => {
    if (!active) return
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (allowNextUnload.current) { allowNextUnload.current = false; return }
      event.preventDefault()
      event.returnValue = ''
    }
    const click = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const anchor = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>('a[href]') : null
      if (!anchor || anchor.hasAttribute('download') || (anchor.target && anchor.target !== '_self')) return
      const destination = new URL(anchor.href, window.location.href)
      if (!['http:', 'https:'].includes(destination.protocol)) return
      const current = window.location
      if (destination.origin === current.origin && destination.pathname === current.pathname && destination.search === current.search) return
      event.preventDefault()
      event.stopPropagation()
      ask(() => navigate(destination.href))
    }
    window.addEventListener('beforeunload', beforeUnload)
    document.addEventListener('click', click, true)
    return () => {
      window.removeEventListener('beforeunload', beforeUnload)
      document.removeEventListener('click', click, true)
    }
  }, [active, ask, navigate])

  const stay = useCallback(() => setPending(null), [])
  const confirm = useCallback(() => {
    if (!pending || !active) return
    setPending(null)
    allowNextUnload.current = true
    // The synchronous confirmed navigation may unload without asking twice.
    // A no-op/failed action must not disable protection for a later departure.
    if (permitTimer.current !== null) window.clearTimeout(permitTimer.current)
    permitTimer.current = window.setTimeout(() => { allowNextUnload.current = false; permitTimer.current = null }, 0)
    try { pending.run() }
    catch (error) { allowNextUnload.current = false; throw error }
  }, [active, pending])
  useEffect(() => {
    if (!active) { allowNextUnload.current = false; setPending(null) }
  }, [active])
  useEffect(() => () => {
    if (permitTimer.current !== null) window.clearTimeout(permitTimer.current)
    allowNextUnload.current = false
  }, [])
  return <Registration.Provider value={register}>
    {children}
    {active && pending && details && <LeaveDialog details={details} onStay={stay} onLeave={confirm} />}
  </Registration.Provider>
}

function LeaveDialog({ details, onStay, onLeave }: {
  details: GameLeaveDetails; onStay: () => void; onLeave: () => void
}) {
  const titleId = useId(), descriptionId = useId()
  const stayRef = useRef<HTMLButtonElement | null>(null)
  const panelRef = useSlotDialog(true, onStay, stayRef)
  return createPortal(<div className="game-leave-prompt__backdrop" role="presentation"
    onMouseDown={event => { if (event.target === event.currentTarget) onStay() }}>
    <section className="game-leave-prompt" ref={panelRef} tabIndex={-1} role="dialog" aria-modal="true"
      aria-labelledby={titleId} aria-describedby={descriptionId}>
      <span className="game-leave-prompt__mark" aria-hidden="true">♠</span>
      <h2 id={titleId}>{details.title}</h2>
      <p id={descriptionId}>{details.description}</p>
      <div className="game-leave-prompt__actions">
        <button ref={stayRef} type="button" onClick={onStay}>Stay in game</button>
        <button type="button" onClick={onLeave}>Leave page</button>
      </div>
    </section>
  </div>, document.body)
}
