import test from 'node:test';
import assert from 'node:assert/strict';
import { planUpdate, publishProposal } from './runtime-update.mjs';
const global = { sdk: { version: '10.0.403', rollForward: 'disable' } };
const props = '<RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion>';
const release = (sdk, runtime) => ({ sdk: { version: sdk }, runtime: { version: runtime } });
test('couples SDK and runtime from the same official release', () => {
  const plan = planUpdate(global, props, { releases: [release('10.0.404', '10.0.15'), release('10.0.403', '10.0.14')] });
  assert.equal(plan.sdk, '10.0.404'); assert.equal(plan.runtime, '10.0.15');
});
for (const state of ['new', 'open', 'closed', 'untrusted']) test(`runtime proposal handles ${state} review state without repository write permissions`, async () => {
  const marker = '<!-- runtime-update:10.0.404:10.0.15 -->';
  const issues = state === 'new' ? [] : [{ number: 9, state: state === 'closed' ? 'closed' : 'open', user: { login: state === 'untrusted' ? 'someone' : 'github-actions[bot]' }, body: marker }];
  const calls = [];
  const api = async (path, method = 'GET', body) => { calls.push({ path, method, body }); if (method === 'GET') return issues; return {}; };
  await publishProposal({ sdk: '10.0.404', runtime: '10.0.15', oldSdk: '10.0.403', oldRuntime: '10.0.14' }, api, 'https://github.com/mrphyslaw/SpeedLink/actions/runs/123', 'a'.repeat(40));
  assert.ok(calls.every(call => call.path.startsWith('/issues')));
  if (state === 'closed') assert.equal(calls.length, 1);
  else { assert.equal(calls[1].method, state === 'open' ? 'PATCH' : 'POST'); assert.ok(calls[1].body.body.includes('No branches, reviews, merges or releases')); }
});
test('ignores previews, major upgrades, feature bands and downgrades', () => {
  assert.equal(planUpdate(global, props, { releases: [release('11.0.100', '11.0.0'), release('10.0.500', '10.0.15'), release('10.0.404-preview', '10.0.15'), release('10.0.402', '10.0.13'), release('10.0.404', '10.0.13')] }), null);
});
test('current or absent metadata makes no changes', () => {
  assert.equal(planUpdate(global, props, { releases: [release('10.0.403', '10.0.14')] }), null);
  assert.equal(planUpdate(global, props, {}), null);
});
test('alternate SDK list and unordered metadata are handled', () => {
  const item = { runtime: { version: '10.0.16' }, sdks: [{ version: '10.0.405' }] };
  assert.equal(planUpdate(global, props, { releases: [release('10.0.404', '10.0.15'), item] }).sdk, '10.0.405');
});
test('requires the pinned SDK policy and concrete version fields', () => {
  assert.throws(() => planUpdate({ sdk: { version: '10.0.403', rollForward: 'latestPatch' } }, props, {}));
  assert.throws(() => planUpdate(global, '', {}));
});
