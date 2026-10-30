import '@fontsource-variable/sora'
import './notFoundPage.css'

export function NotFoundPage() {
  return (
    <main className="not-found-page">
      <a className="not-found-page__brand" href="/" aria-label="Fortune Forge home">
        <span className="not-found-page__spade" aria-hidden="true" /> FORTUNE FORGE
      </a>
      <section className="not-found-page__card" aria-labelledby="not-found-title">
        <p className="not-found-page__eyebrow">Page not found · 404</p>
        <h1 id="not-found-title">This page isn’t in the deck.</h1>
        <p>The link may have changed. Head back to Fortune Forge to find your way.</p>
        <a className="not-found-page__button" href="/">Return to Fortune Forge <span aria-hidden="true">→</span></a>
      </section>
    </main>
  )
}
