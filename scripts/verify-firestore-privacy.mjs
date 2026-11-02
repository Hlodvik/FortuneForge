import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

// This proof deliberately uses the browser REST protocol, not the privileged server SDK.
// Unsigned mock Firebase tokens are understood ONLY by the Firestore emulator.
export function emulatorTarget(host, project) {
  const url = new URL(`http://${host}`);
  assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(url.hostname), 'Only a loopback emulator is allowed.');
  assert.ok(url.port && url.pathname === '/' && !url.search && !url.hash && !url.username && !url.password,
    'Supply only the explicit loopback emulator host and port.');
  assert.match(project, /^demo-[a-z0-9-]+$/, 'Only a non-production demo-* project is allowed.');
  return { origin: url.origin, project };
}

function emulatorToken(project, uid, claims = {}) {
  const issuedAt = Math.floor(Date.now() / 1000);
  const encoded = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  return `${encoded({ alg: 'none', typ: 'JWT' })}.${encoded({
    ...claims, iss: `https://securetoken.google.com/${project}`, aud: project,
    iat: issuedAt, exp: issuedAt + 3600, auth_time: issuedAt, sub: uid, user_id: uid,
    firebase: { sign_in_provider: 'custom', identities: {} },
  })}.`;
}

async function request(target, path, { token, method = 'GET', body } = {}) {
  return fetch(`${target.origin}${path}`, {
    method,
    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
    signal: AbortSignal.timeout(15_000),
  });
}

async function loadRules(target, content) {
  const result = await request(target, `/emulator/v1/projects/${target.project}:securityRules`, {
    method: 'PUT', body: { rules: { files: [{ content }] } },
  });
  assert.equal(result.status, 200, `Local rules failed to load: ${await result.text()}`);
}

export async function verifyPrivacy(host = process.env.FIRESTORE_EMULATOR_HOST ?? '127.0.0.1:8080') {
  const target = emulatorTarget(host, `demo-mines-privacy-${randomUUID()}`);
  const root = `/v1/projects/${target.project}/databases/(default)/documents`;
  const seed = { fields: { userId: { stringValue: 'test-owner' }, board: { stringValue: 'private-test-board' } } };
  const owner = emulatorToken(target.project, 'test-owner');
  const other = emulatorToken(target.project, 'another-player');
  const fakeAdmin = emulatorToken(target.project, 'browser-admin', { admin: true, role: 'admin' });

  // A positive control proves mock authentication reaches request.auth instead of
  // accidentally passing an anonymous-only test. No real project is reachable.
  await loadRules(target, `rules_version = '2'; service cloud.firestore {
    match /databases/{database}/documents { match /privacyControl/{id} {
      allow read: if request.auth != null && request.auth.uid == 'test-owner';
    } }
  }`);
  assert.equal((await request(target, `${root}/privacyControl/one`, { token: 'owner', method: 'PATCH', body: seed })).status, 200);
  assert.equal((await request(target, `${root}/privacyControl/one`, { token: owner })).status, 200);
  assert.equal((await request(target, `${root}/privacyControl/one`, { token: other })).status, 403);
  assert.equal((await request(target, `${root}/privacyControl/one`)).status, 403);

  const rulesPath = new URL('../firestore.rules', import.meta.url);
  await loadRules(target, await readFile(rulesPath, 'utf8'));
  let denied = 0;
  for (const collection of ['minesRounds', 'minesSessions', 'minesCommands', 'userBalances', 'balanceTransactions']) {
    for (const docPath of [`${collection}/one`, `${collection}/one/private/nested`]) {
      assert.equal((await request(target, `${root}/${docPath}`, { token: 'owner', method: 'PATCH', body: seed })).status, 200);
      const collectionPath = docPath.slice(0, docPath.lastIndexOf('/'));
      for (const token of [undefined, owner, other, fakeAdmin]) {
        const operations = [
          [`${root}/${docPath}`, {}],
          [`${root}/${collectionPath}`, {}],
          [`${root}/${docPath}`, { method: 'PATCH', body: seed }],
          [`${root}/${collectionPath}/new`, { method: 'PATCH', body: seed }],
          [`${root}/${docPath}`, { method: 'DELETE' }],
        ];
        for (const [path, operation] of operations) {
          const result = await request(target, path, { ...operation, token });
          assert.equal(result.status, 403, `Unexpected browser access to ${docPath} (${operation.method ?? 'GET'}).`);
          denied++;
        }
      }
      // Rules must not prevent the server's independently IAM-controlled path.
      assert.equal((await request(target, `${root}/${docPath}`, { token: 'owner' })).status, 200);
    }
  }
  console.log(`Firestore privacy passed: ${denied} denied browser operations; authenticated positive control and privileged server path passed (${target.project}).`);
  return denied;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await verifyPrivacy();
}
