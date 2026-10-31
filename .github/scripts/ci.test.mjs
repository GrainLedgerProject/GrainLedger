import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { changedFiles, checkDocs, classify, gate, selectJobs } from './ci.mjs';

const all = { run_backend: true, run_web: true, run_container: true };
const none = { run_backend: false, run_web: false, run_container: false };
const script = fileURLToPath(new URL('./ci.mjs', import.meta.url));
const coverageScript = fileURLToPath(new URL('./coverage-summary.mjs', import.meta.url));

test('coverage Summary reports counts, empty metrics and partial results; invalid input fails', t => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'grainledger-coverage-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const input = path.join(root, 'coverage.json');
  const output = path.join(root, 'summary.md');
  const total = Object.fromEntries(['lines', 'branches', 'functions', 'statements']
    .map(metric => [metric, { total: 4, covered: 3 }]));
  total.branches = { total: 0, covered: 0 };
  writeFileSync(input, JSON.stringify({ total }));
  const run = outcome => spawnSync(process.execPath, [coverageScript, input], {
    encoding: 'utf8', env: { ...process.env, GITHUB_STEP_SUMMARY: output, COVERAGE_TEST_OUTCOME: outcome },
  });
  assert.equal(run('success').status, 0);
  assert.match(readFileSync(output, 'utf8'), /lines \| 75.00% \| 3\/4/);
  assert.match(readFileSync(output, 'utf8'), /branches \| N\/A \| 0\/0/);
  assert.equal(run('failure').status, 0);
  assert.match(readFileSync(output, 'utf8'), /coverage may be partial/);
  total.lines.covered = 5;
  writeFileSync(input, JSON.stringify({ total }));
  assert.notEqual(run('success').status, 0);
});

test('only known docs may skip builds; combined and unknown changes require coverage', () => {
  assert.deepEqual(classify(['README.md', 'docs/guide.md']), none);
  assert.deepEqual(classify(['backend/src/Api.cs', 'docs/guide.md']), { ...none, run_backend: true });
  assert.deepEqual(classify(['apps/web/src/App.tsx']), { ...none, run_web: true });
  assert.deepEqual(classify(['apps/mobile/package.json']), { ...all, run_backend: false });
  assert.deepEqual(classify(['Dockerfile', '.dockerignore']), { ...none, run_container: true });
  assert.deepEqual(classify(['global.json']), { ...all, run_web: false });
  for (const files of [[], undefined, ['.github/workflows/ci.yml'], ['scripts/new.sh'], ['docs/script.sh'], ['new.config']]) {
    assert.deepEqual(classify(files), all);
  }
  assert.deepEqual(classify(['backend/src/Api.cs', 'apps/web/src/App.tsx']), { ...all, run_container: false });
  assert.deepEqual(classify(Array.from({ length: 4000 }, (_, i) => `docs/${i}.md`).concat('backend/new.cs')),
    { ...none, run_backend: true });
});

test('gate rejects missing/invalid plans, failed/cancelled jobs and unexpected skips', () => {
  const needs = flags => ({
    changes: { result: 'success', outputs: flags },
    backend: { result: 'success' }, web: { result: 'success' }, container: { result: 'success' },
  });
  const full = Object.fromEntries(Object.entries(all).map(([key, value]) => [key, String(value)]));
  assert.doesNotThrow(() => gate(needs(full)));
  for (const job of ['backend', 'web', 'container']) {
    for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
      assert.throws(() => gate({ ...needs(full), [job]: { result } }));
    }
  }
  for (const flag of ['', undefined, 'TRUE', true]) {
    assert.throws(() => gate(needs({ ...full, run_backend: flag })));
  }
  for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
    assert.throws(() => gate({ ...needs(full), changes: { result, outputs: full } }));
  }
  const docs = needs(Object.fromEntries(Object.keys(none).map(key => [key, 'false'])));
  for (const job of ['backend', 'web', 'container']) docs[job].result = 'skipped';
  assert.doesNotThrow(() => gate(docs));
  docs.web.result = 'failure';
  assert.throws(() => gate(docs));
});

test('Git diff covers the entire PR, renamed/deleted files and unusual filenames', t => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'grainledger-ci-git-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init');
  git('config', 'user.name', 'CI test');
  git('config', 'user.email', 'ci@example.invalid');
  mkdirSync(path.join(root, 'backend'));
  mkdirSync(path.join(root, 'docs'));
  writeFileSync(path.join(root, 'backend', 'old.cs'), 'original');
  git('add', '.'); git('commit', '-m', 'base');
  const base = git('rev-parse', 'HEAD');
  git('mv', 'backend/old.cs', 'docs/moved.md');
  git('commit', '-m', 'rename');
  const docsBase = git('rev-parse', 'HEAD');
  const filename = 'docs/name with spaces $(never-run).md';
  writeFileSync(path.join(root, filename), 'docs');
  git('add', '.'); git('commit', '-m', 'docs');
  const files = changedFiles(base, git('rev-parse', 'HEAD'), root);
  assert.deepEqual(new Set(files), new Set(['backend/old.cs', 'docs/moved.md', filename]));
  assert.equal(classify(files).run_backend, true);
  assert.throws(() => changedFiles('--bad-ref', base, root));

  const eventPath = path.join(root, 'event.json');
  const outputPath = path.join(root, 'output');
  writeFileSync(eventPath, JSON.stringify({ pull_request: { base: { sha: docsBase, ref: 'dev' }, head: { sha: git('rev-parse', 'HEAD') } } }));
  for (const [enabled, expected] of [['true', 'false'], ['', 'false'], ['false', 'true'], ['invalid', 'true']]) {
    writeFileSync(outputPath, '');
    const result = spawnSync(process.execPath, [script, 'plan'], {
      cwd: root, encoding: 'utf8', env: { ...process.env, GITHUB_EVENT_NAME: 'pull_request',
        GITHUB_EVENT_PATH: eventPath, GITHUB_OUTPUT: outputPath, GITHUB_STEP_SUMMARY: '',
        CI_ENABLE_PATH_FILTERS: enabled },
    });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(readFileSync(outputPath, 'utf8'),
      `run_backend=${expected}\nrun_web=${expected}\nrun_container=${expected}\n`);
  }
});

