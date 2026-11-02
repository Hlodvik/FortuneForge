/** Order game tasks and their local workspace prerequisites, excluding the host. */
export function planGameWorkspaceTasks(workspaces, task) {
  const byName = new Map()
  for (const workspace of workspaces) {
    const name = workspace.packageJson.name
    if (byName.has(name)) throw new Error(`Duplicate workspace package: ${name}`)
    byName.set(name, workspace)
  }

  const ordered = []
  const visited = new Set()
  const visiting = new Set()
  function visit(workspace) {
    const { name, dependencies, devDependencies, optionalDependencies } = workspace.packageJson
    if (visited.has(name)) return
    if (visiting.has(name)) throw new Error(`Workspace dependency cycle: ${[...visiting, name].join(' -> ')}`)
    visiting.add(name)
    for (const dependency of Object.keys({ ...dependencies, ...devDependencies, ...optionalDependencies })) {
      const localDependency = byName.get(dependency)
      if (localDependency) visit(localDependency)
    }
    visiting.delete(name)
    visited.add(name)
    ordered.push(workspace)
  }

  for (const workspace of workspaces.filter(({ path }) => path.startsWith('games/'))) visit(workspace)

  return ordered.flatMap(({ path, packageJson }) => {
    const commands = []
    // Package exports resolve dist, so type checks and tests need shared
    // prerequisites compiled even on a fresh checkout with no build output.
    if (!path.startsWith('games/') && (task === 'check' || task === 'test') && packageJson.scripts?.build) {
      commands.push({ name: packageJson.name, script: 'build', skip: false })
    }
    commands.push({ name: packageJson.name, script: task, skip: !packageJson.scripts?.[task] })
    return commands
  })
}
