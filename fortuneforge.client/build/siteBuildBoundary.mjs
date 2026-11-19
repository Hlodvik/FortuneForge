// Edited: 2026-11-18T22:49:50-06:00 | Computer: Vinnabok

// Catalogue art/route metadata and account history are owned by the host. Actual
// game clients, manifests, game routes and their runtime must never enter this build.
const hostGameModules = /^(?:slots\/(?:routeMetadata\.ts|catalogTypes\.ts|catalog\.ts|(?:goldFoundry|dragonHoard|jadeDragon|neonNights)\/catalog\.ts)|cards\/shared\/(?:CardRoomHistory\.tsx|cardRoomHistoryApi\.ts|cardRoomHistoryTypes\.ts|cardRoomHeader\.css))$/

export function assertSiteModuleBoundary(moduleIds) {
  const violations = moduleIds.filter((id) => {
    const path = id.replaceAll('\\', '/').split('?')[0]
    if (/^(?:games|packages\/slot-game-runtime)\//.test(path) || /node_modules\/@fortuneforge\/games-/.test(path)) return true
    if (path.startsWith('fortuneforge.client/src/app/routes/')) {
      return !/\/(?:AdminOperationsRoute|AdminPortalRoute|AuthenticatedRouteState)\.tsx$/.test(path)
    }
    const prefix = 'fortuneforge.client/src/games/'
    return path.startsWith(prefix) && !hostGameModules.test(path.slice(prefix.length))
  })
  if (violations.length) throw new Error(`Site build includes playable game modules:\n${violations.join('\n')}`)
}

export function siteBuildBoundary(repositoryRoot) {
  const root = repositoryRoot.replaceAll('\\', '/').replace(/\/$/, '') + '/'
  return {
    name: 'site-build-boundary',
    generateBundle() {
      const modules = [...this.getModuleIds()].map(id => id.replaceAll('\\', '/').replace(root, '')).sort()
      assertSiteModuleBoundary(modules)
      this.emitFile({ type: 'asset', name: 'site-graph.json', source: JSON.stringify({ schemaVersion: 1, modules }, null, 2) + '\n' })
    },
  }
}
