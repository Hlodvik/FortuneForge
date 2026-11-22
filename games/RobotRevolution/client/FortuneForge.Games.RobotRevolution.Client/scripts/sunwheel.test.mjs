import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import test from 'node:test'
import ts from 'typescript'

const source = readFileSync(new URL('../src/sunwheel.ts', import.meta.url), 'utf8')
const { outputText } = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } })
const { decodeSunwheelFeature } = await import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`)
const positions = rows => rows.map((row, reel) => ({ reel, row }))

// These are the exact independently computed all-Sunstone backend fixtures.
function eclipseFixture(free = false) {
  const paths = free ? [[3, 4, 5, 4, 3], [1, 0, 5, 0, 1], [0, 1, 2, 1, 0]]
    : [[2, 3, 0, 3, 2], [2, 1, 0, 1, 2], [1, 2, 3, 2, 1]]
  const rotation = free ? [3, 4, 5] : [2, 3, 0]
  return {
    contractVersion: 1, kind: 'sunstone-eclipse', mathVersion: 'sunstone-eclipse-v1',
    dialStep: free ? 3 : 2, rows: free ? 6 : 4, isEclipseSpin: free,
    heart: { reel: 2, row: free ? 3 : 2, symbolId: 'POWER', lit: true },
    rings: ['jaguar', 'eagle', 'serpent'].map((id, index) => ({
      id, symbolId: ['7', '6', '5'][index], rotationSteps: rotation[index], path: positions(paths[index]),
      matchedPositions: positions(paths[index]), matches: 5, aligned: true, baseMultiplier: 2,
      awardPoints: free ? 1200 : 600,
    })),
    alignedRings: 3, alignmentMultiplier: 3, eclipseTriggered: true,
    eclipseAwardPoints: free ? 400 : 200, totalAwardPoints: free ? 4000 : 2000,
    maxAwardMultiplier: free ? 40 : 20,
  }
}

test('renders exact base and free-spin server feature values without recalculating money', () => {
  for (const free of [false, true]) {
    const feature = eclipseFixture(free)
    assert.equal(decodeSunwheelFeature(feature), feature)
    assert.equal(feature.totalAwardPoints, free ? 4000 : 2000)
  }
})

test('old settled rounds can omit their feature', () => {
  assert.equal(decodeSunwheelFeature(null), null)
  assert.equal(decodeSunwheelFeature(undefined), null)
})

test('rejects foreign versions, impossible geometry, guardians and award totals', () => {
  const changes = [
    feature => { feature.kind = 'foreign' },
    feature => { feature.contractVersion = 2 },
    feature => { feature.mathVersion = 'unverified' },
    feature => { feature.rows = 5 },
    feature => { feature.rings[0].id = 'eagle' },
    feature => { feature.rings[0].symbolId = '6' },
    feature => { feature.rings[0].rotationSteps = 1 },
    feature => { feature.rings[0].path[0].row = 1 },
    feature => { feature.rings[0].matchedPositions[0] = feature.rings[0].matchedPositions[1] },
    feature => { feature.rings[0].baseMultiplier = 1 },
    feature => { feature.rings[0].aligned = false },
    feature => { feature.totalAwardPoints += 1 },
    feature => { feature.maxAwardMultiplier = 100 },
    feature => { feature.rings[0].awardPoints = Infinity },
    feature => { feature.heart.lit = false },
    feature => { feature.heart.symbolId = 'ACE' },
    feature => { feature.alignmentMultiplier = 2 },
  ]
  for (const change of changes) {
    const feature = eclipseFixture()
    change(feature)
    assert.equal(decodeSunwheelFeature(feature), null)
  }
})

test('a minimum-wager three-match guardian has a positive one-point award', () => {
  const feature = eclipseFixture()
  const paths = [[0, 1, 2, 1, 0], [0, 3, 2, 3, 0], [3, 0, 1, 0, 3]]
  feature.dialStep = 0
  feature.heart = { reel: 2, row: 2, symbolId: '7', lit: false }
  feature.rings.forEach((ring, index) => {
    ring.rotationSteps = index
    ring.path = positions(paths[index])
    ring.matchedPositions = index === 0 ? ring.path.slice(0, 3) : []
    ring.matches = index === 0 ? 3 : 0
    ring.aligned = index === 0
    ring.baseMultiplier = index === 0 ? 0.5 : 0
    ring.awardPoints = index === 0 ? 1 : 0
  })
  feature.alignedRings = 1
  feature.alignmentMultiplier = 1
  feature.eclipseTriggered = false
  feature.eclipseAwardPoints = 0
  feature.totalAwardPoints = 1
  assert.equal(decodeSunwheelFeature(feature), feature)
  const zeroAward = structuredClone(feature)
  zeroAward.rings[0].awardPoints = 0
  zeroAward.totalAwardPoints = 0
  assert.equal(decodeSunwheelFeature(zeroAward), null)
})
