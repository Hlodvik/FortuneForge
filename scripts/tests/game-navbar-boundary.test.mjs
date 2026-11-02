import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { inspectCss, inspectSource, inspectRepository, isScopedSelector } from '../verify-game-navbar.mjs'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const gameCss = 'games/Example/client/src/example.css'
const gameSource = 'games/Example/client/src/Example.tsx'
const codes = findings => findings.map(finding => finding.code)

test('rejects whole-document and generic control styling, retaining shared token and animation definitions', () => {
  const result = inspectCss(':root{font-family:Fantasy} body{background:red} button{color:red} *{opacity:.5}', gameCss)
  assert.equal(result.filter(item => item.code === 'global-game-style').length, 4)
  assert.deepEqual(inspectCss(':root{--ff-game-felt:#003823}@font-face{font-family:Game;src:url(font.woff2)}@keyframes blink{0%{opacity:0}100%{opacity:1}}.game{animation:blink 1s}', gameCss), [])
})

test('parses functional selector alternatives instead of accepting a class anywhere in the selector text', () => {
  for (const selector of [':is(body,.game) button', ':where(button,.game)', ':not(.game) button', ':has(.game) button', 'body.game button', '.game + button']) {
    assert.equal(isScopedSelector(selector), false, selector)
    assert.ok(codes(inspectCss(`${selector}{color:red}`, gameCss)).includes('global-game-style'))
  }
  for (const selector of ['.game :is(button,a)', ':is(.game button,.local-button)', ':where(.game) *', '.game button+button', '.game>div+div', '.local-card+.local-card', "[data-dialog-theme='game'] .grid + small"]) {
    assert.equal(isScopedSelector(selector), true, selector)
    assert.deepEqual(inspectCss(`${selector}{color:red}`, gameCss), [])
  }
})

test('forbids game selectors and variables that reach the canonical navbar at any nesting level', () => {
  const result = inspectCss('.game :is(.site-navbar,.local){color:red}.game [data-game-navbar]{padding:0}.game{--site-nav-gold:red;color:var(--site-nav-gold)}', gameCss)
  assert.equal(result.filter(item => item.code === 'game-navbar-selector').length, 2)
  assert.equal(result.filter(item => item.code === 'game-navbar-variable').length, 2)
  assert.ok(codes(inspectCss(':root{--site-nav-gold:red}', gameCss)).includes('game-navbar-variable'))
  assert.ok(codes(inspectCss('.in-game-shell{opacity:.5}', gameCss)).includes('game-navbar-selector'))
  assert.deepEqual(inspectCss('.app-shell:has(>.in-game-shell>.game-page){background:#050a08}', 'fortuneforge.client/src/app/routes/ExampleRoute.css'), [])
  assert.deepEqual(inspectCss('.game__header{color:red}.game__header nav a{color:gold}', gameCss), [])
})

test('checks lazy host presentation styles while allowing the shared baseline and invoice-only print treatment', () => {
  assert.ok(codes(inspectCss('button:focus-visible{outline:2px solid red}', 'fortuneforge.client/src/pages/cards/blackjack/blackjack.css')).includes('global-game-style'))
  assert.deepEqual(inspectCss(':root{font-family:Sora}button{font:inherit}', 'fortuneforge.client/src/index.css'), [])
  assert.deepEqual(inspectCss('@media print{.home-lobby:has(.invoice-main)>.site-navbar{display:none}}', 'fortuneforge.client/src/pages/payments/paymentInvoices.css'), [])
})

test('accepts canonical game state props and independent local headers and body controls', () => {
  const source = `import {SiteNavbar} from './SiteNavbar'; import {InGameNavbar} from './InGameNavbar'; import {InGameShell} from './InGameShell';
    <SiteNavbar mode="game" account={account} balance={balance} authenticated reportTitle="Craps" onNavigate={stopAutoSpin}/>;
    <InGameNavbar account={account} title="Game" balance={balance}/>;
    <InGameShell className="game-page" bodyClassName="game-body"><header className="game__header"><nav><button>Rules</button></nav></header></InGameShell>;
    <SiteNavbar mode="account" className="account-header" actions={<button>Account</button>}/>;`
  assert.deepEqual(inspectSource(source, gameSource), [])
})

