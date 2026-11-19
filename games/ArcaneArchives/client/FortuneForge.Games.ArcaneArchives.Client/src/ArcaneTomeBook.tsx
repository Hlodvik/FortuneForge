// Edited: 2026-11-18T19:05:53-06:00 | Computer: Vinnabok
import { useEffect, useRef, useState } from 'react'
import { getSlotSymbolDefinition } from '@fortuneforge/slot-client-core'
import type { SlotTomePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols'
import lectern from './assets/art/optimized/arcane-archives-lectern-blank-tome-layer-v1.webp'
import pageTurnAtlas from './assets/art/tome/arcane-tome-page-flip-overlay-v1.png'
import './tomeBook.css'

type Turn = 'next' | 'previous'
type TomeBookProps = Pick<SlotTomePanelProps, 'state' | 'disabled' | 'reducedMotion' | 'onChangePage'> & {
  showIngredientTotals: boolean
}

const columns = 8
const rows = 6
const frameCount = columns * rows
const durationMs = 960

function pageTurnFrames(direction: Turn): Keyframe[] {
  return Array.from({ length: frameCount + 1 }, (_, index) => {
    const sequenceIndex = Math.min(index, frameCount - 1)
    const frame = direction === 'next' ? sequenceIndex : frameCount - 1 - sequenceIndex
    const x = (frame % columns) * 100 / (columns - 1)
    const y = Math.floor(frame / columns) * 100 / (rows - 1)
    return { backgroundPosition: `${x}% ${y}%`, offset: index / frameCount, easing: 'steps(1, end)' }
  })
}

export function ArcaneTomeBook({ state, disabled, reducedMotion, onChangePage, showIngredientTotals }: TomeBookProps) {
  const { pages } = state
  const pageIndex = Math.max(0, pages.findIndex(page => page.id === (state.activePage ?? state.selectedPage)))
  const page = pages[pageIndex]
  const isActive = state.activePage !== null
  const [turn, setTurn] = useState<Turn | null>(null)
  const [atlasReady, setAtlasReady] = useState(false)
  const overlayRef = useRef<HTMLSpanElement>(null)
  const playingTurn = reducedMotion ? null : turn

  useEffect(() => {
    if (reducedMotion) return
    const preload = new Image()
    let mounted = true
    preload.src = pageTurnAtlas
    void preload.decode().then(() => {
      if (mounted) setAtlasReady(true)
    }, () => { /* The Tome stays usable when the animation cannot load. */ })
    return () => { mounted = false }
  }, [reducedMotion])

  useEffect(() => {
    if (!playingTurn || !overlayRef.current) return
    const overlay = overlayRef.current
    if (typeof overlay.animate !== 'function') {
      setTurn(null)
      return
    }
    const motion = overlay.animate(pageTurnFrames(playingTurn), { duration: durationMs })
    void motion.finished.then(() => setTurn(null), () => {})
    return () => motion.cancel()
  }, [playingTurn])

  useEffect(() => {
    if (reducedMotion) setTurn(null)
  }, [reducedMotion])

  if (!page) return null
  const turnDisabled = disabled || isActive || playingTurn !== null
  const previousUnavailable = turnDisabled || pageIndex === 0
  const nextUnavailable = turnDisabled || pageIndex === pages.length - 1

  const changePage = (direction: Turn) => {
    if (direction === 'next' ? nextUnavailable : previousUnavailable) return
    const target = pages[pageIndex + (direction === 'next' ? 1 : -1)]
    if (!reducedMotion && atlasReady) setTurn(direction)
    onChangePage(target.id, false)
  }

  return <div className="arcane-tome-book">
    <div className="arcane-tome-book__stage" aria-busy={playingTurn !== null}>
      <div className="arcane-tome-book__canvas">
        <img className="arcane-tome-book__lectern" src={lectern} alt="" />
        {playingTurn && <span className="arcane-tome-book__turn" ref={overlayRef} aria-hidden="true"
          data-turn-direction={playingTurn}
          style={{ backgroundImage: `url("${pageTurnAtlas}")`, backgroundSize: `${columns * 100}% ${rows * 100}%` }} />}
        <article className="arcane-tome-book__spread" aria-live="polite" data-turning={playingTurn !== null || undefined}>
          <div className="arcane-tome-book__page-title">
            <h3>{page.label}</h3>
            <small>{pageIndex + 1} / {pages.length}</small>
          </div>
          <ul aria-label="Boosted ingredients">
            {page.ingredients.map(id => {
              const ingredient = getSlotSymbolDefinition(ARCANE_ARCHIVES_SYMBOLS, id)
              return <li key={id}>
                <img src={ingredient.image} alt="" />
                <span>{ingredient.label}{showIngredientTotals && <small> · {state.ingredients[id] ?? 0}</small>}</span>
              </li>
            })}
          </ul>
        </article>
      </div>
    </div>
    <div className="arcane-tome-book__controls">
      <nav aria-label="Tome pages" onKeyDown={event => {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
        event.preventDefault()
        changePage(event.key === 'ArrowRight' ? 'next' : 'previous')
      }}>
        <button type="button" aria-label="Previous page" aria-disabled={previousUnavailable}
          onClick={() => changePage('previous')}>‹ Previous</button>
        <button type="button" aria-label="Next page" aria-disabled={nextUnavailable}
          onClick={() => changePage('next')}>Next ›</button>
      </nav>
      <p>{isActive
        ? `${page.label} · ${state.boostedSpinsRemaining} boosted ${state.boostedSpinsRemaining === 1 ? 'spin' : 'spins'} left`
        : state.grimoireReady
          ? 'Activate this page to collect two units of each featured ingredient per landing for five paid spins.'
          : 'Land a Grand Grimoire to activate a page. Its featured ingredients give two units per landing for five paid spins.'}</p>
      {state.grimoireReady && !isActive && <button className="arcane-tome-activate" type="button"
        disabled={turnDisabled} onClick={() => onChangePage(page.id, true)}>Activate {page.label}</button>}
    </div>
  </div>
}
