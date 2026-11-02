type NavigationAction = () => void
let handler: ((action: NavigationAction) => void) | null = null

/** Synchronous programmatic navigation uses the same presentation gate as links. */
export function requestGameNavigation(action: NavigationAction) {
  if (handler) handler(action)
  else action()
}

export function installGameNavigationHandler(next: (action: NavigationAction) => void) {
  handler = next
  return () => { if (handler === next) handler = null }
}
