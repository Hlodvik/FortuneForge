// Edited: 2026-11-18T22:59:30-06:00 | Computer: Vinnabok
/** Shared site chrome used by both the complete app and the site-only entry. */
export function siteShellClasses(pathname: string): string[] {
  return [
    'app-shell',
    pathname === '/' ? 'app-shell--landing' : '',
    pathname === '/home' || pathname.startsWith('/home/') || pathname === '/balance' ? 'app-shell--home' : '',
  ].filter(Boolean)
}