test('rejects aliases, namespaces, custom children, unknown mode presentation, and unsafe navbar/shell props', () => {
  const source = `import {SiteNavbar as Nav} from './SiteNavbar'; import * as Shared from './InGameNavbar';
    const GameNav=Nav;
    <GameNav mode={'game'} className="game-nav" style={{color:'red'}} actions={<button/>}/>;
    <Shared.InGameNavbar theme="casino"><button>Injected</button></Shared.InGameNavbar>;
    <Nav mode={dynamicMode} className="maybe-game"/>;
    <InGameShell theme="cards" actions={<button/>}/>;`
  const result = codes(inspectSource(source, gameSource))
  assert.equal(result.filter(code => code === 'navbar-presentation-prop').length, 7)
  assert.equal(result.filter(code => code === 'navbar-children').length, 1)
})

test('rejects ambiguous prop spreads and forged reserved markup even through constant string concatenation', () => {
  const result = codes(inspectSource(`<SiteNavbar mode="game" {...props}/>;<InGameNavbar {...props}/>;<header data-game-navbar className={'site-'+'navbar'}/>`, gameSource))
  assert.equal(result.filter(code => code === 'navbar-prop-spread').length, 2)
  assert.equal(result.filter(code => code === 'navbar-markup-owner').length, 2)
})

test('blocks direct and aliased navbar DOM styling, attributes, dataset changes and mutation helpers', () => {
  const source = `const selector='.site-navbar'; const header=document.querySelector(selector); const alias=header; const style=alias.style;
    style.setProperty('color','red'); alias.classList.add('casino'); alias.setAttribute('style','color:red');
    alias.dataset.theme='casino'; Object.assign(alias.style,{color:'blue'}); alias.replaceChildren(document.createElement('span'));`
  const result = codes(inspectSource(source, gameSource))
  assert.equal(result.filter(code => code === 'navbar-dom-mutation').length, 6)
})

test('tracks destructured style aliases, local helper parameters, selected descendants and NodeList callbacks', () => {
  const source = `const nav=document.querySelector('[data-game-navbar]'); const {style:paint}=nav; paint.color='red';
    function recolor(target){target.style.background='red'} recolor(nav);
    nav.querySelector('a').remove();
    document.querySelectorAll('.site-navbar a').forEach(link=>{link.style.color='red'});`
  assert.equal(codes(inspectSource(source, gameSource)).filter(code => code === 'navbar-dom-mutation').length, 4)
})

test('allows read-only canonical balance geometry, local surface writes, and reversible body overflow state', () => {
  const source = `const target=creditTileRef.current??document.querySelector('.in-game-shell > [data-game-navbar] .site-header-balance');
    const rectangle=target?.getBoundingClientRect(); const destination=rectangle?.left??0;
    const surface=document.querySelector('.game'); surface.style.setProperty('--chip-size','12px');
    const previous=document.body.style.overflow; document.body.style.overflow='hidden'; document.body.style.overflow=previous;`
  assert.deepEqual(inspectSource(source, gameSource), [])
})

test('blocks global document presentation writes while preserving the shared route lifecycle owner', () => {
  assert.ok(codes(inspectSource(`document.body.style.fontFamily='Fantasy';document.documentElement.style.setProperty('--site-nav-gold','red');`, gameSource)).includes('navbar-dom-mutation'))
  assert.deepEqual(inspectSource(`document.documentElement.classList.add('ff-game-active');document.body.classList.remove('ff-game-active');`, 'fortuneforge.client/src/app/App.tsx'), [])
})

test('rejects uninspectable runtime stylesheet and HTML injection', () => {
  const source = `const style=document.createElement('style'); const sheet=new CSSStyleSheet(); sheet.insertRule('.site-navbar{color:red}');
    <style>{css}</style>; <div dangerouslySetInnerHTML={{__html:html}}/>;`
  assert.equal(codes(inspectSource(source, gameSource)).filter(code => code === 'runtime-style-injection').length, 5)
})

