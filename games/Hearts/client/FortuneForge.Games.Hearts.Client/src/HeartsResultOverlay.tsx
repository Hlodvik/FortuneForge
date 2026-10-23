import { useEffect, useRef, type ReactNode } from 'react'

const focusableSelector = 'button,input:not([type="hidden"]),select,textarea,a[href],summary,[tabindex],[contenteditable="true"]'

function available(element: HTMLElement): boolean {
  if (element.matches(':disabled,[aria-disabled="true"]') || element.closest('[hidden],[inert],[aria-hidden="true"]')) return false
  for (let ancestor: HTMLElement | null = element; ancestor; ancestor = ancestor.parentElement) {
    const style = getComputedStyle(ancestor)
    if (style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse') return false
  }
  return true
}

function controls(dialog: HTMLElement): HTMLElement[] {
  return [...dialog.querySelectorAll<HTMLElement>(focusableSelector)].filter(element => element.tabIndex >= 0 && available(element))
}

/** A result needs an explicit return/confirmation action; Escape must not discard it. */
export function HeartsResultOverlay({ children, className, label }: { children: ReactNode; className?: string; label: string }) {
  const root = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const mounted = root.current
    if (!mounted) return
    const dialog: HTMLDivElement = mounted
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const page = dialog.closest('.ff-hearts-page')
    // Help/history can temporarily appear above the result without two focus traps fighting.
    const active = () => [...document.querySelectorAll<HTMLElement>('[role="dialog"][aria-modal="true"]')].filter(available).at(-1) === dialog
    const focusResult = () => dialog.focus({ preventScroll: true })
    function key(event: KeyboardEvent) {
      if (!active()) return
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); return }
      if (event.key !== 'Tab') return
      const actions = controls(dialog), first = actions[0], last = actions.at(-1)
      if (!first) { event.preventDefault(); focusResult(); return }
      if (!dialog.contains(document.activeElement) || document.activeElement === dialog) {
        event.preventDefault(); (event.shiftKey ? last : first)?.focus(); return
      }
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    function containFocus(event: FocusEvent) {
      if (active() && event.target instanceof Node && !dialog.contains(event.target)) focusResult()
    }
    // A pending confirmation may remove/disable its focused action, then add a retry later.
    const observer = new MutationObserver(() => {
      if (active() && (!dialog.contains(document.activeElement) || (document.activeElement instanceof HTMLElement && !available(document.activeElement)))) focusResult()
    })
    observer.observe(dialog, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled', 'hidden', 'inert', 'aria-hidden', 'aria-disabled', 'style', 'class'] })
    document.addEventListener('keydown', key)
    document.addEventListener('focusin', containFocus)
    if (active()) focusResult()
    return () => {
      observer.disconnect()
      document.removeEventListener('keydown', key)
      document.removeEventListener('focusin', containFocus)
      if (previous?.isConnected && previous !== document.body && available(previous)) previous.focus({ preventScroll: true })
      else if (page?.isConnected) [...page.querySelectorAll<HTMLElement>('.ff-hearts-lobby button,.ff-hearts-onboarding button')].find(element => element.tabIndex >= 0 && available(element))?.focus({ preventScroll: true })
    }
  }, [])
  return <div className={`ff-hearts-result${className ? ` ${className}` : ''}`}><div ref={root} role="dialog" aria-modal="true" aria-label={label} tabIndex={-1}>{children}</div></div>
}
