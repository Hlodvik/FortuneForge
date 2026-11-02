import '@fontsource-variable/sora'
import { SiteNavbar } from '../../components/SiteNavbar'
import './notFoundPage.css'

export function NotFoundPage() {
  return (
    <div className="not-found-page">
      <SiteNavbar mode="public" />
      <main className="not-found-page__main">
      <section className="not-found-page__card" aria-labelledby="not-found-title">
        <p className="not-found-page__eyebrow">Page not found · 404</p>
        <h1 id="not-found-title">This page isn’t in the deck.</h1>
        <p>The link may have changed. Head back to Fortune Forge to find your way.</p>
        <a className="not-found-page__button" href="/">Return to Fortune Forge <span aria-hidden="true">→</span></a>
      </section>
      </main>
    </div>
  )
}