function fixture(t, extraFiles = {}) {
  const temporaryRoot = path.join(repo, '.tmp/navbar-boundary-tests')
  fs.mkdirSync(temporaryRoot, { recursive: true })
  const root = fs.mkdtempSync(path.join(temporaryRoot, 'fixture-'))
  t.after(() => {
    const resolved = path.resolve(root)
    assert.ok(resolved.startsWith(path.resolve(temporaryRoot) + path.sep), 'Fixture cleanup stays inside the intended temporary root.')
    fs.rmSync(resolved, { recursive: true, force: true })
  })
  const files = {
    'package.json': JSON.stringify({ workspaces: ['games/Example/client', 'packages/presentation', 'fortuneforge.client'] }),
    'fortuneforge.client/package.json': JSON.stringify({ name: 'host' }),
    'fortuneforge.client/src/main.tsx': `import './index.css'; import('./app/App')`,
    'fortuneforge.client/src/index.css': ':root{font-family:Sora}*{box-sizing:border-box}',
    'fortuneforge.client/src/app/App.tsx': `import {simulation} from '@fixture/game'; export const App=()=>simulation;`,
    'games/Example/client/package.json': JSON.stringify({ name: '@fixture/game', exports: { '.': { import: './dist/index.js' }, './styles.css': './dist/index.css' } }),
    'games/Example/client/src/index.ts': `export {simulation} from './simulation';import './game.css';`,
    'games/Example/client/src/simulation.ts': 'export const simulation=1',
    'games/Example/client/src/game.css': '@import "./controls.css";.game{color:white}',
    'games/Example/client/src/controls.css': '.game button{color:gold}',
    'packages/presentation/package.json': JSON.stringify({ name: '@fixture/presentation', exports: { '.': { import: './dist/index.js' }, './money': { import: './dist/formatMoney.js' } } }),
    'packages/presentation/src/index.ts': 'export const card=1',
    'packages/presentation/src/formatMoney.ts': 'export const money=1',
    ...extraFiles,
  }
  for (const [file, content] of Object.entries(files)) { const full = path.join(root, file); fs.mkdirSync(path.dirname(full), { recursive: true }); fs.writeFileSync(full, content) }
  return root
}

test('follows package index side-effect CSS and nested CSS imports even when the host only imports simulation', t => {
  const root = fixture(t, { 'games/Example/client/src/controls.css': 'button{background:red}' })
  const result = inspectRepository(root)
  assert.ok(result.findings.some(item => item.file === 'games/Example/client/src/controls.css' && item.code === 'global-game-style'))
  assert.ok(result.graph.some(edge => edge.specifier === '@fixture/game'))
  assert.ok(result.graph.some(edge => edge.specifier === './controls.css'))
})

test('resolves declared workspace export subpaths, CSS exports and lazy host pages without built dist output', t => {
  const root = fixture(t, {
    'fortuneforge.client/src/app/App.tsx': `import '@fixture/game/styles.css';import {money} from '@fixture/presentation/money';import('./Page');export const App=money`,
    'fortuneforge.client/src/app/Page.tsx': `import './page.css';export const Page=()=> <header className="local-header">Local</header>`,
    'fortuneforge.client/src/app/page.css': '.local-header{color:white}',
  })
  const result = inspectRepository(root)
  assert.deepEqual(result.findings, [])
  assert.equal(result.cssFiles, 4)
  assert.ok(result.graph.some(edge => edge.to === 'packages/presentation/src/formatMoney.ts'))
})

test('reports unresolved/computed runtime boundaries instead of silently excluding them', t => {
  const root = fixture(t, { 'fortuneforge.client/src/app/App.tsx': `import './missing';import(variable);export const App=1` })
  const result = codes(inspectRepository(root).findings)
  assert.ok(result.includes('unresolved-runtime-import'))
  assert.ok(result.includes('dynamic-runtime-import'))
})

test('inspects directly imported vendor CSS and Vite absolute CSS paths instead of trusting their location', t => {
  const root = fixture(t, {
    'fortuneforge.client/src/app/App.tsx': `import '@fixture/theme';import '@fixture/font';import '/public-theme.css';export const App=1`,
    'node_modules/@fixture/theme/package.json': JSON.stringify({ name: '@fixture/theme', exports: './index.css' }),
    'node_modules/@fixture/theme/index.css': '.site-navbar{color:red}',
    'node_modules/@fixture/font/package.json': JSON.stringify({ name: '@fixture/font', exports: './index.css' }),
    'node_modules/@fixture/font/index.css': '@font-face{font-family:Shared;src:url(font.woff2);font-display:swap}',
    'fortuneforge.client/public/public-theme.css': 'button{font-family:Fantasy}',
  })
  const result = inspectRepository(root).findings
  assert.ok(result.some(item => item.file === 'node_modules/@fixture/theme/index.css' && item.code === 'game-navbar-selector'))
  assert.ok(result.some(item => item.file === 'fortuneforge.client/public/public-theme.css' && item.code === 'global-game-style'))
  assert.equal(result.some(item => item.file === 'node_modules/@fixture/font/index.css'), false)
})

test('checks standalone preview API calls while excluding dev-only document framing from runtime CSS', t => {
  const root = fixture(t, {
    'games/Example/client/src/dev/main.tsx': `import './preview.css';<InGameNavbar className="custom"/>`,
    'games/Example/client/src/dev/preview.css': 'html,body,#root{height:100%;margin:0}',
  })
  const result = inspectRepository(root)
  assert.ok(codes(result.findings).includes('navbar-presentation-prop'))
  assert.equal(result.findings.some(item => item.file.endsWith('preview.css')), false)
})
