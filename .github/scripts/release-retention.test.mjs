import test from 'node:test';
import assert from 'node:assert/strict';
import { createClient, planRetention, retainReleases } from './release-retention.mjs';

const release = (id, options = {}) => ({ id, draft: false, prerelease: true, tag_name: `v0.5.4-alpha.${id}`, published_at: new Date(Date.UTC(2026, 9, id)).toISOString(), ...options });
const fake = releases => {
  const state = { releases: structuredClone(releases), deleted: [], tags: new Map(releases.map(r => [`refs/tags/${r.tag_name}`, `sha-${r.id}`])) };
  return Object.assign(state, {
    async listReleases() { return structuredClone(this.releases); },
    async getRelease(id) { return structuredClone(this.releases.find(r => r.id === id)); },
    async deleteRelease(id) { this.deleted.push(id); this.releases = this.releases.filter(r => r.id !== id); },
    async tagRefs() { return new Map(this.tags); },
  });
};
const silent = { dryRun: false, log() {} };

test('orders by publication time rather than API order, tag text or creation time', () => {
  const rows = Array.from({ length: 10 }, (_, i) => release(i + 1, { created_at: new Date(Date.UTC(2026, 9, 20 - i)).toISOString() })).reverse();
  [rows[0], rows[8]] = [rows[8], rows[0]];
  const plan = planRetention(rows);
  assert.deepEqual(plan.keep.map(r => r.id), [10, 9, 8, 7, 6]);
  assert.deepEqual(plan.remove.map(r => r.id), [5, 4, 3, 2, 1]);
});

test('counts normal releases and prereleases together, excludes drafts', () => {
  const rows = [release(99, { draft: true, published_at: null }), ...Array.from({ length: 6 }, (_, i) => release(i + 1, { prerelease: i % 2 === 0 }))];
  assert.deepEqual(planRetention(rows).remove.map(r => r.id), [1]);
});

test('ties use release ID deterministically', () => {
  const rows = Array.from({ length: 7 }, (_, i) => release(i + 1, { published_at: '2026-10-22T12:00:00Z' }));
  assert.deepEqual(planRetention(rows).keep.map(r => r.id), [7, 6, 5, 4, 3]);
});

test('malformed timestamps and duplicate IDs fail before deletion', async () => {
  for (const rows of [[release(1), release(1)], [release(1, { published_at: null })], [release(1, { published_at: 'bad' })], [release(-1)], [release(1, { draft: undefined })]]) {
    const client = fake(rows);
    await assert.rejects(retainReleases(client, silent));
    assert.deepEqual(client.deleted, []);
  }
});

test('zero through five published releases do not delete anything', async () => {
  for (let count = 0; count <= 5; count++) {
    const client = fake(Array.from({ length: count }, (_, i) => release(i + 1)));
    const result = await retainReleases(client, silent);
    assert.equal(result.kept.length, count);
    assert.deepEqual(client.deleted, []);
  }
});

test('preview defaults to read-only even with expired releases', async () => {
  const client = fake(Array.from({ length: 9 }, (_, i) => release(i + 1)));
  const result = await retainReleases(client, { log() {} });
  assert.equal(result.dryRun, true);
  assert.equal(result.wouldRemove.length, 4);
  assert.deepEqual(client.deleted, []);
  await assert.rejects(retainReleases(client, { dryRun: 'false' }));
});

test('deletes only older release entries, preserving drafts and all tag refs', async () => {
  const client = fake([release(99, { draft: true, published_at: null }), ...Array.from({ length: 9 }, (_, i) => release(i + 1))]);
  const tags = new Map(client.tags);
  const result = await retainReleases(client, silent);
  assert.deepEqual(result.kept, [9, 8, 7, 6, 5].map(id => release(id).tag_name));
  assert.deepEqual(client.deleted, [1, 2, 3, 4]);
  assert(client.releases.some(r => r.draft));
  assert.deepEqual(client.tags, tags);
});

