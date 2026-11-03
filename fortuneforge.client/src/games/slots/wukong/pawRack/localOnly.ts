export function isLoopbackPrototypeLocation(location: Pick<Location, 'hostname' | 'protocol'>): boolean {
  return (location.protocol === 'http:' || location.protocol === 'https:') &&
    ['localhost', '127.0.0.1', '[::1]', '::1'].includes(location.hostname.toLowerCase())
}
