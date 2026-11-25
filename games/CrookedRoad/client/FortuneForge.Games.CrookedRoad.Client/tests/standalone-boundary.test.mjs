import test from 'node:test'
import assert from 'node:assert/strict'
import { existsSync, readFileSync, realpathSync } from 'node:fs'
import { dirname, extname, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

// This is an ownership gate, not a substitute for controller/API and paid-browser tests.
const packageRoot = realpathSync(resolve(dirname(fileURLToPath(import.meta.url)), '..'))
const gameRoot = resolve(packageRoot, '../..')
const repositoryRoot = resolve(gameRoot, '../..')
const sourceRoot = resolve(packageRoot, 'src')
const manifest = JSON.parse(readFileSync(resolve(packageRoot, 'package.json'), 'utf8'))
const release = JSON.parse(readFileSync(resolve(gameRoot, 'game-release.json'), 'utf8'))
const codeExtensions = ['.ts', '.tsx', '.js', '.jsx', '.mjs', '.mts']
const neutralPackages = new Map([
  ['@fortuneforge/slot-client-core', resolve(repositoryRoot, 'packages/slot-client-core')],
  ['@fortuneforge/client-presentation', resolve(repositoryRoot, 'packages/client-presentation')],
])
const owners = [packageRoot, ...neutralPackages.values()].map(root => realpathSync(root))
const label = path => relative(repositoryRoot, path).replaceAll('\\', '/')
const canonical = path => existsSync(path) ? realpathSync(path) : resolve(path)
const inside = (root, path) => {
  const child = relative(root, path)
  return child === '' || child !== '..' && !child.startsWith(`..${sep}`) && !child.includes(':')
}
const ownerOf = path => owners.find(root => inside(root, canonical(path)))
const sourceText = (path, overrides) => overrides?.get(canonical(path)) ?? readFileSync(path, 'utf8')
const parse = (path, text) => ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true,
  extname(path) === '.tsx' || extname(path) === '.jsx' ? ts.ScriptKind.TSX : ts.ScriptKind.TS)

function sourceFile(candidate) {
  const path = canonical(candidate)
  const extension = extname(path)
  const stem = codeExtensions.includes(extension) ? path.slice(0, -extension.length) : path
  const candidates = codeExtensions.includes(extension) || !extension
    ? [...codeExtensions.map(value => stem + value), ...codeExtensions.map(value => resolve(stem, 'index' + value)), path]
    : [path]
  const found = candidates.find(value => existsSync(value))
  assert.ok(found, `Unresolved runtime module ${label(candidate)}`)
  return canonical(found)
}

function exportedPath(packageDirectory, subpath) {
  const metadata = JSON.parse(readFileSync(resolve(packageDirectory, 'package.json'), 'utf8'))
  const key = subpath || '.'
  let target = metadata.exports?.[key]
  if (target === undefined) {
    const pattern = Object.keys(metadata.exports ?? {}).find(value => {
      const [prefix, suffix] = value.split('*')
      return value.includes('*') && key.startsWith(prefix) && key.endsWith(suffix)
    })
    assert.ok(pattern, `No public runtime export ${metadata.name}${key.slice(1)}`)
    const [prefix, suffix] = pattern.split('*')
    const wildcard = key.slice(prefix.length, suffix.length ? -suffix.length : undefined)
    const entry = metadata.exports[pattern]
    target = typeof entry === 'string' ? entry.replace('*', wildcard)
      : { ...entry, import: entry.import?.replace('*', wildcard) }
  }
  const entry = typeof target === 'string' ? target : target?.import ?? target?.default
  assert.equal(typeof entry, 'string', `Missing executable export ${metadata.name}${key.slice(1)}`)
  assert.ok(entry.startsWith('./dist/'), `Runtime export must resolve to built package output: ${entry}`)
  return sourceFile(resolve(packageDirectory, 'src', entry.slice('./dist/'.length)))
}

