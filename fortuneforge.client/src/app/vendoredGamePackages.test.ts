import { createHash } from 'node:crypto'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { basename } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

type PackageManifest = Readonly<{ dependencies?: Readonly<Record<string, string>> }>
type PackageLock = Readonly<{ packages?: Readonly<Record<string, Readonly<{ resolved?: string }>>> }>

const packageJsonUrl = new URL('../../package.json', import.meta.url)
const packageLockUrl = new URL('../../package-lock.json', import.meta.url)
const vendoredManifestUrl = new URL('../../../packages/client/MANIFEST.sha256', import.meta.url)
const appPackage = JSON.parse(readFileSync(packageJsonUrl, 'utf8')) as PackageManifest
const packageLock = JSON.parse(readFileSync(packageLockUrl, 'utf8')) as PackageLock
const vendoredManifest = readFileSync(vendoredManifestUrl, 'utf8')

function checksum(path: string): string {
  return createHash('sha256').update(readFileSync(path)).digest('hex')
}

describe('vendored game packages', () => {
  it('pins every game client dependency to a verified local package', () => {
    const gamePackages = Object.entries(appPackage.dependencies ?? {})
      .filter(([name]) => name.startsWith('@fortuneforge/games-'))

    expect(gamePackages.length).toBeGreaterThan(0)

    for (const [name, specifier] of gamePackages) {
      expect(specifier, `${name} must be a local immutable artifact`).toMatch(/^file:/)
      const artifactUrl = new URL(specifier.slice('file:'.length), packageJsonUrl)
      const artifactPath = fileURLToPath(artifactUrl)
      const artifactName = basename(artifactPath)

      expect(existsSync(artifactPath), `${name} artifact is missing`).toBe(true)
      if (statSync(artifactPath).isDirectory()) {
        const sourcePackageBaseUrl = new URL(`${artifactUrl.href}/`)
        const sourcePackage = JSON.parse(readFileSync(new URL('package.json', sourcePackageBaseUrl), 'utf8')) as Readonly<{
          files?: readonly string[]
          name?: string
        }>

        expect(sourcePackage.name).toBe(name)
        expect(sourcePackage.files).toContain('dist')
        expect(existsSync(fileURLToPath(new URL('dist', sourcePackageBaseUrl)))).toBe(true)
        expect(packageLock.packages?.[`node_modules/${name}`]?.resolved).toBe(specifier.slice('file:'.length))
      } else {
        expect(packageLock.packages?.[`node_modules/${name}`]?.resolved).toBe(specifier)
        expect(vendoredManifest).toContain(`${checksum(artifactPath)}  ${artifactName}`)
      }
    }
  })
})
