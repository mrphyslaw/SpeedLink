import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export const keepCount = 5;

export function planRetention(releases) {
  const ids = new Set();
  const published = releases.filter(release => {
    if (typeof release.draft !== 'boolean' || !Number.isSafeInteger(release.id) || release.id <= 0 || ids.has(release.id)) {
      throw new Error('Invalid or duplicate release metadata; no cleanup is safe.');
    }
    ids.add(release.id);
    if (release.draft) return false;
    if (typeof release.tag_name !== 'string' || !release.tag_name || !release.published_at || !Number.isFinite(Date.parse(release.published_at))) {
      throw new Error('A published release has incomplete metadata; refusing cleanup.');
    }
    return true;
  }).sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at) || b.id - a.id);
  return { keep: published.slice(0, keepCount), remove: published.slice(keepCount) };
}

export function createClient(repository, token, fetcher = fetch) {
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository ?? '') || !token) {
    throw new Error('A repository and GitHub token are required.');
  }
  const base = `https://api.github.com/repos/${repository}`;
  const request = async (path, method = 'GET') => {
    const response = await fetcher(base + path, {
      method,
      redirect: 'error',
      signal: AbortSignal.timeout(30_000),
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'SpeedLink-release-retention',
      },
    });
    if (method === 'DELETE' && response.status === 204) return;
    if (!response.ok) throw new Error(`GitHub ${method} ${path}: HTTP ${response.status}.`);
    return response.json();
  };
  return {
    async listReleases() {
      const releases = [];
      for (let page = 1; ; page++) {
        const rows = await request(`/releases?per_page=100&page=${page}`);
        if (!Array.isArray(rows)) throw new Error('Invalid release list.');
        releases.push(...rows);
        if (rows.length < 100) return releases;
      }
    },
    getRelease(id) {
      if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid release ID.');
      return request(`/releases/${id}`);
    },
    deleteRelease(id) {
      if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid release ID.');
      // This is the only write endpoint. Never delete or update a Git ref.
      return request(`/releases/${id}`, 'DELETE');
    },
    async tagRefs() {
      const refs = await request('/git/matching-refs/tags/');
      if (!Array.isArray(refs) || refs.some(ref => typeof ref.ref !== 'string' || !ref.ref.startsWith('refs/tags/') || !ref.object?.sha)) {
        throw new Error('Invalid tag snapshot.');
      }
      return new Map(refs.map(ref => [ref.ref, ref.object.sha]));
    },
  };
}

export async function retainReleases(client, { dryRun = true, log = console.log } = {}) {
  if (typeof dryRun !== 'boolean') throw new Error('dryRun must be a boolean.');
  const tagsBefore = await client.tagRefs();
  let plan = planRetention(await client.listReleases());
  const planned = plan.remove.map(release => release.tag_name);
  const deleted = [];
  if (dryRun) {
    log(JSON.stringify({ preview: true, keep: plan.keep.map(r => r.tag_name), wouldRemove: planned }));
  } else {
    let changes = 0;
    while (plan.remove.length) {
      if (++changes > 1000) throw new Error('Cleanup limit reached; inspect remaining releases.');
      const candidate = plan.remove.at(-1);
      const current = await client.getRelease(candidate.id);
      if (!current.draft && current.id === candidate.id && current.tag_name === candidate.tag_name && current.published_at === candidate.published_at) {
        await client.deleteRelease(candidate.id);
        deleted.push(candidate.tag_name);
        log(JSON.stringify({ deletedRelease: candidate.tag_name, releaseId: candidate.id }));
      }
      // Recompute after each operation: newly published, unpublished or edited
      // releases must not cause the remaining published count to fall below five.
      plan = planRetention(await client.listReleases());
    }
  }
  const tagsAfter = await client.tagRefs();
  for (const [name, sha] of tagsBefore) {
    if (tagsAfter.get(name) !== sha) throw new Error('A pre-existing Git tag changed during cleanup.');
  }
  return { dryRun, kept: plan.keep.map(r => r.tag_name), wouldRemove: dryRun ? planned : [], deleted, tagsPreserved: true };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const value = process.env.DRY_RUN ?? 'true';
    if (!['true', 'false'].includes(value)) throw new Error('DRY_RUN must be true or false.');
    const result = await retainReleases(createClient(process.env.GITHUB_REPOSITORY, process.env.GH_TOKEN), { dryRun: value === 'true' });
    console.log(JSON.stringify(result));
    if (process.env.GITHUB_STEP_SUMMARY) {
      appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## Release retention\n\nMode: ${result.dryRun ? 'preview' : 'cleanup'}. Kept ${result.kept.length} published releases; ${result.deleted.length} removed; ${result.wouldRemove.length} would be removed.\n\nExisting tags preserved. Drafts are excluded.\n`);
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
