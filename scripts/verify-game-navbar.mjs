import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

// Regression policy, not a runtime security sandbox. We inspect first-party static
// imports/exports, literal lazy imports, directly imported vendor CSS, JSX provider
// calls and common local DOM aliases/helpers. External library JavaScript remains
// trusted dependency code. The host game release loader has three explicit sinks.
// The registered module entries and their CSS are inspected before publication.
// Other computed imports and runtime stylesheet injection fail closed.
const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const hostRequire = createRequire(path.join(repositoryRoot, 'fortuneforge.client/package.json'))
const ts = hostRequire('typescript')
const viteRequire = createRequire(hostRequire.resolve('vite/package.json'))
const postcss = viteRequire('postcss')
const normalize = value => value.replaceAll('\\', '/')
const reserved = /(?:site-navbar|site-header-balance|in-game-navbar|player-shell-(?:header|menu)|data-(?:game|site)-navbar)/i
const reservedVariable = /--[\w-]*(?:navbar|site-nav|site-header|in-game-nav|player-shell-header|player-shell-menu)[\w-]*/i
const sourceExtension = /\.(?:[cm]?js|jsx|tsx?)$/i
const sharedProviders = new Set(['SiteNavbar', 'InGameNavbar', 'InGameShell', 'HeaderBalance', 'GameReportControl', 'PlayerAccountMenus'])
const sharedCss = file => /^(?:fortuneforge\.client\/src\/index\.css|fortuneforge\.client\/src\/app\/styles\/[^/]+\.css|fortuneforge\.client\/src\/components\/(?:SiteNavbar|HeaderBalance|GameReportControl)\.css)$/.test(normalize(file))
const gamePresentation = file => /^(?:games\/|fortuneforge\.client\/src\/(?:features\/slots\/|games\/|app\/routes\/|pages\/(?:cards|games)\/))/.test(normalize(file))
const providerSource = file => /^fortuneforge\.client\/src\/components\/[^/]+\.[jt]sx?$/.test(normalize(file)) && sharedProviders.has(path.basename(file).replace(/\.[^.]+$/, ''))
const keyframes = node => { for (let item = node.parent; item; item = item.parent) if (item.type === 'atrule' && /keyframes$/i.test(item.name)) return true; return false }
const releaseFeature = 'fortuneforge.client/src/features/game-releases/'

function hostReleaseSink(file, node, source, owner) {
  if (normalize(file) !== releaseFeature + source) return false
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (ts.isFunctionDeclaration(parent)) return parent.name?.text === owner
  }
  return false
}

// Split only at the current selector level; strings, escapes and functions stay intact.
function splitSelectors(value) {
  const parts = []; let start = 0, depth = 0, bracket = 0, quote = ''
  for (let index = 0; index < value.length; index++) {
    const char = value[index]
    if (char === '\\') { index++; continue }
    if (quote) { if (char === quote) quote = ''; continue }
    if (char === '"' || char === "'") { quote = char; continue }
    if (char === '[') bracket++
    if (char === ']') bracket--
    if (bracket) continue
    if (char === '(') depth++
    if (char === ')') depth--
    if (char === ',' && depth === 0) { parts.push(value.slice(start, index).trim()); start = index + 1 }
  }
  parts.push(value.slice(start).trim())
  return parts
}

