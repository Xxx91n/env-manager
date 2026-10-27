// scripts/npm-audit-vex.test.mjs - fail-closed regression tests for the
// VEX-aware npm audit gate. Run with:
//   node --test scripts/npm-audit-vex.test.mjs
//
// The gate is exercised as a subprocess: its contract is the process exit code
// plus the exemption provenance printed on stdout, and importing it would run
// main() and exit the test runner.
//
// The happy-path fixture is the real advisory set from the failed audit run
// behind PR #112 (issue #105): one high (source-map-js) and seven moderate
// advisories (npm audit spells the level `moderate`). The fixture VEX document
// is a fixed snapshot of that triage, so the test does not turn red when a
// stale exemption is later removed from security/vex/frontend.json. A separate
// test replays every statement in the checked-in document through the gate,
// validating the real OpenVEX shape without pinning it to this lockfile.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..');
const gate = path.join(scriptDir, 'npm-audit-vex.mjs');
const realVexDir = path.join(repoRoot, 'security', 'vex');
const realVexFile = path.join(realVexDir, 'frontend.json');

// The eight GHSA advisories reported by the audit run referenced in PR #112.
// Titles are the GitHub advisory summaries; `purl` is the product version from
// that triage. `source` values are synthetic numeric ids: real npm audit
// reports carry one alongside the advisory URL, and the gate must derive the
// GHSA from that URL.
const REAL_ADVISORIES = [
  {
    ghsa: 'GHSA-68fv-2mgg-jv7q',
    package: 'source-map-js',
    purl: 'pkg:npm/source-map-js@1.2.1',
    severity: 'high',
    source: 1102401,
    title: 'source-map-js allows event-loop denial of service through indexed source-map section offsets',
  },
  {
    ghsa: 'GHSA-rj75-hqrm-r3gf',
    package: 'postcss-selector-parser',
    purl: 'pkg:npm/postcss-selector-parser@6.1.4',
    severity: 'moderate',
    source: 1102402,
    title: 'PostCSS: Quadratic complexity in flat selector parsing allows CPU exhaustion',
  },
  {
    ghsa: 'GHSA-crpf-4hrx-3jrp',
    package: 'svelte',
    purl: 'pkg:npm/svelte@4.2.20',
    severity: 'moderate',
    source: 1102403,
    title: 'Svelte SSR attribute spreading includes inherited properties from prototype chain',
  },
  {
    ghsa: 'GHSA-m56q-vw4c-c2cp',
    package: 'svelte',
    purl: 'pkg:npm/svelte@4.2.20',
    severity: 'moderate',
    source: 1102404,
    title: 'Svelte SSR does not validate dynamic element tag names in svelte:element',
  },
  {
    ghsa: 'GHSA-f7gr-6p89-r883',
    package: 'svelte',
    purl: 'pkg:npm/svelte@4.2.20',
    severity: 'moderate',
    source: 1102405,
    title: 'Svelte affected by cross-site scripting via spread attributes in Svelte SSR',
  },
  {
    ghsa: 'GHSA-phwv-c562-gvmh',
    package: 'svelte',
    purl: 'pkg:npm/svelte@4.2.20',
    severity: 'moderate',
    source: 1102406,
    title: 'Svelte vulnerable to XSS during SSR with contenteditable bind:innerText and bind:textContent',
  },
  {
    ghsa: 'GHSA-rcqx-6q8c-2c42',
    package: 'svelte',
    purl: 'pkg:npm/svelte@4.2.20',
    severity: 'moderate',
    source: 1102407,
    title: 'Svelte vulnerable to XSS via DOM Clobbering of Internal Framework State',
  },
  {
    ghsa: 'GHSA-pr6f-5x2q-rwfp',
    package: 'svelte',
    purl: 'pkg:npm/svelte@4.2.20',
    severity: 'moderate',
    source: 1102408,
    title: 'Svelte SSR vulnerable to cross-site scripting via spread attributes',
  },
];

