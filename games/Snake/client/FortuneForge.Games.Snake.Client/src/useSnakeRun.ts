import { useEffect, useMemo, useSyncExternalStore } from 'react'
import type { SnakeGateway } from './contracts'
import { SnakeRunController } from './snakeRunController'

export function useSnakeRun(gateway: SnakeGateway) {
  const controller = useMemo(() => new SnakeRunController(gateway), [gateway])
  const run = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  useEffect(() => {
    controller.activate()
    const pause = () => controller.pause()
    const visibility = () => { if (document.hidden) controller.pause() }
    window.addEventListener('blur', pause); document.addEventListener('visibilitychange', visibility)
    return () => { window.removeEventListener('blur', pause); document.removeEventListener('visibilitychange', visibility); controller.dispose() }
  }, [controller])
  return { run, controller }
}
