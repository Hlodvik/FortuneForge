import { useLayoutEffect, useRef, type RefObject } from 'react'

export type SlotDialogTheme = 'midnight-vault'
const activeDialogs: symbol[] = []
const focusableSelector = 'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

export function useSlotDialog(
  isOpen: boolean,
  onClose: () => void,
  initialFocusRef?: RefObject<HTMLElement | null>,
) {
  const dialogRef = useRef<HTMLElement | null>(null)
  const onCloseRef = useRef(onClose)
  useLayoutEffect(() => { onCloseRef.current = onClose })

  useLayoutEffect(() => {
    if (!isOpen || !dialogRef.current) return
    const panel = dialogRef.current
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const id = Symbol('slot-dialog')
    activeDialogs.push(id)
    const isTopDialog = () => activeDialogs.at(-1) === id
    const focusable = () => Array.from(panel.querySelectorAll<HTMLElement>(focusableSelector)).filter(element => {
      if (element.closest('[hidden], [inert], [aria-hidden="true"]')) return false
      let candidate: HTMLElement | null = element
      while (candidate && candidate !== panel.parentElement) {
        const style = getComputedStyle(candidate)
        if (style.display === 'none' || style.visibility === 'hidden') return false
        candidate = candidate.parentElement
      }
      return true
    })
    const focusFirst = () => (initialFocusRef?.current ?? focusable()[0] ?? panel).focus({ preventScroll: true })
    const containFocus = (event: FocusEvent) => {
      if (isTopDialog() && event.target instanceof Node && !panel.contains(event.target)) focusFirst()
    }
    const handleKey = (event: KeyboardEvent) => {
      if (!isTopDialog()) return
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        onCloseRef.current()
      } else if (event.key === 'Tab') {
        const controls = focusable()
        const first = controls[0]
        const last = controls.at(-1)
        const current = document.activeElement
        if (!first || !last) {
          event.preventDefault()
          panel.focus({ preventScroll: true })
        } else if (event.shiftKey && (current === first || !controls.includes(current as HTMLElement))) {
          event.preventDefault()
          last.focus({ preventScroll: true })
        } else if (!event.shiftKey && (current === last || !controls.includes(current as HTMLElement))) {
          event.preventDefault()
          first.focus({ preventScroll: true })
        }
      }
    }
    focusFirst()
    document.addEventListener('keydown', handleKey, true)
    document.addEventListener('focusin', containFocus, true)
    return () => {
      const wasTop = isTopDialog()
      const index = activeDialogs.indexOf(id)
      if (index >= 0) activeDialogs.splice(index, 1)
      document.removeEventListener('keydown', handleKey, true)
      document.removeEventListener('focusin', containFocus, true)
      if (wasTop && previousFocus?.isConnected) previousFocus.focus({ preventScroll: true })
    }
  }, [isOpen, initialFocusRef])

  return dialogRef
}
