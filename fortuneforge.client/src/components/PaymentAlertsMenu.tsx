import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { listPaymentInvoices, type PaymentCheckout } from '../features/payments/services/paymentsApi'
import './PaymentAlertsMenu.css'

export function PaymentAlertsMenu() {
  const [invoices, setInvoices] = useState<PaymentCheckout[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isOpen, setIsOpen] = useState(false)
  const [accountTheme, setAccountTheme] = useState(false)
  const detailsRef = useRef<HTMLDetailsElement>(null)
  const triggerRef = useRef<HTMLElement>(null)
  const panelRef = useRef<HTMLElement>(null)
  const panelId = useId()

  const close = useCallback((restoreFocus = false) => {
    if (detailsRef.current) detailsRef.current.open = false
    setIsOpen(false)
    if (restoreFocus) triggerRef.current?.focus()
  }, [])

  useLayoutEffect(() => {
    if (!isOpen) return
    const trigger = triggerRef.current
    const panel = panelRef.current
    if (!trigger || !panel) return

    const position = () => {
      const viewport = window.visualViewport
      const leftEdge = viewport?.offsetLeft ?? 0
      const topEdge = viewport?.offsetTop ?? 0
      const width = viewport?.width ?? window.innerWidth
      const height = viewport?.height ?? window.innerHeight
      const margin = 16
      const gap = 8
      const anchor = trigger.getBoundingClientRect()
      if (anchor.width === 0 || anchor.height === 0 || anchor.bottom < topEdge || anchor.top > topEdge + height ||
        anchor.right < leftEdge || anchor.left > leftEdge + width) {
        close()
        return
      }
      const rootFontSize = Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16
      const panelWidth = Math.max(0, Math.min(23 * rootFontSize, width - margin * 2))
      panel.style.width = `${panelWidth}px`
      panel.style.maxHeight = `${Math.max(0, height - margin * 2)}px`
      const desiredHeight = panel.getBoundingClientRect().height
      const below = Math.max(0, topEdge + height - margin - anchor.bottom - gap)
      const above = Math.max(0, anchor.top - gap - topEdge - margin)
      const openBelow = below >= desiredHeight || below >= above
      panel.style.maxHeight = `${openBelow ? below : above}px`
      const panelHeight = panel.getBoundingClientRect().height
      const top = openBelow ? anchor.bottom + gap : anchor.top - gap - panelHeight
      panel.style.left = `${Math.max(leftEdge + margin, Math.min(anchor.right - panelWidth, leftEdge + width - margin - panelWidth))}px`
      panel.style.top = `${Math.max(topEdge + margin, Math.min(top, topEdge + height - margin - panelHeight))}px`
      panel.style.visibility = 'visible'
    }
    const reposition = (event: Event) => {
      if (event.target instanceof Node && panel.contains(event.target)) return
      position()
    }
    const dismissOutside = (event: PointerEvent) => {
      if (event.target instanceof Node && !detailsRef.current?.contains(event.target) && !panel.contains(event.target)) close()
    }
    const dismissEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      event.stopPropagation()
      close(true)
    }
    position()
    window.addEventListener('resize', position)
    window.addEventListener('scroll', reposition, true)
    window.visualViewport?.addEventListener('resize', position)
    window.visualViewport?.addEventListener('scroll', position)
    document.addEventListener('pointerdown', dismissOutside)
    window.addEventListener('keydown', dismissEscape, true)
    return () => {
      window.removeEventListener('resize', position)
      window.removeEventListener('scroll', reposition, true)
      window.visualViewport?.removeEventListener('resize', position)
      window.visualViewport?.removeEventListener('scroll', position)
      document.removeEventListener('pointerdown', dismissOutside)
      window.removeEventListener('keydown', dismissEscape, true)
    }
  }, [isOpen, invoices, error, isLoading, close])

  useEffect(() => {
    let active = true

    async function refresh() {
      try {
        const nextInvoices = await listPaymentInvoices(8)
        if (active) {
          setInvoices(nextInvoices)
          setError(null)
        }
      } catch {
        if (active) {
          setError('Orders are temporarily unavailable.')
        }
      } finally {
        if (active) {
          setIsLoading(false)
        }
      }
    }

    void refresh()
    const handleInvoiceUpdate = () => void refresh()
    window.addEventListener('payment-invoices-updated', handleInvoiceUpdate)
    const interval = window.setInterval(() => void refresh(), 30_000)
    return () => {
      active = false
      window.removeEventListener('payment-invoices-updated', handleInvoiceUpdate)
      window.clearInterval(interval)
    }
  }, [])

  const activeCount = invoices.filter((invoice) =>
    invoice.status === 'received' || invoice.status === 'processing').length

  return (
    <details ref={detailsRef} className="payment-alerts" onToggle={event => {
      setIsOpen(event.currentTarget.open)
      setAccountTheme(event.currentTarget.closest('.home-lobby, .balance-page__alerts') !== null)
    }}>
      <summary ref={triggerRef} aria-controls={panelId} aria-expanded={isOpen}
        aria-label={`Payment alerts${activeCount > 0 ? `, ${activeCount} active` : ''}`}
        onKeyDown={event => {
          if (isOpen && event.key === 'Tab' && !event.shiftKey) {
            event.preventDefault()
            panelRef.current?.querySelector<HTMLAnchorElement>('a[href]')?.focus()
          }
        }}>
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" />
        </svg>
        {activeCount > 0 && <span>{activeCount > 9 ? '9+' : activeCount}</span>}
      </summary>
      {isOpen && createPortal(<section ref={panelRef} id={panelId}
        className={`payment-alerts__panel${accountTheme ? ' payment-alerts__panel--account' : ''}`}
        aria-label="Recent payment orders" onKeyDown={event => {
          if (event.key !== 'Tab' || !event.shiftKey) return
          if (event.target === panelRef.current?.querySelector('a[href]')) {
            event.preventDefault()
            close(true)
          }
        }}>
        <header><div><small>Payment alerts</small><strong>Your invoices</strong></div><a href="/home/invoices">View all</a></header>
        {isLoading && <p className="payment-alerts__message">Loading orders…</p>}
        {!isLoading && error !== null && <p className="payment-alerts__message">{error}</p>}
        {!isLoading && error === null && invoices.length === 0 && (
          <p className="payment-alerts__message">No payment orders yet.</p>
        )}
        {!isLoading && error === null && invoices.length > 0 && (
          <ul>
            {invoices.map((invoice) => (
              <li key={invoice.checkoutId}>
                <a href={`/home/invoices/${encodeURIComponent(invoice.invoiceId)}`}>
                  <span className={`payment-alerts__status payment-alerts__status--${invoice.status}`} aria-hidden="true" />
                  <span><strong>{formatMoney(invoice)}</strong><small>{formatStatus(invoice.status)} · {new Date(invoice.createdAtUtc).toLocaleDateString()}</small></span>
                  <span aria-hidden="true">›</span>
                </a>
              </li>
            ))}
          </ul>
        )}
      </section>, document.body)}
    </details>
  )
}

function formatMoney(invoice: PaymentCheckout) {
  return new Intl.NumberFormat(invoice.locale, {
    style: 'currency',
    currency: invoice.currency,
  }).format(invoice.amountMinor / 100)
}

function formatStatus(status: PaymentCheckout['status']) {
  return status.charAt(0).toUpperCase() + status.slice(1)
}
