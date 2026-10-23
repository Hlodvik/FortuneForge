import { useCatalogueVisibility } from './catalogVisibility'
export function CatalogueStatus() {
  const visibility = useCatalogueVisibility()
  return visibility.error ? <p role="alert">{visibility.error}</p> : visibility.loading ? <p role="status">Checking available games…</p> : null
}