test('rechecks the published count after another release becomes a draft', async () => {
  const client = fake(Array.from({ length: 7 }, (_, i) => release(i + 1)));
  const remove = client.deleteRelease;
  client.deleteRelease = async function(id) {
    await remove.call(this, id);
    this.releases.find(r => r.id === 7).draft = true;
  };
  await retainReleases(client, silent);
  assert.deepEqual(client.deleted, [1]);
  assert.equal(client.releases.filter(r => !r.draft).length, 5);
});

test('does not delete a candidate changed to a draft before its DELETE', async () => {
  const client = fake(Array.from({ length: 6 }, (_, i) => release(i + 1)));
  client.getRelease = async function(id) { const row = this.releases.find(r => r.id === id); row.draft = true; return structuredClone(row); };
  await retainReleases(client, silent);
  assert.deepEqual(client.deleted, []);
});

test('newly published releases are included when cleanup recomputes the plan', async () => {
  const client = fake(Array.from({ length: 6 }, (_, i) => release(i + 1)));
  const remove = client.deleteRelease;
  client.deleteRelease = async function(id) {
    await remove.call(this, id);
    if (id === 1) this.releases.push(release(7));
  };
  const result = await retainReleases(client, silent);
  assert.deepEqual(client.deleted, [1, 2]);
  assert.equal(result.kept[0], release(7).tag_name);
});

test('tag verification allows newly added tags but detects removal or movement', async () => {
  for (const changed of [null, 'different-sha']) {
    const client = fake(Array.from({ length: 6 }, (_, i) => release(i + 1)));
    const remove = client.deleteRelease;
    client.deleteRelease = async function(id) { await remove.call(this, id); if (changed === null) this.tags.delete('refs/tags/v0.5.4-alpha.1'); else this.tags.set('refs/tags/v0.5.4-alpha.1', changed); };
    await assert.rejects(retainReleases(client, silent), /tag changed/);
  }
  const client = fake([release(1)]);
  let reads = 0;
  client.tagRefs = async () => { if (++reads === 2) client.tags.set('refs/tags/new', 'new-sha'); return new Map(client.tags); };
  assert.equal((await retainReleases(client, silent)).tagsPreserved, true);
});

test('HTTP client paginates and scopes every write to a numeric release endpoint', async () => {
  const calls = [];
  const client = createClient('owner/repo', 'test-token', async (url, options) => {
    calls.push({ url, ...options });
    if (options.method === 'DELETE') return new Response(null, { status: 204 });
    return Response.json(url.endsWith('page=1') ? Array.from({ length: 100 }, (_, i) => release(i + 1)) : [release(101)]);
  });
  assert.equal((await client.listReleases()).length, 101);
  await client.deleteRelease(123);
  for (const bad of ['refs/tags/x', '../git/refs', -1, 0, NaN, 1.1]) assert.throws(() => client.deleteRelease(bad));
  const writes = calls.filter(r => r.method !== 'GET');
  assert.deepEqual(writes.map(r => [r.method, r.url]), [['DELETE', 'https://api.github.com/repos/owner/repo/releases/123']]);
  assert.equal(writes[0].redirect, 'error');
});

test('API errors fail closed without retrying DELETE or leaking response bodies', async () => {
  let calls = 0;
  const client = createClient('owner/repo', 'test-token', async () => { calls++; return new Response('private response', { status: 403 }); });
  await assert.rejects(client.deleteRelease(123), error => /HTTP 403/.test(error.message) && !error.message.includes('private response'));
  assert.equal(calls, 1);
  assert.throws(() => createClient('../other/repo', 'token'));
  assert.throws(() => createClient('owner/repo', ''));
});

test('an uncertain deletion stops the run without retrying or deleting another release', async () => {
  const client = fake(Array.from({ length: 8 }, (_, i) => release(i + 1)));
  let attempts = 0;
  client.deleteRelease = async () => { attempts++; throw new Error('Request timed out.'); };
  await assert.rejects(retainReleases(client, silent), /timed out/);
  assert.equal(attempts, 1);
  assert.equal(client.releases.length, 8);
});
