// scripts/gen-latest-json.test.mjs - unit tests for the Tauri updater latest.json
// generator (ticket 44, architecture-recovery). Run with: node --test
// scripts/gen-latest-json.test.mjs
//
// Schema-pinning tests per issue 44 AC: 'scripts/gen-latest-json.mjs exists and
// schema is correct (pinned by unit tests)'. Tests are pure-filesystem, no
// network, no real signing key (uses throwaway fixture bytes), and isolated per
// test via mkdtemp under os.tmpdir().

import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { buildManifest, writeManifest, validateSemver, validateRfc3339 } from './gen-latest-json.mjs';

function withFixture(fn) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gen-latest-json-'));
  try {
    return fn(dir);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function seedAssets(assetsDir, version, arch, signature) {
  fs.mkdirSync(assetsDir, { recursive: true });
  const zip = path.join(assetsDir, `Env Manager_${version}_${arch}.msi.zip`);
  const sig = `${zip}.sig`;
  fs.writeFileSync(zip, 'fake-zip-bytes');
  fs.writeFileSync(sig, signature + '\n');
  return { zip, sig };
}

const SIG = 'dW50cnVzdGVkIGNvbW1lbnQ6IG1pbmlzaWduIHNpZ25hdHVyZTogZGVmYXVsdAo=';
const DATE = '2026-09-09T12:34:56Z';

test('validateSemver: true for plain, prerelease, build meta', () => {
  assert.equal(validateSemver('0.9.30'), true);
  assert.equal(validateSemver('1.0.0-rc.1'), true);
  assert.equal(validateSemver('1.0.0+build.7'), true);
  assert.equal(validateSemver('1.0.0-rc.1+build.7'), true);
});

test('validateSemver: false for empty, non-semver, non-string', () => {
  assert.equal(validateSemver(''), false);
  assert.equal(validateSemver('1.0'), false);
  assert.equal(validateSemver('v1.0.0'), false);
  assert.equal(validateSemver('not-a-version'), false);
  assert.equal(validateSemver(undefined), false);
  assert.equal(validateSemver(null), false);
});

test('validateRfc3339: true for Z and explicit-offset timestamps', () => {
  assert.equal(validateRfc3339('2026-09-09T12:34:56Z'), true);
  assert.equal(validateRfc3339('2026-09-09T12:34:56.789Z'), true);
  assert.equal(validateRfc3339('2026-09-09T12:34:56+08:00'), true);
  assert.equal(validateRfc3339('2026-09-09T12:34:56-05:00'), true);
});

test('validateRfc3339: false for space, missing time, bad offset', () => {
  assert.equal(validateRfc3339('2026-09-09'), false);
  assert.equal(validateRfc3339('2026-09-09 12:34:56Z'), false);
  assert.equal(validateRfc3339('2026-09-09T12:34:56'), false);
  assert.equal(validateRfc3339('2026-09-09T12:34:56+0800'), false);
});

test('buildManifest: emits the full schema (version/notes/pub_date/platforms.windows-x86_64.{url,signature})', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({
      version: '0.12.0',
      arch: 'x64',
      'assets-dir': assets,
      'pub-date': DATE,
      'url-base': 'https://example.test/dl/',
      'notes-text': 'release notes body',
    });
    assert.deepEqual(Object.keys(m).sort(), ['notes', 'platforms', 'pub_date', 'version']);
    assert.equal(m.version, '0.12.0');
    assert.equal(m.pub_date, DATE);
    assert.equal(m.notes, 'release notes body');
    const p = m.platforms['windows-x86_64'];
    assert.equal(typeof p, 'object');
    assert.deepEqual(Object.keys(p).sort(), ['signature', 'url']);
    assert.equal(p.url, 'https://example.test/dl/Env Manager_0.12.0_x64.msi.zip');
    assert.equal(p.signature, SIG);
  });
});

test('buildManifest: default url base is the GitHub releases/download path', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE });
    assert.equal(m.platforms['windows-x86_64'].url, 'https://github.com/Xxx91n/env-manager/releases/download/v0.12.0/Env Manager_0.12.0_x64.msi.zip');
  });
});

test('buildManifest: url-base without trailing slash normalizes to single slash', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE, 'url-base': 'https://cdn.test/no-slash' });
    assert.equal(m.platforms['windows-x86_64'].url, 'https://cdn.test/no-slash/Env Manager_0.12.0_x64.msi.zip');
  });
});

test('buildManifest: arch mapping x86/arm64 -> windows-i686/windows-aarch64', () => {
  withFixture((root) => {
    for (const [arch, triple] of [['x86', 'windows-i686'], ['arm64', 'windows-aarch64']]) {
      const assets = path.join(root, `msi-${arch}`);
      seedAssets(assets, '0.12.0', arch, SIG);
      const m = buildManifest({ version: '0.12.0', arch, 'assets-dir': assets, 'pub-date': DATE });
      assert.equal(m.platforms[triple].url.endsWith(`Env Manager_0.12.0_${arch}.msi.zip`), true);
    }
  });
});

