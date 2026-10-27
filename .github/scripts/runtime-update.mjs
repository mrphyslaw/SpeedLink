import { readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const numeric = value => /^\d+\.\d+\.\d+$/.test(value);
const compare = (a, b) => { const x = a.split('.').map(Number), y = b.split('.').map(Number); return x[0] - y[0] || x[1] - y[1] || x[2] - y[2]; };
export function planUpdate(global, props, feed) {
  const sdk = global.sdk.version;
  const runtime = props.match(/<RuntimeFrameworkVersion>([^<]+)<\/RuntimeFrameworkVersion>/)?.[1];
  if (!numeric(sdk) || !numeric(runtime) || global.sdk.rollForward !== 'disable') throw Error('Invalid current runtime policy');
  const band = sdk.split('.');
  const eligible = (feed.releases ?? []).flatMap(release => {
    const targetRuntime = release.runtime?.version;
    return [release.sdk, ...(release.sdks ?? [])].filter(Boolean).map(item => ({ sdk: item.version, runtime: targetRuntime }));
  }).filter(item => numeric(item.sdk ?? '') && numeric(item.runtime ?? '') &&
    item.sdk.startsWith(`${band[0]}.${band[1]}.`) && Math.floor(Number(item.sdk.split('.')[2]) / 100) === Math.floor(Number(band[2]) / 100) &&
    item.runtime.startsWith(runtime.split('.').slice(0, 2).join('.') + '.') && compare(item.sdk, sdk) >= 0 && compare(item.runtime, runtime) >= 0)
    .sort((a, b) => compare(b.sdk, a.sdk) || compare(b.runtime, a.runtime));
  const next = eligible[0];
  if (!next || (next.sdk === sdk && next.runtime === runtime)) return null;
  return { oldSdk: sdk, oldRuntime: runtime, ...next, branch: `maintenance/dotnet-${next.sdk}-${next.runtime}` };
}

async function api(path, method = 'GET', body) {
  if (process.env.GITHUB_REPOSITORY !== 'mrphyslaw/SpeedLink') throw Error('Unexpected repository');
  const response = await fetch(`https://api.github.com/repos/${process.env.GITHUB_REPOSITORY}${path}`, { method, headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: 'application/vnd.github+json', 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined, redirect: 'error' });
  if (response.status === 404 && method === 'GET') return null;
  if (!response.ok) throw Error(`Maintenance API failed (${response.status})`);
  return response.status === 204 ? null : response.json();
}

async function main() {
  if (process.argv[2] === 'prepare') {
    const global = JSON.parse(readFileSync('dotnet/global.json', 'utf8'));
    const props = readFileSync('dotnet/Directory.Build.props', 'utf8');
    const channel = global.sdk.version.split('.').slice(0, 2).join('.');
    if (!/^\d+\.\d+$/.test(channel)) throw Error('Invalid channel');
    const response = await fetch(`https://builds.dotnet.microsoft.com/dotnet/release-metadata/${channel}/releases.json`, { redirect: 'error' });
    if (!response.ok) throw Error('Official runtime metadata is unavailable');
    const plan = planUpdate(global, props, await response.json());
    if (!plan) { appendFileSync(process.env.GITHUB_OUTPUT, 'changed=false\n'); console.log('No new runtime patch required.'); return; }
    if (await api(`/git/ref/heads/${plan.branch}`)) {
      const pulls = await api(`/pulls?state=open&head=${encodeURIComponent('mrphyslaw:' + plan.branch)}`);
      if (!pulls.length) throw Error('An update branch exists without an open PR; inspect it before rerunning. Existing work was preserved.');
      appendFileSync(process.env.GITHUB_OUTPUT, 'changed=false\n'); console.log('Existing runtime PR preserved for review.'); return;
    }
    global.sdk.version = plan.sdk;
    writeFileSync('dotnet/global.json', JSON.stringify(global, null, 2) + '\n');
    writeFileSync('dotnet/Directory.Build.props', props.replace(`<RuntimeFrameworkVersion>${plan.oldRuntime}</RuntimeFrameworkVersion>`, `<RuntimeFrameworkVersion>${plan.runtime}</RuntimeFrameworkVersion>`));
    writeFileSync('runtime-update.json', JSON.stringify(plan));
    appendFileSync(process.env.GITHUB_OUTPUT, `changed=true\nbranch=${plan.branch}\n`);
  } else if (process.argv[2] === 'pull') {
    const plan = JSON.parse(readFileSync('runtime-update.json', 'utf8'));
    if (!/^maintenance\/dotnet-\d+\.\d+\.\d+-\d+\.\d+\.\d+$/.test(plan.branch)) throw Error('Invalid maintenance branch');
    execFileSync('git', ['config', 'user.name', 'github-actions[bot]']);
    execFileSync('git', ['config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com']);
    execFileSync('git', ['switch', '-c', plan.branch]);
    // Explicit file inventory: never stage reports, credentials or generated executable files.
    execFileSync('git', ['add', 'dotnet/global.json', 'dotnet/Directory.Build.props', ...execFileSync('git', ['ls-files', 'dotnet/**/packages.lock.json'], { encoding: 'utf8' }).trim().split(/\r?\n/).filter(Boolean)]);
    execFileSync('git', ['commit', '-m', `Update bundled runtime to ${plan.runtime} and SDK to ${plan.sdk}`]);
    const sha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    // Push without a credential helper or token-bearing URL; the short-lived extra header is never printed or persisted.
    const auth = Buffer.from(`x-access-token:${process.env.GH_TOKEN}`).toString('base64');
    execFileSync('git', ['push', 'origin', `HEAD:refs/heads/${plan.branch}`], { stdio: 'pipe', env: { ...process.env, GIT_CONFIG_COUNT: '1', GIT_CONFIG_KEY_0: 'http.https://github.com/.extraheader', GIT_CONFIG_VALUE_0: `AUTHORIZATION: basic ${auth}` } });
    await api('/pulls', 'POST', { head: plan.branch, base: 'main', title: `Update .NET SDK ${plan.sdk} and bundled runtime ${plan.runtime}`, body: `Updates the pinned SDK and bundled runtime together using Microsoft's published metadata; refreshes dependency locks.\n\nOffline unit and mandatory ROM-save integrity checks passed before opening this PR. The full desktop/release checks are dispatched separately. Review version-dependent notices and documentation before merging. No automatic merge or release is enabled.\n\nSource commit: ${sha}` });
    // GITHUB_TOKEN-created PRs do not trigger pull_request workflows. Dispatch checks explicitly on this branch.
    await api('/actions/workflows/checks-dotnet.yml/dispatches', 'POST', { ref: plan.branch });
    console.log('Runtime maintenance PR opened; full checks dispatched. Human review is required.');
  } else throw Error('Unknown maintenance mode');
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
