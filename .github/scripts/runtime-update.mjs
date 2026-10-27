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

export async function publishProposal(plan, call, runUrl, baseCommit) {
  if (![plan.sdk, plan.runtime].every(numeric) || !/^[a-f0-9]{40}$/.test(baseCommit) || !/^https:\/\/github\.com\/mrphyslaw\/SpeedLink\/actions\/runs\/\d+$/.test(runUrl)) throw Error('Invalid update proposal identity');
  const marker = `<!-- runtime-update:${plan.sdk}:${plan.runtime} -->`;
  const issues = await call('/issues?state=all&per_page=100');
  const existing = issues.find(issue => !issue.pull_request && issue.user?.login === 'github-actions[bot]' && issue.body?.includes(marker));
  if (existing?.state === 'closed') return; // Preserve a maintainer's decision to decline an update.
  const body = `${marker}\nA tested runtime update is ready for review.\n\n- SDK: ${plan.oldSdk} → ${plan.sdk}\n- Bundled runtime: ${plan.oldRuntime} → ${plan.runtime}\n- Base source commit: ${baseCommit}\n\n[Download the runtime-update artifact and view the checks](${runUrl}). It contains the coupled SDK/runtime patch, refreshed dependency locks and test evidence. Artifacts expire after 14 days; an open proposal is refreshed by the next successful weekly run.\n\nReview the patch, runtime notices and documentation. Apply runtime-update.patch on a branch based on the source commit above, then open a PR. The full desktop and standalone checks must pass before merge. Build a new release to deliver the runtime patch to users.\n\nNo branches, reviews, merges or releases were changed automatically.`;
  await call(existing ? `/issues/${existing.number}` : '/issues', existing ? 'PATCH' : 'POST', { title: `Runtime update ready: SDK ${plan.sdk} / .NET ${plan.runtime}`, body });
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
    global.sdk.version = plan.sdk;
    writeFileSync('dotnet/global.json', JSON.stringify(global, null, 2) + '\n');
    writeFileSync('dotnet/Directory.Build.props', props.replace(`<RuntimeFrameworkVersion>${plan.oldRuntime}</RuntimeFrameworkVersion>`, `<RuntimeFrameworkVersion>${plan.runtime}</RuntimeFrameworkVersion>`));
    writeFileSync('runtime-update.json', JSON.stringify(plan));
    appendFileSync(process.env.GITHUB_OUTPUT, `changed=true\n`);
  } else if (process.argv[2] === 'patch') {
    const files = ['dotnet/global.json', 'dotnet/Directory.Build.props', ...execFileSync('git', ['ls-files', 'dotnet/**/packages.lock.json'], { encoding: 'utf8' }).trim().split(/\r?\n/).filter(Boolean)];
    writeFileSync('runtime-update.patch', execFileSync('git', ['diff', '--binary', '--', ...files]));
  } else if (process.argv[2] === 'proposal') {
    const plan = JSON.parse(readFileSync('runtime-update.json', 'utf8'));
    const base = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    await publishProposal(plan, api, `https://github.com/${process.env.GITHUB_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}`, base);
    console.log('Tested runtime patch proposed for review. No branches, reviews, merges or releases were changed.');  } else throw Error('Unknown maintenance mode');
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