function reference(path, specifier, runtime) {
  const value = specifier.split(/[?#]/, 1)[0]
  if (value.startsWith('.')) {
    const resolved = sourceFile(resolve(dirname(path), value))
    assert.ok(ownerOf(resolved), `${label(path)} imports host or another game's source: ${specifier}`)
    const currentOwner = ownerOf(path)
    assert.ok(inside(currentOwner, resolved), `${label(path)} crosses its package source boundary: ${specifier}`)
    return resolved
  }
  if (/^react(?:\/|$)|^react-dom(?:\/|$)/.test(value)) return null
  if (value === '@fortuneforge/slot-game-runtime' || value.startsWith('@fortuneforge/slot-game-runtime/')) {
    assert.equal(runtime, false, `${label(path)} uses the generic game runtime/showcase builder: ${specifier}`)
    return null
  }
  const packageName = value.startsWith('@') ? value.split('/').slice(0, 2).join('/') : value.split('/')[0]
  const root = packageName === manifest.name ? packageRoot : neutralPackages.get(packageName)
  assert.ok(root, `${label(path)} imports an unapproved host/foreign runtime dependency: ${specifier}`)
  return exportedPath(root, value.length === packageName.length ? '.' : '.' + value.slice(packageName.length))
}

function walk(node, visit) {
  visit(node)
  ts.forEachChild(node, child => walk(child, visit))
}

function runtimeGraph(roots, overrides = new Map()) {
  const graph = new Map()
  function visit(path) {
    path = canonical(path)
    if (graph.has(path)) return
    assert.ok(ownerOf(path), `Runtime escaped owned or neutral packages: ${label(path)}`)
    const children = []
    graph.set(path, children)
    const extension = extname(path)
    if (extension === '.css') {
      for (const [, specifier] of sourceText(path, overrides).matchAll(/@import\s+(?:url\(\s*)?['"]([^'"]+)['"]/g)) {
        const dependency = reference(path, specifier, true)
        if (dependency) children.push(dependency)
      }
    } else if (codeExtensions.includes(extension)) {
      const text = sourceText(path, overrides)
      // Even erased type imports may not make game source depend on the host or another game.
      walk(parse(path, text), node => {
        if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier &&
            ts.isStringLiteralLike(node.moduleSpecifier)) reference(path, node.moduleSpecifier.text, false)
      })
      const emitted = ts.transpileModule(text, {
        fileName: path,
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022,
          jsx: ts.JsxEmit.ReactJSX, verbatimModuleSyntax: false },
      }).outputText
      walk(parse(path + '.js', emitted), node => {
        let specifier
        if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier) {
          assert.ok(ts.isStringLiteralLike(node.moduleSpecifier), `Non-static module in ${label(path)}`)
          specifier = node.moduleSpecifier.text
        } else if (ts.isCallExpression(node) && (node.expression.kind === ts.SyntaxKind.ImportKeyword ||
            ts.isIdentifier(node.expression) && node.expression.text === 'require')) {
          assert.ok(node.arguments.length === 1 && ts.isStringLiteralLike(node.arguments[0]),
            `Non-static dynamic import cannot establish ownership: ${label(path)}`)
          specifier = node.arguments[0].text
        }
        if (specifier !== undefined) {
          const dependency = reference(path, specifier, true)
          if (dependency) children.push(dependency)
        }
      })
    }
    for (const dependency of children) visit(dependency)
  }
  for (const root of roots) visit(root)
  return graph
}

function importsOf(path, overrides) {
  const imports = new Map()
  for (const node of parse(path, sourceText(path, overrides)).statements) {
    if (!ts.isImportDeclaration(node) || !node.importClause || node.importClause.isTypeOnly || !ts.isStringLiteralLike(node.moduleSpecifier)) continue
    const dependency = reference(path, node.moduleSpecifier.text, true)
    if (!dependency) continue
    const clause = node.importClause
    if (clause.name) imports.set(clause.name.text, { path: dependency, exported: 'default' })
    if (clause.namedBindings && ts.isNamedImports(clause.namedBindings)) {
      for (const item of clause.namedBindings.elements) if (!item.isTypeOnly)
        imports.set(item.name.text, { path: dependency, exported: item.propertyName?.text ?? item.name.text })
    }
  }
  return imports
}