test('documentation check validates local paths without making network requests', t => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'grainledger-ci-docs-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  writeFileSync(path.join(root, 'target file.md'), '# Target');
  writeFileSync(path.join(root, 'README.md'), '[ok](<target file.md>) [web](https://example.invalid) [anchor](#hello)');
  assert.doesNotThrow(() => checkDocs(['README.md', 'deleted.md'], root));
  writeFileSync(path.join(root, 'README.md'), '[bad](missing.md)');
  assert.throws(() => checkDocs(['README.md'], root), /missing local link/);
});

test('documentation check accepts only SDD renders backed by tracked sources', t => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'grainledger-ci-sdd-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(path.join(root, 'docs', 'SDD', 'diagrams'), { recursive: true });
  const sdd = path.join(root, 'docs', 'SDD', 'GrainLedger_SDD.md');
  writeFileSync(sdd, '![Class](diagrams/rendered/05_class.svg)');
  assert.throws(() => checkDocs(['docs/SDD/GrainLedger_SDD.md'], root), /missing local link/);
  writeFileSync(path.join(root, 'docs', 'SDD', 'diagrams', '05_class.mmd'), 'classDiagram');
  assert.doesNotThrow(() => checkDocs(['docs/SDD/GrainLedger_SDD.md'], root));
  writeFileSync(path.join(root, 'docs', 'SDD', 'other.md'), '![Class](diagrams/rendered/05_class.svg)');
  assert.throws(() => checkDocs(['docs/SDD/other.md'], root), /missing local link/);
});

test('actual plan/gate CLI defaults to full runs and fails closed', t => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'grainledger-ci-cli-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const eventPath = path.join(root, 'event.json');
  const output = path.join(root, 'output');
  writeFileSync(eventPath, '{}');
  const env = { ...process.env, GITHUB_EVENT_PATH: eventPath, GITHUB_OUTPUT: output,
    GITHUB_STEP_SUMMARY: '', CI_ENABLE_PATH_FILTERS: 'true', GITHUB_EVENT_NAME: 'workflow_dispatch' };
  const plan = spawnSync(process.execPath, [script, 'plan'], { cwd: root, env, encoding: 'utf8' });
  assert.equal(plan.status, 0, plan.stderr);
  assert.match(readFileSync(output, 'utf8'), /run_backend=true\nrun_web=true\nrun_container=true/);
  const fallback = spawnSync(process.execPath, [script, 'plan'], {
    cwd: root, env: { ...env, GITHUB_EVENT_NAME: 'pull_request' }, encoding: 'utf8',
  });
  assert.equal(fallback.status, 0, fallback.stderr);
  assert.match(fallback.stderr, /all build\/test jobs are required/);
  const invalidGate = spawnSync(process.execPath, [script, 'gate'], {
    env: { ...process.env, CI_NEEDS_JSON: '{}' }, encoding: 'utf8',
  });
  assert.equal(invalidGate.status, 1);
});


test('packaging changes and promotion require image validation; dev source edits avoid duplicate builds', () => {
  for (const file of ['backend/src/Api.csproj', 'Directory.Build.props', 'global.json']) {
    assert.deepEqual(classify([file]), { ...all, run_web: false });
  }
  for (const file of ['package-lock.json', 'apps/web/package.json', 'apps/web/vite.config.ts']) {
    assert.deepEqual(classify([file]), { ...all, run_backend: false });
  }
  assert.deepEqual(classify(['backend/src/Migrations/20261026.cs']), { ...none, run_backend: true });
  const docs = ['README.md'];
  assert.deepEqual(selectJobs(docs, 'pull_request', 'dev'), none);
  assert.deepEqual(selectJobs(docs, 'pull_request', 'dev', 'true'), none);
  for (const filters of ['false', 'TRUE', 'invalid']) {
    assert.deepEqual(selectJobs(docs, 'pull_request', 'dev', filters), all);
  }
  for (const base of ['main', undefined, 'release']) {
    assert.deepEqual(selectJobs(docs, 'pull_request', base), all);
  }
  for (const event of ['push', 'merge_group', 'workflow_dispatch']) {
    assert.deepEqual(selectJobs(docs, event, 'dev'), all);
  }
  assert.deepEqual(selectJobs(undefined, 'pull_request', 'dev'), all);
});