test('buildManifest: signature file content is read verbatim with trailing LF/CRLF trimmed', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    fs.mkdirSync(assets, { recursive: true });
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip'), 'x');
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip.sig'), SIG + '\r\n');
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE });
    assert.equal(m.platforms['windows-x86_64'].signature, SIG);
  });
});

test('buildManifest: multi-line signature file rejected (single base64 line only)', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    fs.mkdirSync(assets, { recursive: true });
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip'), 'x');
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip.sig'), SIG + '\nsecond line\n');
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE }), /must be a single base64 line/);
  });
});

test('buildManifest: empty signature file rejected', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    fs.mkdirSync(assets, { recursive: true });
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip'), 'x');
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip.sig'), '');
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE }), /must be a single base64 line/);
  });
});

test('buildManifest: missing .msi.zip rejected with actionable hint', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    fs.mkdirSync(assets, { recursive: true });
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip.sig'), SIG + '\n');
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE }), /missing updater bundle/);
  });
});

test('buildManifest: missing .sig rejected with actionable hint', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    fs.mkdirSync(assets, { recursive: true });
    fs.writeFileSync(path.join(assets, 'Env Manager_0.12.0_x64.msi.zip'), 'x');
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE }), /missing updater signature/);
  });
});

test('buildManifest: missing assets-dir rejected', () => {
  withFixture((root) => {
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': path.join(root, 'nope'), 'pub-date': DATE }), /--assets-dir does not exist/);
  });
});

test('buildManifest: invalid version rejected before any fs access', () => {
  withFixture((root) => {
    assert.throws(() => buildManifest({ version: 'v0.12.0', arch: 'x64', 'assets-dir': root, 'pub-date': DATE }), /must be a semver string/);
  });
});

test('buildManifest: invalid pub-date rejected', () => {
  withFixture((root) => {
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': root, 'pub-date': 'yesterday' }), /must be an RFC3339 timestamp/);
  });
});

test('buildManifest: unknown arch rejected', () => {
  withFixture((root) => {
    assert.throws(() => buildManifest({ version: '0.12.0', arch: 'ppc64', 'assets-dir': root, 'pub-date': DATE }), /--arch must be one of/);
  });
});

test('buildManifest: notes-file takes precedence over notes-text', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const notesFile = path.join(root, 'NOTES.md');
    fs.writeFileSync(notesFile, 'from file');
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE, 'notes-file': notesFile, 'notes-text': 'ignored' });
    assert.equal(m.notes, 'from file');
  });
});

test('buildManifest: default notes is the versioned product label', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE });
    assert.equal(m.notes, 'Env Manager 0.12.0');
  });
});

test('buildManifest: pub_date defaults to current UTC second-precision when omitted', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets });
    assert.equal(validateRfc3339(m.pub_date), true);
    assert.equal(m.pub_date.endsWith('Z'), true);
  });
});

test('writeManifest: emits a trailing-newline JSON document with stable key order', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE, 'notes-text': 'x' });
    const out = path.join(root, 'sub', 'latest.json');
    const body = writeManifest(m, out);
    assert.equal(body.endsWith('\n'), true);
    assert.equal(Buffer.compare(fs.readFileSync(out), Buffer.from(body, 'utf8')), 0);
    const parsed = JSON.parse(body);
    assert.deepEqual(Object.keys(parsed), ['version', 'notes', 'pub_date', 'platforms']);
    assert.deepEqual(Object.keys(parsed.platforms['windows-x86_64']), ['url', 'signature']);
  });
});

test('round-trip: CLI build output is byte-for-byte loadable as JSON with the tauri plugin schema shape', () => {
  withFixture((root) => {
    const assets = path.join(root, 'msi');
    seedAssets(assets, '0.12.0', 'x64', SIG);
    const m = buildManifest({ version: '0.12.0', arch: 'x64', 'assets-dir': assets, 'pub-date': DATE });
    const out = path.join(root, 'latest.json');
    writeManifest(m, out);
    const reloaded = JSON.parse(fs.readFileSync(out, 'utf8'));
    // tauri-plugin-updater v2 UpdateManifest shape check.
    assert.equal(reloaded.version, m.version);
    assert.equal(reloaded.pub_date, m.pub_date);
    assert.equal(reloaded.notes, m.notes);
    assert.equal(reloaded.platforms['windows-x86_64'].url, m.platforms['windows-x86_64'].url);
    assert.equal(reloaded.platforms['windows-x86_64'].signature, m.platforms['windows-x86_64'].signature);
  });
});
