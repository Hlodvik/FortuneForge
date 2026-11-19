// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import { Component, type ReactNode } from 'react'

export function RouteLoadingState() {
  return <main className="route-state" role="status">Opening Fortune Forge…</main>
}

export function RouteErrorState() {
  return <main className="route-state route-state--error" role="alert">
    <strong>This page could not be loaded.</strong>
    <span>Check your connection, then try loading the page again.</span>
    <button type="button" onClick={() => window.location.reload()}>Try again</button>
    <a href="/">Return to Fortune Forge</a>
  </main>
}

export class RouteErrorBoundary extends Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  render() { return this.state.failed ? this.props.fallback : this.props.children }
}
