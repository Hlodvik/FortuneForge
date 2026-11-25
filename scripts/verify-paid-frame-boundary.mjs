import { existsSync, readFileSync } from 'node:fs'
import { dirname, extname, relative, resolve } from 'node:path'

/** Entry reachability evidence only; API behavior and paid settlement still require execution tests. */
export function verifyPaidFrameBoundary({ root, clientRoot, entry, apiEntry, hostEntry }) {
  const errors = [], reachable = new Set()
  const inside = file => { const path = relative(clientRoot, file); return path !== '..' && !path.startsWith('../') && !path.startsWith('..\\') }
  function visit(file) {
    if (reachable.has(file) || !inside(file) || !existsSync(file)) return
    reachable.add(file)
    const text = readFileSync(file, 'utf8')
    const references = extname(file) === '.html'
      ? [...text.matchAll(/<script\b[^>]*\bsrc=["']([^"']+)["']/g)].map(match => match[1])
      : [...text.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s*)['"]([^'"]+)['"]/g)].map(match => match[1])
    for (const reference of references) {
      if (!reference.startsWith('.') && !reference.startsWith('/')) continue
      const bare = reference.split('?')[0]
      const path = bare.startsWith('/') ? resolve(clientRoot, '.' + bare) : resolve(dirname(file), bare)
      if (!inside(path)) { errors.push('frame imports outside its client package'); continue }
      for (const candidate of [path, path + '.ts', path + '.js', path + '.tsx']) {
        if (['.html', '.js', '.ts', '.tsx'].includes(extname(candidate)) && existsSync(candidate)) { visit(candidate); break }
      }
    }
  }
  visit(entry)
  const api = typeof apiEntry === 'string' ? resolve(clientRoot, apiEntry) : null
  if (!api || !inside(api) || !reachable.has(api) || !/\bauthenticatedFetch\b/.test(readFileSync(api, 'utf8')))
    errors.push('frame entry does not reach a package-owned authenticated API client')
  const host = typeof hostEntry === 'string' && existsSync(resolve(root, hostEntry)) ? readFileSync(resolve(root, hostEntry), 'utf8') : ''
  if (!host.includes('attachAuthenticatedFrameTransport') || /\b(?:wagerPoints|payout|validate\w*Outcome|create\w*GameApi|loadSession)\b|\.spin\(/.test(host))
    errors.push('frame host must use neutral authenticated transport without game commands or outcome validation')
  return { errors, reachable: [...reachable] }
}
