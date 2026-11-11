import { useInsertionEffect, type ComponentType, type ReactElement } from 'react'
import { AuthenticatedRouteState } from '../../app/routes/AuthenticatedRouteState'
import { useGameRelease } from './useGameRelease'

type GameSelection<Props> =
  | { Game: ComponentType<Props>; pending: null }
  | { Game: null; pending: ReactElement }

export function useReleasedGame<Props>(
  gameId: string,
  title: string,
  BundledGame: ComponentType<Props>,
  bundledStyles: string,
): GameSelection<Props> {
  const { state, retry } = useGameRelease<Props>(gameId)
  const useBundledStyles = state.status === 'local'

  useInsertionEffect(() => {
    if (!useBundledStyles) return
    const stylesheet = document.createElement('style')
    stylesheet.textContent = bundledStyles
    document.head.append(stylesheet)
    return () => stylesheet.remove()
  }, [bundledStyles, useBundledStyles])

  if (state.status === 'loading' || state.status === 'error') {
    return {
      Game: null,
      pending: <AuthenticatedRouteState
        error={state.status === 'error' ? state.error : null}
        loadingLabel={`Opening ${title}…`}
        errorTitle={`${title} could not be opened.`}
        onRetry={retry}
      />,
    }
  }

  return { Game: state.status === 'ready' ? state.Game : BundledGame, pending: null }
}
