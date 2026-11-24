export * from './manifest'
export * from './catalog'
export * from './sounds'
export * from './cabinetTheme'
export * from './symbols'
export * from './visuals'
export * from './presentation'
export * from './ArcaneLightningCollection'

import { ArcaneArchivesView } from './ArcaneArchivesView'
import { useArcaneArchives } from './useArcaneArchives'
import type { ArcaneArchivesGameProps } from './types'

export function ArcaneArchivesGame(props: ArcaneArchivesGameProps) {
  return createElement(ArcaneArchivesView, { controller: useArcaneArchives(props), renderShell: props.renderShell })
}

export type { ArcaneArchivesGameProps } from './types'
export * from './arcaneFeatures'
import { createElement } from 'react'
