import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import test from 'node:test'
import ts from 'typescript'

const source = readFileSync(new URL('../src/sunwheel.ts', import.meta.url), 'utf8')
const { outputText } = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } })
const { decodeSunwheelFeature, getSunwheelPath } = await import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`)
const positions = rows => rows.map((row, reel) => ({ reel, row }))

// Independently calculated all-Sunstone boards at the new R3 minimum wager.
function fifthSunFixture(free = false) {
  const paths = free ? [[3, 4, 5, 4, 3], [1, 0, 5, 0, 1], [0, 1, 2, 1, 0]]
    : [[2, 3, 0, 3, 2], [2, 1, 0, 1, 2], [1, 2, 3, 2, 1]]
  const rotation = free ? [3, 4, 5] : [2, 3, 0]
  return {
    contractVersion: 2, kind: 'fifth-sun', mathVersion: 'fifth-sun-wheel-v1',
    dialStep: free ? 3 : 2, rows: free ? 6 : 4, isEclipseSpin: free,
    heart: { reel: 2, row: free ? 3 : 2, symbolId: 'SUNSTONE', lit: true },
    rings: ['jaguar', 'eagle', 'serpent'].map((id, index) => ({
      id, symbolId: ['JAGUAR', 'EAGLE', 'SERPENT'][index], rotationSteps: rotation[index],
      path: positions(paths[index]), matchedPositions: positions(paths[index]),
      matches: 5, aligned: true, baseMultiplier: 2, awardPoints: free ? 144 : 72,
    })),
    alignedRings: 3, alignmentMultiplier: 3, eclipseTriggered: true,
    eclipseAwardPoints: free ? 48 : 24, totalAwardPoints: free ? 480 : 240,
    maxAwardMultiplier: free ? 40 : 20,
  }
}

test('Fifth Sun renders exact paid and six-row free-spin feature values', () => {
  for (const free of [false, true]) {
    const feature = fifthSunFixture(free)
    assert.equal(decodeSunwheelFeature(feature), feature)
    assert.equal(feature.totalAwardPoints, free ? 480 : 240)
  }
})

test('Fifth Sun rejects mixed historical identities, geometry and financial evidence', () => {
  const changes = [
    feature => { feature.contractVersion = 1 },
    feature => { feature.kind = 'sunstone-eclipse' },
    feature => { feature.mathVersion = 'sunstone-eclipse-v1' },
    feature => { feature.rings[0].symbolId = '7' },
    feature => { feature.heart.symbolId = 'POWER' },
    feature => { feature.rows = 5 },
    feature => { feature.rings[0].path[0].row = 1 },
    feature => { feature.rings[1].rotationSteps = 0 },
    feature => { feature.rings[0].awardPoints -= 1 },
    feature => { feature.maxAwardMultiplier = 40 },
  ]
  for (const change of changes) {
    const feature = fifthSunFixture()
    change(feature)
    assert.equal(decodeSunwheelFeature(feature), null)
  }
})

test('Fifth Sun guide geometry shares the exact validated paths', () => {
  for (const free of [false, true]) {
    const feature = fifthSunFixture(free)
    for (const ring of feature.rings)
      assert.deepEqual(getSunwheelPath(ring.id, feature.rows, feature.dialStep), ring.path)
  }
  assert.throws(() => getSunwheelPath('jaguar', 5, 0), RangeError)
  assert.throws(() => getSunwheelPath('jaguar', 4, 4), RangeError)
})
