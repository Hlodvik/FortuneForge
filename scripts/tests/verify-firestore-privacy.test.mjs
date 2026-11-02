import assert from 'node:assert/strict';
import { test } from 'node:test';
import { emulatorTarget } from '../verify-firestore-privacy.mjs';

test('privacy proof only accepts explicit localhost demo-project targets', () => {
  assert.equal(emulatorTarget('127.0.0.1:8080', 'demo-mines-proof').origin, 'http://127.0.0.1:8080');
  assert.equal(emulatorTarget('localhost:9090', 'demo-local').origin, 'http://localhost:9090');
  assert.equal(emulatorTarget('[::1]:8080', 'demo-local').origin, 'http://[::1]:8080');
});

test('privacy proof refuses production, external, credentialed and ambiguous targets', () => {
  for (const host of ['firestore.googleapis.com:443', '0.0.0.0:8080', '127.0.0.1.evil:8080',
    'user@localhost:8080', 'localhost', 'localhost:8080/path', 'localhost:8080?query=1']) {
    assert.throws(() => emulatorTarget(host, 'demo-local'), host);
  }
  for (const project of ['fortuneforgegame', 'demo-', 'demo-Project', 'demo-local/path']) {
    assert.throws(() => emulatorTarget('127.0.0.1:8080', project), project);
  }
});
