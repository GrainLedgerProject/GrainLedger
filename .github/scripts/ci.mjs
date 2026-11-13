import { execFileSync } from 'node:child_process';
import { appendFileSync, existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const jobs = ['backend', 'web', 'container'];
const full = () => Object.fromEntries(jobs.map(job => [`run_${job}`, true]));

export function classify(files) {
  // An empty/unknown diff must never become a successful docs-only run.
  if (!files?.length) return full();
  const flags = Object.fromEntries(jobs.map(job => [`run_${job}`, false]));
  for (const file of files) {
    if (/^(README\.md|AGENTS\.md|docs\/.*\.md)$/.test(file)) continue;
    if (file.startsWith('backend/') ||
        /^(global\.json|GrainLedger\.slnx|NuGet\.[Cc]onfig|nuget\.config|Directory\.(Build|Packages)\.(props|targets))$/.test(file)) {
      flags.run_backend = true;
      if (!file.endsWith('.cs')) flags.run_container = true;
    } else if (file.startsWith('apps/web/') ||
               ['package.json', 'package-lock.json', 'apps/mobile/package.json'].includes(file)) {
      flags.run_web = true;
      if (!file.startsWith('apps/web/src/')) flags.run_container = true;
    } else if (['Dockerfile', '.dockerignore'].includes(file)) {
      flags.run_container = true;
    } else {
      return full();
    }
  }
  return flags;
}

export function selectJobs(files, eventName, baseRef, filters = '') {
  // Unknown configuration fails safe; only an unset variable or true opts in.
  if (eventName !== 'pull_request' || baseRef !== 'dev' ||
      !['', 'true'].includes(filters)) return full();
  return classify(files);
}

export function changedFiles(base, head, cwd = process.cwd()) {
  if (![base, head].every(sha => /^[a-f0-9]{40}$/i.test(sha ?? ''))) {
    throw new Error('Expected immutable GitHub commit SHAs.');
  }
  const git = args => execFileSync('git', args, {
    cwd, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'],
  });
  const mergeBase = git(['merge-base', base, head]).trim();
  // Disabling rename detection reports both the old and new paths; NUL handles
  // spaces/newlines without interpreting any PR-supplied filename as shell code.
  return git(['diff', '--name-only', '--no-renames', '-z', mergeBase, head, '--'])
    .split('\0').filter(Boolean);
}

export function checkDocs(files, root = process.cwd()) {
  const errors = [];
  for (const file of files.filter(file => file.endsWith('.md'))) {
    const source = path.resolve(root, file);
    if (!existsSync(source)) continue; // Deleted document.
    const markdown = readFileSync(source, 'utf8').replace(/```[^]*?```/g, '');
    for (const match of markdown.matchAll(/!?\[[^\]]*\]\((<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)/g)) {
      const target = match[1].replace(/^<|>$/g, '');
      if (/^(?:[a-z][a-z0-9+.-]*:|\/\/|#)/i.test(target)) continue;
      let relative;
      try {
        relative = decodeURIComponent(target.split(/[?#]/)[0]);
      } catch {
        errors.push(`${file}: malformed local link ${target}`);
        continue;
      }
      if (!relative) continue;
      const destination = relative.startsWith('/')
        ? path.resolve(root, `.${relative}`)
        : path.resolve(path.dirname(source), relative);
      if (!existsSync(destination)) errors.push(`${file}: missing local link ${target}`);
    }
  }
  if (errors.length) throw new Error(errors.join('\n'));
}

export function gate(needs) {
  if (needs?.changes?.result !== 'success') throw new Error('CI planning/checks did not succeed.');
  for (const job of jobs) {
    const required = needs.changes.outputs?.[`run_${job}`];
    const result = needs[job]?.result;
    if (!['true', 'false'].includes(required)) throw new Error(`Invalid requirement for ${job}.`);
    const accepted = required === 'true' ? ['success'] : ['success', 'skipped'];
    if (!accepted.includes(result)) throw new Error(`${job}: required=${required}, result=${result}.`);
  }
}

function plan() {
  const event = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  let files;
  if (process.env.GITHUB_EVENT_NAME === 'pull_request') {
    try {
      files = changedFiles(event.pull_request?.base?.sha, event.pull_request?.head?.sha);
    } catch {
      console.warn('PR diff unavailable; all build/test jobs are required.');
    }
  }
  checkDocs(files ?? ['README.md', 'docs/ci-plan.md', 'docs/ci-guide.md']);
  const flags = selectJobs(files, process.env.GITHUB_EVENT_NAME,
    event.pull_request?.base?.ref, process.env.CI_ENABLE_PATH_FILTERS ?? '');
  const output = Object.entries(flags).map(([key, value]) => `${key}=${value}`).join('\n');
  appendFileSync(process.env.GITHUB_OUTPUT, `${output}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY,
      `### Required CI jobs\n\n${Object.entries(flags).map(([key, value]) => `- ${key}: ${value}`).join('\n')}\n`);
  }
  console.log(output);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    if (process.argv[2] === 'plan') plan();
    else if (process.argv[2] === 'gate') {
      gate(JSON.parse(process.env.CI_NEEDS_JSON));
      console.log('All required CI jobs succeeded.');
    } else throw new Error('Usage: node .github/scripts/ci.mjs plan|gate');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