const PROVENANCE_POLICY = 'SECURITY.md#known-dependency-advisories--disposition';

function withTempDir(fn) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'npm-audit-vex-'));
  try {
    return fn(dir);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function writeJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

function runGate(args) {
  const result = spawnSync(process.execPath, [gate, ...args], {
    cwd: repoRoot,
    encoding: 'utf8',
  });
  assert.equal(result.error, undefined, `failed to spawn the gate: ${result.error}`);
  return { code: result.status, stdout: result.stdout, stderr: result.stderr };
}

// Write an audit report and a VEX directory into a throwaway tree, run the
// gate against them, and clean up. `vex` maps filename -> document; an empty
// map models "no VEX documents at all".
function runWithFixtures({ audit, vex = {}, args = [] }) {
  return withTempDir((dir) => {
    const input = path.join(dir, 'audit.json');
    writeJson(input, audit);
    const vexDir = path.join(dir, 'vex');
    fs.mkdirSync(vexDir, { recursive: true });
    for (const [file, document] of Object.entries(vex)) {
      writeJson(path.join(vexDir, file), document);
    }
    return runGate([`--input=${input}`, `--vex-dir=${vexDir}`, ...args]);
  });
}

// Minimal npm audit v2 report. The gate only reads `vulnerabilities[*].via`
// advisory objects, but the surrounding fields keep the fixture faithful.
function npmAuditReport(advisories) {
  const vulnerabilities = {};
  for (const advisory of advisories) {
    const entry = vulnerabilities[advisory.package] ??= {
      name: advisory.package,
      severity: advisory.severity,
      isDirect: false,
      via: [],
      effects: [],
      range: '*',
      nodes: [`node_modules/${advisory.package}`],
      fixAvailable: false,
    };
    entry.via.push({
      source: advisory.source,
      name: advisory.package,
      dependency: advisory.package,
      title: advisory.title,
      url: advisory.ghsa
        ? `https://github.com/advisories/${advisory.ghsa}`
        : advisory.url,
      severity: advisory.severity,
      range: '*',
    });
  }
  return {
    auditReportVersion: 2,
    vulnerabilities,
    metadata: { vulnerabilities: {} },
  };
}

function vexDocument(statements) {
  return {
    '@context': 'https://openvex.dev/ns/v0.2.0',
    '@id': 'https://example.test/vex/fixture.json',
    author: 'Env Manager test fixture',
    role: 'Document Creator',
    timestamp: '2026-10-25T00:00:00.000Z',
    version: 1,
    statements,
  };
}

function notAffected(ghsa, purl, { provenance = 'complete' } = {}) {
  const statement = {
    vulnerability: { name: ghsa },
    timestamp: '2026-10-25T00:00:00.000Z',
    products: [{ '@id': purl, subcomponents: [] }],
    status: 'not_affected',
    justification: 'vulnerable_code_not_in_execute_path',
    impact_statement: 'test fixture statement',
  };
  if (provenance === 'complete') {
    statement.provenance = {
      policy: PROVENANCE_POLICY,
      advisory: `https://github.com/advisories/${ghsa}`,
      triaged: '2026-10-25',
      triaged_by: 'issue #113',
    };
  } else if (provenance === 'missing-policy') {
    statement.provenance = { advisory: `https://github.com/advisories/${ghsa}` };
  } else if (provenance === 'missing-advisory') {
    statement.provenance = { policy: PROVENANCE_POLICY };
  }
  return statement;
}

function packageNameFromPurl(purl) {
  const decoded = decodeURIComponent(purl);
  return /^pkg:npm\/((?:@[^/]+\/)?[^/@]+)@/.exec(decoded)?.[1] ?? null;
}

// Fixed VEX snapshot for the #112 triage. Kept local rather than read from
// security/vex/frontend.json so removing a stale exemption from the live
// document does not turn this regression test red.
const REAL_VEX_DOCUMENT = vexDocument(
  REAL_ADVISORIES.map(({ ghsa, purl }) => notAffected(ghsa, purl)),
);

test('replays the 8-advisory #112 set against matching VEX: exit 0 and provenance', () => {
  const audit = npmAuditReport(REAL_ADVISORIES);

  const high = runWithFixtures({ audit, vex: { 'frontend.json': REAL_VEX_DOCUMENT } });
  assert.equal(high.code, 0, high.stderr);
  assert.match(high.stdout, /audit advisories: 8 total, 1 exempted by VEX, 7 below threshold, 0 blocking/);
  assert.match(high.stdout, /EXEMPT\s+GHSA-68fv-2mgg-jv7q \(high\) source-map-js/);
  assert.match(high.stdout, new RegExp(`policy: ${PROVENANCE_POLICY.replace(/[.#]/g, '\\$&')}`));
  assert.match(high.stdout, /advisory: https:\/\/github\.com\/advisories\/GHSA-68fv-2mgg-jv7q/);
  assert.match(high.stdout, /provenance: .*frontend\.json statement #1/);

  // Raising the threshold to moderate proves every statement is reachable
  // and each of the eight advisories is exempted, not merely below the bar.
  const moderate = runWithFixtures({
    audit,
    vex: { 'frontend.json': REAL_VEX_DOCUMENT },
    args: ['--audit-level=moderate'],
  });
  assert.equal(moderate.code, 0, moderate.stderr);
  assert.match(moderate.stdout, /audit advisories: 8 total, 8 exempted by VEX, 0 below threshold, 0 blocking/);
  assert.equal((moderate.stdout.match(/^EXEMPT\b/gm) ?? []).length, REAL_ADVISORIES.length);
  assert.equal((moderate.stdout.match(/^  policy: /gm) ?? []).length, REAL_ADVISORIES.length);
  assert.equal((moderate.stdout.match(/^  advisory: /gm) ?? []).length, REAL_ADVISORIES.length);
  assert.equal((moderate.stdout.match(/^  provenance: /gm) ?? []).length, REAL_ADVISORIES.length);
  for (const { ghsa } of REAL_ADVISORIES) {
    assert.match(moderate.stdout, new RegExp(`advisory: https://github\\.com/advisories/${ghsa}`));
  }
});

test('an unexempted high advisory fails the gate', () => {
  const run = runWithFixtures({
    audit: npmAuditReport([
      ...REAL_ADVISORIES,
      {
        ghsa: 'GHSA-xxxx-yyyy-zzzz',
        package: 'left-pad',
        severity: 'high',
        source: 999001,
        title: 'synthetic high advisory with no VEX statement',
      },
    ]),
    vex: { 'frontend.json': REAL_VEX_DOCUMENT },
  });
  assert.equal(run.code, 1);
  assert.match(run.stderr, /GHSA-xxxx-yyyy-zzzz \(high\) left-pad/);
  assert.match(run.stderr, /no covering VEX not_affected statement/);
});

test('a not_affected VEX statement without complete provenance fails closed', () => {
  for (const provenance of ['absent', 'missing-policy', 'missing-advisory']) {
    const run = runWithFixtures({
      audit: npmAuditReport([
        {
          ghsa: 'GHSA-aaaa-bbbb-cccc',
          package: 'left-pad',
          severity: 'high',
          source: 999002,
          title: 'synthetic high advisory',
        },
      ]),
      vex: {
        'broken.json': vexDocument([
          notAffected('GHSA-aaaa-bbbb-cccc', 'pkg:npm/left-pad@1.3.0', { provenance }),
        ]),
      },
    });
    assert.equal(run.code, 1, `${provenance}: expected exit 1`);
    assert.match(run.stderr, /not_affected without provenance/, `${provenance}: message`);
  }
});

test('a VEX statement naming a different package fails closed', () => {
  const run = runWithFixtures({
    audit: npmAuditReport([
      {
        ghsa: 'GHSA-dddd-eeee-ffff',
        package: 'svelte',
        severity: 'high',
        source: 999003,
        title: 'synthetic high advisory on svelte',
      },
    ]),
    vex: {
      'wrong-package.json': vexDocument([
        notAffected('GHSA-dddd-eeee-ffff', 'pkg:npm/source-map-js@1.2.1'),
      ]),
    },
  });
  assert.equal(run.code, 1);
  assert.match(run.stderr, /names different package\(s\): source-map-js but audit affects svelte/);
});

test('an advisory without an identifiable GHSA cannot be exempted at high', () => {
  const run = runWithFixtures({
    audit: npmAuditReport([
      {
        package: 'left-pad',
        severity: 'high',
        source: 999004,
        title: 'opaque advisory with no GHSA url',
        url: 'https://example.test/advisory/999004',
      },
    ]),
    // Even a document that names the synthetic id must not match: the gate
    // keys coverage on a real GHSA, so this advisory can never be exempted.
    vex: {
      'lookalike.json': vexDocument([
        notAffected('unidentified-advisory-999004', 'pkg:npm/left-pad@1.3.0'),
      ]),
    },
  });
  assert.equal(run.code, 1);
  assert.match(run.stderr, /unidentified-advisory-999004/);
  assert.match(run.stderr, /no covering VEX not_affected statement/);
});

test("checked-in VEX statements pass the gate's own validation", () => {
  const doc = JSON.parse(fs.readFileSync(realVexFile, 'utf8'));
  const advisories = doc.statements.map((statement, index) => {
    const where = `statement #${index + 1}`;
    const purl = statement.products?.[0]?.['@id'];
    assert.equal(typeof purl, 'string', `${where}: product @id`);
    const packageName = packageNameFromPurl(purl);
    assert.equal(typeof packageName, 'string', `${where}: product is an npm purl`);
    return {
      ghsa: statement.vulnerability?.name,
      package: packageName,
      severity: 'high',
      source: 900000 + index,
      title: `fixture for ${statement.vulnerability?.name}`,
    };
  });
  assert.notEqual(advisories.length, 0, 'checked-in VEX carries statements');

  withTempDir((dir) => {
    const input = path.join(dir, 'audit.json');
    writeJson(input, npmAuditReport(advisories));
    const run = runGate([`--input=${input}`, `--vex-dir=${realVexDir}`]);
    assert.equal(run.code, 0, run.stderr);
    assert.match(
      run.stdout,
      new RegExp(`audit advisories: ${advisories.length} total, ${advisories.length} exempted by VEX, 0 below threshold, 0 blocking`),
    );
    for (const advisory of advisories) {
      assert.match(run.stdout, new RegExp(`EXEMPT\\s+${advisory.ghsa} \\(high\\) ${advisory.package}`));
    }
  });
});

test('security/vex/frontend.json products use @id npm purls with provenance fields', () => {
  const doc = JSON.parse(fs.readFileSync(realVexFile, 'utf8'));
  assert.equal(Array.isArray(doc.statements), true, 'statements array');
  assert.notEqual(doc.statements.length, 0, 'statements non-empty');

  doc.statements.forEach((statement, index) => {
    const where = `statement #${index + 1}`;
    assert.match(
      statement.vulnerability?.name ?? '',
      /^GHSA-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$/,
      `${where}: vulnerability.name is a GHSA id`,
    );
    assert.equal(
      Array.isArray(statement.products) && statement.products.length > 0,
      true,
      `${where}: products is a non-empty array`,
    );
    for (const product of statement.products) {
      assert.equal(Object.hasOwn(product, '@id'), true, `${where}: product uses @id, not identifier`);
      assert.match(product['@id'], /^pkg:npm\/.+@.+$/, `${where}: product is a versioned npm purl`);
    }
    assert.equal(
      typeof statement.provenance?.policy === 'string' && statement.provenance.policy.length > 0,
      true,
      `${where}: provenance.policy is non-empty`,
    );
    assert.equal(
      typeof statement.provenance?.advisory === 'string' && statement.provenance.advisory.length > 0,
      true,
      `${where}: provenance.advisory is non-empty`,
    );
  });
});