function withoutRelationalConstraints(selector) {
  let result = '', cursor = 0
  const pattern = /:(?:has|not)\(/g
  for (let match; (match = pattern.exec(selector));) {
    result += selector.slice(cursor, match.index)
    let end = pattern.lastIndex, depth = 1, quote = ''
    for (; end < selector.length && depth; end++) {
      const char = selector[end]
      if (char === '\\') { end++; continue }
      if (quote) { if (char === quote) quote = ''; continue }
      if (char === '"' || char === "'") { quote = char; continue }
      if (char === '(') depth++
      if (char === ')') depth--
    }
    cursor = end; pattern.lastIndex = end
  }
  return result + selector.slice(cursor)
}

// Positive class/ID/data anchors bound a selector. Negative/relational pseudo arguments
// cannot supply that boundary. Every :is/:where alternative must supply its own anchor.
export function isScopedSelector(selector) {
  let positive = false, plain = ''
  for (let index = 0; index < selector.length;) {
    const rest = selector.slice(index)
    const attribute = rest.match(/^\[((?:[^\]"']|"(?:\\.|[^"])*"|'(?:\\.|[^'])*')+)\]/)
    if (attribute) {
      if (/^\s*data-[\w-]+(?:\s|[~|^$*]?=|$)/i.test(attribute[1])) { positive = true; plain += '.__scope__' }
      index += attribute[0].length; continue
    }
    const anchor = rest.match(/^[.#]([a-z_-][\w-]*)/i)
    if (anchor) { if (anchor[1] !== 'root') positive = true; plain += anchor[0]; index += anchor[0].length; continue }
    const pseudo = rest.match(/^:{1,2}([\w-]+)\(/)
    if (pseudo) {
      const open = index + pseudo[0].length - 1
      let end = open + 1, depth = 1, quote = ''
      for (; end < selector.length && depth; end++) {
        const char = selector[end]
        if (char === '\\') { end++; continue }
        if (quote) { if (char === quote) quote = ''; continue }
        if (char === '"' || char === "'") { quote = char; continue }
        if (char === '(') depth++
        if (char === ')') depth--
      }
      if (depth) return false
      if (/^(?:is|where)$/i.test(pseudo[1]) && splitSelectors(selector.slice(open + 1, end - 1)).every(isScopedSelector)) { positive = true; plain += '.__scope__' }
      index = end; continue
    }
    plain += selector[index++]
  }
  if (/(^|[\s>+~,(])(?:html|body|:root|#root)(?=$|[\s:>+~.#),(])/i.test(plain)) return false
  // Siblings remain local when their subject has a positive anchor or a common
  // positively anchored ancestor. `.game + button` has neither and escapes.
  plain = plain.replace(/\s*([>+~])\s*/g, '$1')
  const sibling = Math.max(plain.lastIndexOf('+'), plain.lastIndexOf('~'))
  if (sibling >= 0 && !/[.#][\w-]+/.test(plain.slice(sibling + 1)) &&
    !/[.#][\w-]+[^+~]*[\s>]/.test(plain.slice(0, sibling))) return false
  return positive
}

export function inspectCss(text, file, { shared = sharedCss(file), game = gamePresentation(file) } = {}) {
  const findings = []
  const add = (node, code, message) => findings.push({ file, line: node.source?.start?.line ?? 1, code, message })
  let ast
  try { ast = postcss.parse(text, { from: file }) } catch (error) { return [{ file, line: error.line ?? 1, code: 'css-parse', message: error.reason ?? error.message }] }
  ast.walkRules(rule => {
    if (keyframes(rule)) return
    const declarations = rule.nodes.filter(node => node.type === 'decl')
    for (const selector of splitSelectors(rule.selector)) {
      if (game && (reserved.test(selector) || /\.in-game-shell(?![\w-])/.test(withoutRelationalConstraints(selector)))) add(rule, 'game-navbar-selector', `Game CSS must not select shared navigation or its outer shell: ${selector}`)
      const tokensOnly = selector.trim() === ':root' && declarations.length > 0 && declarations.every(node => node.prop.startsWith('--') && !reservedVariable.test(node.prop + ' ' + node.value))
      if (!shared && !tokensOnly && !isScopedSelector(selector)) add(rule, 'global-game-style', `Runtime presentation CSS must stay within a local surface: ${selector}`)
    }
    if (game) for (const declaration of declarations)
      if (reservedVariable.test(declaration.prop + ' ' + declaration.value)) add(declaration, 'game-navbar-variable', 'Game CSS cannot define or consume reserved navbar variables.')
  })
  return findings
}

export function assertReleasedGameStyles(text, file) {
  const findings = inspectCss(text, file, { game: true, shared: false })
  if (findings.length) throw new Error(findings.map(({ line, code, message }) => file + ':' + line + ' [' + code + '] ' + message).join('\n'))
  postcss.parse(text, { from: file }).walkAtRules('import', () => {
    throw new Error('Published module styles must bundle their CSS imports: ' + file)
  })
}

const unwrap = node => {
  while (node && (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node) || ts.isTypeAssertionExpression(node))) node = node.expression
  return node
}
function stringValue(node, constants = new Map()) {
  node = unwrap(node)
  if (!node) return undefined
  if (ts.isStringLiteralLike(node)) return node.text
  if (ts.isJsxExpression(node)) return stringValue(node.expression, constants)
  if (ts.isIdentifier(node)) return constants.get(node.text)
  if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.PlusToken) {
    const left = stringValue(node.left, constants), right = stringValue(node.right, constants)
    if (left !== undefined && right !== undefined) return left + right
  }
}
const walk = (node, visit) => { visit(node); ts.forEachChild(node, child => walk(child, visit)) }
const safeState = new Set(['account', 'authenticated', 'title', 'reportTitle', 'balance', 'onNavigate', 'key'])
const mutatingCalls = new Set(['setAttribute', 'setAttributeNS', 'removeAttribute', 'toggleAttribute', 'append', 'prepend', 'appendChild', 'insertBefore', 'replaceChild', 'replaceChildren', 'removeChild', 'remove', 'replaceWith', 'insertAdjacentHTML', 'insertAdjacentElement', 'setProperty', 'removeProperty', 'add', 'toggle', 'replace', 'animate', 'attachShadow'])

export function inspectSource(text, file) {
  const ast = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, /x$/.test(file) ? ts.ScriptKind.TSX : ts.ScriptKind.TS)
  const findings = [], constants = new Map(), components = new Map([['SiteNavbar', 'SiteNavbar'], ['InGameNavbar', 'InGameNavbar'], ['InGameShell', 'InGameShell']])
  const add = (node, code, message) => findings.push({ file, line: ast.getLineAndCharacterOfPosition(node.getStart(ast)).line + 1, code, message })
  const inlineStyles = new Set(), styleHooks = new Map()
  walk(ast, node => {
    if (ts.isImportDeclaration(node) && ts.isStringLiteralLike(node.moduleSpecifier)) {
      const specifier = node.moduleSpecifier.text
      if (/\.css\?inline$/.test(specifier) && node.importClause?.name) inlineStyles.add(node.importClause.name.text)
      const bindings = node.importClause?.namedBindings
      if (specifier.endsWith('/features/game-releases/useReleasedGame') && bindings && ts.isNamedImports(bindings)) {
        for (const binding of bindings.elements) {
          const name = binding.propertyName?.text ?? binding.name.text
          if (name === 'useBundledGameStyles') styleHooks.set(binding.name.text, 0)
          if (name === 'useReleasedGame') styleHooks.set(binding.name.text, 3)
        }
      }
    }
    if (ts.isImportDeclaration(node) && node.importClause?.namedBindings) {
      const bindings = node.importClause.namedBindings
      if (ts.isNamedImports(bindings)) for (const binding of bindings.elements) {
        const original = binding.propertyName?.text ?? binding.name.text
        if (components.has(original)) components.set(binding.name.text, original)
      }
      else if (/(?:SiteNavbar|InGameNavbar|InGameShell)$/.test(node.moduleSpecifier.text))
        for (const name of ['SiteNavbar', 'InGameNavbar', 'InGameShell']) components.set(bindings.name.text + '.' + name, name)
    }
  })
  function staticStyleArgument(node) {
    node = unwrap(node)
    if (!node) return false
    if (ts.isConditionalExpression(node)) return staticStyleArgument(node.whenTrue) && staticStyleArgument(node.whenFalse)
    return ts.isIdentifier(node) && (node.text === 'undefined' || inlineStyles.has(node.text))
  }
  for (let pass = 0; pass < 4; pass++) walk(ast, node => {
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name)) {
      const value = stringValue(node.initializer, constants)
      if (value !== undefined) constants.set(node.name.text, value)
      const source = unwrap(node.initializer)?.getText(ast)
      if (components.has(source)) components.set(node.name.text, components.get(source))
    }
  })
  walk(ast, node => {
    if (!ts.isJsxOpeningElement(node) && !ts.isJsxSelfClosingElement(node)) return
    const component = components.get(node.tagName.getText(ast))
    const attributes = node.attributes.properties
    const mode = attributes.find(item => ts.isJsxAttribute(item) && item.name.getText(ast) === 'mode')
    const modeValue = stringValue(mode?.initializer, constants)
    if (component && (component !== 'SiteNavbar' || modeValue === 'game' || modeValue === undefined)) {
      const allowed = new Set(safeState)
      if (component === 'SiteNavbar') allowed.add('mode')
      if (component === 'InGameShell') { allowed.add('className'); allowed.add('bodyClassName'); allowed.add('children') }
      for (const attribute of attributes) {
        if (ts.isJsxSpreadAttribute(attribute)) add(attribute, 'navbar-prop-spread', 'A shared game navbar/shell requires explicit state props; spreads can inject presentation.')
        else if (!allowed.has(attribute.name.getText(ast))) add(attribute, 'navbar-presentation-prop', `${component} cannot accept ${attribute.name.getText(ast)} for game navigation.`)
      }
      if (component !== 'InGameShell' && ts.isJsxOpeningElement(node) && node.parent.children.some(child => !ts.isJsxText(child) || child.text.trim()))
        add(node, 'navbar-children', 'Game navigation cannot receive custom children or controls.')
    }
    if (!providerSource(file)) for (const attribute of attributes) {
      if (!ts.isJsxAttribute(attribute)) continue
      const name = attribute.name.getText(ast)
      if (name === 'dangerouslySetInnerHTML' && gamePresentation(file)) add(attribute, 'runtime-style-injection', 'Game HTML injection cannot establish the shared navigation boundary.')
      if (/^data-(?:site|game)-navbar$/.test(name)) add(attribute, 'navbar-markup-owner', 'Only the shared provider may create canonical navbar markup.')
      if (name === 'className' || name === 'id') {
        const value = stringValue(attribute.initializer, constants)
        if (value && reserved.test(value)) add(attribute, 'navbar-markup-owner', 'Game/page markup cannot reproduce reserved navbar classes or IDs.')
        else walk(attribute, child => {
        if (ts.isStringLiteralLike(child) && reserved.test(child.text)) add(child, 'navbar-markup-owner', 'Game/page markup cannot reproduce reserved navbar classes or IDs.')
        })
      }
    }
    if (node.tagName.getText(ast) === 'style' && !providerSource(file)) add(node, 'runtime-style-injection', 'Runtime CSS belongs in the statically inspected import graph.')
  })

  const tainted = new Map()
  function reference(node) {
    node = unwrap(node)
    if (!node) return undefined
    if (ts.isIdentifier(node)) return tainted.get(node.text)
    if (ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) {
      if (node.getText(ast) === 'document.body') return 'body'
      if (node.getText(ast) === 'document.documentElement') return 'document'
      return reference(node.expression)
    }
    if (ts.isCallExpression(node)) {
      const method = ts.isPropertyAccessExpression(node.expression) ? node.expression.name.text : ''
      if (/^(?:querySelector|querySelectorAll|closest|getElementById|getElementsByClassName|getElementsByTagName)$/.test(method)) {
        const selector = stringValue(node.arguments[0], constants)
        if (selector && reserved.test(selector)) return 'navbar'
        if (reference(node.expression.expression) === 'navbar') return 'navbar'
        if (selector && !isScopedSelector(selector) && /(?:\bheader\b|\bnav\b|^\*$)/i.test(selector)) return 'navbar'
        if (/^(?:body|html|:root|#root)$/.test(selector ?? '')) return 'document'
      }
      if (node.expression.getText(ast) === 'Array.from') return reference(node.arguments[0])
      return undefined // Geometry and computed values are ordinary data, not DOM aliases.
    }
    if (ts.isBinaryExpression(node)) return reference(node.left) ?? reference(node.right)
    if (ts.isConditionalExpression(node)) return reference(node.whenTrue) ?? reference(node.whenFalse)
  }
  const functions = new Map()
  walk(ast, node => {
    if (ts.isFunctionDeclaration(node) && node.name) functions.set(node.name.text, node)
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer &&
      (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer))) functions.set(node.name.text, node.initializer)
  })
  for (let pass = 0; pass < 6; pass++) walk(ast, node => {
    if (ts.isVariableDeclaration(node)) {
      const value = reference(node.initializer)
      if (value) walk(node.name, binding => { if (ts.isIdentifier(binding)) tainted.set(binding.text, value) })
    }
    if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.EqualsToken && ts.isIdentifier(node.left)) {
      const value = reference(node.right); if (value) tainted.set(node.left.text, value)
    }
    if (ts.isForOfStatement(node) && reference(node.expression) && ts.isVariableDeclarationList(node.initializer))
      for (const declaration of node.initializer.declarations) if (ts.isIdentifier(declaration.name)) tainted.set(declaration.name.text, reference(node.expression))
    if (ts.isCallExpression(node)) {
      const fn = functions.get(node.expression.getText(ast))
      if (fn) node.arguments.forEach((argument, index) => {
        const parameter = fn.parameters[index], value = reference(argument)
        if (value && parameter && ts.isIdentifier(parameter.name)) tainted.set(parameter.name.text, value)
      })
      if (ts.isPropertyAccessExpression(node.expression) && /^(?:forEach|map)$/.test(node.expression.name.text) && reference(node.expression.expression))
        for (const callback of node.arguments) if (ts.isArrowFunction(callback) || ts.isFunctionExpression(callback))
          if (callback.parameters[0] && ts.isIdentifier(callback.parameters[0].name)) tainted.set(callback.parameters[0].name.text, reference(node.expression.expression))
    }
  })
  const sharedApp = normalize(file) === 'fortuneforge.client/src/app/App.tsx'
  function forbiddenMutation(target) {
    const kind = reference(target)
    if (!kind) return false
    if (kind === 'body' && /\.style\.overflow$/.test(target.getText(ast))) return false
    if (sharedApp && kind !== 'navbar') return false
    return true
  }
  walk(ast, node => {
    if (ts.isBinaryExpression(node) && node.operatorToken.kind >= ts.SyntaxKind.FirstAssignment && node.operatorToken.kind <= ts.SyntaxKind.LastAssignment && forbiddenMutation(node.left))
      add(node, 'navbar-dom-mutation', 'Game/page code cannot mutate shared navigation or global presentation.')
    if (ts.isDeleteExpression(node) && forbiddenMutation(node.expression)) add(node, 'navbar-dom-mutation', 'Shared navigation properties cannot be deleted.')
    if ((ts.isPrefixUnaryExpression(node) || ts.isPostfixUnaryExpression(node)) &&
      (node.operator === ts.SyntaxKind.PlusPlusToken || node.operator === ts.SyntaxKind.MinusMinusToken) && forbiddenMutation(node.operand))
      add(node, 'navbar-dom-mutation', 'Shared navigation properties cannot be incremented or decremented.')
    if (!providerSource(file) && ts.isNewExpression(node) && node.expression.getText(ast) === 'CSSStyleSheet')
      add(node, 'runtime-style-injection', 'Constructed runtime stylesheets cannot establish a static navigation boundary.')
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && styleHooks.has(node.expression.text) &&
      !staticStyleArgument(node.arguments[styleHooks.get(node.expression.text)]))
      add(node, 'runtime-style-injection', 'Bundled game styles require a statically inspected CSS import with ?inline.')
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)) {
      const element = node.expression.name.text === 'createElement' ? stringValue(node.arguments[0], constants) : undefined
      const releaseStylesheet = node.expression.getText(ast) === 'document.createElement' &&
        ((element === 'link' && hostReleaseSink(file, node, 'useGameRelease.ts', 'loadStylesheet')) ||
          (element === 'style' && hostReleaseSink(file, node, 'useReleasedGame.tsx', 'useBundledGameStyles')))
      if (!providerSource(file) && !releaseStylesheet && (/^(?:style|link)$/.test(element ?? '') || /^(?:insertRule|replaceSync)$/.test(node.expression.name.text)))
        add(node, 'runtime-style-injection', 'Runtime stylesheets must use statically inspected CSS imports.')
      if (mutatingCalls.has(node.expression.name.text) && forbiddenMutation(node.expression.expression))
        add(node, 'navbar-dom-mutation', 'Game/page code cannot mutate shared navigation or global presentation.')
      if (/^(?:Object\.assign|Object\.defineProperty|Reflect\.set)$/.test(node.expression.getText(ast)) && forbiddenMutation(node.arguments[0]))
        add(node, 'navbar-dom-mutation', 'Shared navigation cannot be changed through a mutation helper.')
    }
  })
  return findings
}

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return []
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry =>
    entry.isDirectory() ? /^(?:node_modules|dist|bin|obj|assets|test-results|playwright-report)$/.test(entry.name) ? [] : filesBelow(path.join(directory, entry.name)) : [path.join(directory, entry.name)])
}
const ignoredSource = file => /(?:\.d\.ts$|(?:^|\/)(?:__tests__|__mocks__)(?:\/)|\.(?:test|spec)\.[^.]+$)/.test(normalize(file))

export function inspectRepository(root = repositoryRoot, { releaseId } = {}) {
  const findings = [], sourceFiles = new Set(), cssFiles = new Set(), graph = [], queue = []
  const read = file => fs.readFileSync(file, 'utf8')
  const relative = file => normalize(path.relative(root, file))
  const rootPackage = JSON.parse(read(path.join(root, 'package.json')))
  const packages = new Map()
  for (const workspace of rootPackage.workspaces ?? []) {
    const folder = path.join(root, workspace), manifest = path.join(folder, 'package.json')
    if (fs.existsSync(manifest)) { const packageJson = JSON.parse(read(manifest)); packages.set(packageJson.name, { folder, workspace, packageJson }) }
  }
  const resolveLocal = filename => {
    const candidates = [filename]
    if (/\.[cm]?js$/.test(filename)) candidates.push(filename.replace(/\.[cm]?js$/, '.ts'), filename.replace(/\.[cm]?js$/, '.tsx'))
    if (!path.extname(filename)) candidates.push(...['.ts', '.tsx', '.js', '.jsx', '.mjs', '.css'].map(extension => filename + extension), ...['index.ts', 'index.tsx', 'index.js', 'index.css'].map(index => path.join(filename, index)))
    return candidates.find(candidate => fs.existsSync(candidate) && fs.statSync(candidate).isFile())
  }
  function dependency(from, specifier) {
    specifier = specifier.replace(/[?#].*$/, '')
    if (/^(?:https?:|data:|node:)/.test(specifier)) return
    let target
    if (specifier.startsWith('.')) target = resolveLocal(path.resolve(path.dirname(from), specifier))
    else if (specifier.startsWith('/')) target = resolveLocal(path.join(root, 'fortuneforge.client', specifier.slice(1))) ?? resolveLocal(path.join(root, 'fortuneforge.client/public', specifier.slice(1)))
    else {
      const name = specifier.startsWith('@') ? specifier.split('/').slice(0, 2).join('/') : specifier.split('/')[0]
      const workspace = packages.get(name)
      if (!workspace) {
        try { const vendor = createRequire(from).resolve(specifier); if (vendor.endsWith('.css')) target = vendor; else return }
        catch { return } // External JavaScript is dependency-managed, not recursively source audited.
      } else {
        const suffix = specifier.slice(name.length)
        const exportEntry = workspace.packageJson.exports?.[suffix ? '.' + suffix : '.']
        const exported = typeof exportEntry === 'string' ? exportEntry : exportEntry?.import ?? exportEntry?.default
        if (exported?.startsWith('./dist/')) target = resolveLocal(path.join(workspace.folder, 'src', exported.slice('./dist/'.length)))
        if (!target && (!suffix || suffix === '/styles.css')) target = resolveLocal(path.join(workspace.folder, 'src/index'))
        if (!target) target = resolveLocal(path.join(workspace.folder, 'src', suffix.slice(1)))
      }
    }
    if (!target) {
      findings.push({ file: relative(from), line: 1, code: 'unresolved-runtime-import', message: `Cannot establish style boundary for ${specifier}` }); return
    }
    if (!sourceExtension.test(target) && !target.endsWith('.css')) return
    if (!normalize(target).startsWith(normalize(path.resolve(root)) + '/')) {
      findings.push({ file: relative(from), line: 1, code: 'import-outside-repository', message: `Runtime import leaves the repository: ${specifier}` }); return
    }
    graph.push({ from: relative(from), specifier, to: relative(target) }); queue.push(target)
  }
  if (releaseId === undefined) {
    const hostEntry = resolveLocal(path.join(root, 'fortuneforge.client/src/main'))
    if (hostEntry) queue.push(hostEntry)
    else findings.push({ file: 'fortuneforge.client/src/main.tsx', line: 1, code: 'missing-host-entry', message: 'The frontend runtime entry is required.' })
    for (const workspace of packages.values()) if (workspace.workspace.startsWith('games/') || workspace.workspace.startsWith('packages/')) {
      const entry = resolveLocal(path.join(workspace.folder, 'src/index'))
      if (entry) queue.push(entry)
      else findings.push({ file: workspace.workspace, line: 1, code: 'missing-workspace-entry', message: 'Cannot establish a source runtime entry for this workspace.' })
    }
  }
  const games = path.join(root, 'games')
  const releaseEntries = new Set()
  if (fs.existsSync(games)) for (const folder of fs.readdirSync(games, { withFileTypes: true })) {
    if (!folder.isDirectory()) continue
    const game = path.join(games, folder.name)
    const configPath = path.join(game, 'game-release.json')
    if (!fs.existsSync(configPath)) continue
    try {
      const { id, frontend } = JSON.parse(read(configPath))
      if (releaseId !== undefined && id !== releaseId) continue
      if (releaseEntries.has(id)) throw new Error('Duplicate game release ID: ' + id)
      releaseEntries.add(id)
      if (frontend?.kind === 'frame') continue // Frame CSS is isolated from the host document.
      const directory = fs.realpathSync(path.resolve(game, frontend.directory))
      const entry = fs.realpathSync(path.resolve(directory, frontend.entry))
      if (!normalize(directory).startsWith(normalize(game) + '/') ||
          !normalize(entry).startsWith(normalize(directory) + '/') ||
          !sourceExtension.test(entry) || !fs.statSync(entry).isFile()) {
        throw new Error('The module release must have a source entry inside its game directory.')
      }
      queue.push(entry)
    } catch (error) {
      findings.push({ file: relative(configPath), line: 1, code: 'invalid-release-entry', message: error.message })
    }
  }
  if (releaseId !== undefined && !releaseEntries.has(releaseId))
    findings.push({ file: 'games', line: 1, code: 'missing-release-entry', message: 'No registered browser release for ' + releaseId })
  while (queue.length) {
    const file = queue.shift()
    if (sourceFiles.has(file) || cssFiles.has(file)) continue
    const text = read(file)
    if (file.endsWith('.css')) {
      cssFiles.add(file)
      findings.push(...inspectCss(text, relative(file), { game: gamePresentation(relative(file)) || relative(file).startsWith('node_modules/') }))
      try { postcss.parse(text).walkAtRules('import', rule => {
        const match = rule.params.match(/^(?:url\(\s*)?["']([^"']+)["']/)
        if (match) dependency(file, match[1])
        else findings.push({ file: relative(file), line: rule.source.start.line, code: 'unresolved-css-import', message: `Cannot establish CSS import boundary: ${rule.params}` })
      }) } catch { /* inspectCss already reports parse failures. */ }
      continue
    }
    sourceFiles.add(file)
    const ast = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true)
    walk(ast, node => {
      if ((ts.isImportDeclaration(node) && !node.importClause?.isTypeOnly || ts.isExportDeclaration(node) && !node.isTypeOnly) && node.moduleSpecifier && ts.isStringLiteralLike(node.moduleSpecifier)) dependency(file, node.moduleSpecifier.text)
      if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
        if (ts.isStringLiteralLike(node.arguments[0])) dependency(file, node.arguments[0].text)
        else if (!(node.arguments[0]?.getText(ast) === 'assets.entry.href' &&
          hostReleaseSink(relative(file), node, 'useGameRelease.ts', 'loadComponent')))
          findings.push({ file: relative(file), line: ast.getLineAndCharacterOfPosition(node.getStart(ast)).line + 1, code: 'dynamic-runtime-import', message: 'Computed runtime imports require an explicit enumerable boundary.' })
      }
    })
  }
  // Preview and integration JSX must obey the same provider API even though standalone
  // document framing CSS is deliberately outside the production runtime style graph.
  const roots = releaseId === undefined ? [...filesBelow(games), ...filesBelow(path.join(root, 'fortuneforge.client/src'))] : []
  const allSource = new Set([...sourceFiles, ...roots].filter(file => sourceExtension.test(file) && !ignoredSource(file)))
  for (const file of allSource) findings.push(...inspectSource(read(file), relative(file)))
  const unique = [...new Map(findings.map(finding => [JSON.stringify(finding), finding])).values()]
  return { findings: unique, sourceFiles: sourceFiles.size, checkedJsxSources: allSource.size, cssFiles: cssFiles.size, graph }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = inspectRepository()
  for (const finding of result.findings) console.error(`${finding.file}:${finding.line} [${finding.code}] ${finding.message}`)
  if (result.findings.length) process.exitCode = 1
  else console.log(`Verified canonical game navigation across ${result.checkedJsxSources} sources and ${result.cssFiles} transitive runtime CSS files (${result.graph.length} import edges).`)
}