function exportedFunction(path, name, overrides) {
  const ast = parse(path, sourceText(path, overrides))
  for (const node of ast.statements) {
    if (!node.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.ExportKeyword)) continue
    if (ts.isFunctionDeclaration(node) && node.name?.text === name && node.body) return node
    if (ts.isVariableStatement(node)) {
      const variable = node.declarationList.declarations.find(item => ts.isIdentifier(item.name) && item.name.text === name)
      if (variable?.initializer && (ts.isArrowFunction(variable.initializer) || ts.isFunctionExpression(variable.initializer)))
        return variable.initializer
    }
  }
  assert.fail(`${label(path)} must own executable exported function ${name}`)
}

function standaloneEntries() {
  assert.equal(release.id, 'crooked-road')
  assert.equal(canonical(resolve(gameRoot, release.frontend.directory)), packageRoot)
  assert.equal(release.frontend.kind ?? 'module', 'module')
  assert.equal(Object.hasOwn(release.frontend, 'manifestExport'), false,
    'Release must export its own game component, not a host renderSlot(manifest) adapter')
  const browser = sourceFile(resolve(packageRoot, release.frontend.entry))
  const entry = exportedPath(packageRoot, '.')
  const controller = sourceFile(resolve(sourceRoot, 'useCrookedRoad.ts'))
  const view = sourceFile(resolve(sourceRoot, 'CrookedRoadView.tsx'))
  const api = sourceFile(resolve(sourceRoot, 'api.ts'))
  return { browser, entry, controller, view, api }
}

function runtimeEntries(entries) {
  return [entries.browser, ...Object.keys(manifest.exports).map(key => {
    assert.ok(!key.includes('*'), `Enumerate public runtime exports so ownership can be verified: ${key}`)
    return exportedPath(packageRoot, key)
  })]
}

function assertExecutableOwnership(entries, graph, overrides) {
  for (const [role, path] of Object.entries(entries)) assert.ok(graph.has(path), `Executable graph never reaches owned ${role}: ${label(path)}`)
  const entryFunction = exportedFunction(entries.entry, 'CrookedRoadGame', overrides)
  const entryImports = importsOf(entries.entry, overrides)
  const controllerNames = [...entryImports].filter(([, value]) => value.path === entries.controller).map(([name]) => name)
  const viewNames = [...entryImports].filter(([, value]) => value.path === entries.view).map(([name]) => name)
  const controllerCall = node => ts.isCallExpression(node) && ts.isIdentifier(node.expression) && controllerNames.includes(node.expression.text)
  const controllerBindings = new Set()
  let callsController = false
  walk(entryFunction.body, node => {
    if (controllerCall(node)) callsController = true
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer && controllerCall(node.initializer))
      controllerBindings.add(node.name.text)
  })
  let viewUsesController = false
  walk(entryFunction.body, node => {
    if (!(ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) || !ts.isIdentifier(node.tagName) || !viewNames.includes(node.tagName.text)) return
    for (const attribute of node.attributes.properties) {
      if (!ts.isJsxAttribute(attribute) || attribute.name.getText() !== 'controller' || !attribute.initializer || !ts.isJsxExpression(attribute.initializer)) continue
      const value = attribute.initializer.expression
      if (value && (controllerCall(value) || ts.isIdentifier(value) && controllerBindings.has(value.text))) viewUsesController = true
    }
  })
  assert.ok(callsController && viewUsesController,
    'Public CrookedRoadGame must execute its own controller and pass that result to its own view')
  const viewFunction = exportedFunction(entries.view, 'CrookedRoadView', overrides)
  let ownsRenderedScreen = false
  walk(viewFunction.body, node => { if (ts.isJsxElement(node) || ts.isJsxSelfClosingElement(node) || ts.isJsxFragment(node)) ownsRenderedScreen = true })
  assert.ok(ownsRenderedScreen, 'Owned view must implement the rendered screen')
  const controllerFunction = exportedFunction(entries.controller, 'useCrookedRoad', overrides)
  const apiBindings = [...importsOf(entries.controller, overrides)].filter(([, value]) => value.path === entries.api).map(([name]) => name)
  let callsOwnedApi = false
  walk(controllerFunction.body, node => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && apiBindings.includes(node.expression.text)) callsOwnedApi = true
  })
  assert.ok(callsOwnedApi, 'Owned controller must execute the owned API factory, not merely re-export it')
  exportedFunction(entries.api, 'createCrookedRoadApi', overrides)
  const browserAst = parse(entries.browser, sourceText(entries.browser, overrides))
  const defaultEntry = browserAst.statements.some(node => ts.isExportDeclaration(node) && node.moduleSpecifier &&
    ts.isStringLiteralLike(node.moduleSpecifier) && reference(entries.browser, node.moduleSpecifier.text, true) === entries.entry &&
    node.exportClause && ts.isNamedExports(node.exportClause) && node.exportClause.elements.some(item => item.name.text === 'default' &&
      (item.propertyName?.text ?? item.name.text) === 'CrookedRoadGame'))
  assert.ok(defaultEntry, 'Release browser entry must default-export the executable package-owned game')
}

test('release and public package entry reach owned screen, controller and API through runtime imports', () => {
  for (const dependency of Object.keys({ ...manifest.dependencies, ...manifest.peerDependencies }))
    assert.ok(neutralPackages.has(dependency) || dependency === 'react' || dependency === 'react-dom',
      `Browser package declares a host, generic-game or other-game runtime dependency: ${dependency}`)
  const entries = standaloneEntries()
  const graph = runtimeGraph(runtimeEntries(entries))
  assertExecutableOwnership(entries, graph)
})

test('executable package exposes paid-only metadata and no demo request destination', () => {
  const inventory = JSON.parse(readFileSync(resolve(repositoryRoot, 'games/catalog/slot-packages.json'), 'utf8'))
  const owned = inventory.games.find(game => game.routeId === 'crooked-road')
  assert.ok(owned)
  assert.equal(owned.playPath, '/slots/crooked-road')
  assert.equal(owned.demoPath, null)
  const entries = standaloneEntries()
  const graph = runtimeGraph(runtimeEntries(entries))
  for (const path of graph.keys()) {
    if (!inside(packageRoot, path) || !codeExtensions.includes(extname(path))) continue
    walk(parse(path, sourceText(path)), node => {
      if (ts.isStringLiteralLike(node) && node.text.startsWith('/api/'))
        assert.ok(!node.text.startsWith('/api/demo/'), `Paid game requests a demo destination in ${label(path)}`)
    })
  }
})

for (const [name, poison, expected] of [
  ['transitive generic showcase', "import '@fortuneforge/slot-game-runtime/showcase'", /generic game runtime\/showcase/],
  ['transitive other game', "import '@fortuneforge/games-neon-nights'", /unapproved host\/foreign/],
  ['non-static dynamic module', 'export const hiddenDependency = value => import(value)', /Non-static dynamic import/],
]) test(`ownership gate rejects ${name} introduced below the entry`, () => {
  const entries = standaloneEntries()
  const overrides = new Map([[entries.api, sourceText(entries.api) + '\n' + poison + '\n']])
  assert.throws(() => runtimeGraph(runtimeEntries(entries), overrides), expected)
})

test('ownership gate rejects a transitive host route import', () => {
  const entries = standaloneEntries()
  const host = resolve(repositoryRoot, 'fortuneforge.client/src/app/routes/SlotGameRoute.tsx')
  const specifier = relative(dirname(entries.api), host).replaceAll('\\', '/')
  const overrides = new Map([[entries.api, sourceText(entries.api) + `\nimport ${JSON.stringify(specifier)}\n`]])
  assert.throws(() => runtimeGraph(runtimeEntries(entries), overrides), /host or another game's source|crosses its package/)
})

test('ownership gate rejects a manifest-only public entry even when metadata files exist', () => {
  const entries = standaloneEntries()
  const overrides = new Map([[entries.entry, "export * from './manifest'\n"]])
  const graph = runtimeGraph(runtimeEntries(entries), overrides)
  assert.throws(() => assertExecutableOwnership(entries, graph, overrides), /never reaches owned|must own executable/)
})
