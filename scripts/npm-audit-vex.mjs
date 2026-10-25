#!/usr/bin/env node
// VEX-aware npm audit gate for the frontend package graph.
//
// Runs `npm audit --omit=dev --json` in frontend/ and fails (exit 1) when any
// advisory at or above --audit-level is not covered by a `not_affected`
// OpenVEX statement in security/vex/*.json. Every exemption is printed with
// the provenance recorded on its statement; a VEX document that is malformed
// or names the wrong package fails the gate (fail closed).
//
// Flags:
//   --audit-level=<low|moderate|high|critical>   blocking threshold (default: high)
//   --registry=<url>                             npm registry passed to npm audit
//   --input=<file>                               replay a saved `npm audit --json`
//                                                report instead of running npm
//                                                (offline self-test)
//   --vex-dir=<dir>                              VEX document directory
//                                                (default: <repo>/security/vex)
//   --audit-dir=<dir>                            directory npm audit runs in
//                                                (default: <repo>/frontend)

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(scriptDir, '..');

const SEVERITY_ORDER = { low: 0, moderate: 1, high: 2, critical: 3 };

function parseArgs(argv) {
  const opts = {
    auditLevel: 'high',
    registry: 'https://registry.npmjs.org',
    input: null,
    vexDir: join(repoRoot, 'security', 'vex'),
    auditDir: join(repoRoot, 'frontend'),
  };
  for (const arg of argv) {
    const m = /^--([a-z-]+)=(.+)$/.exec(arg);
    if (!m) fail(`unrecognized argument: ${arg}`);
    const key = m[1].replace(/-([a-z])/g, (_, c) => c.toUpperCase());
    if (!(key in opts)) fail(`unknown option: ${m[1]}`);
    opts[key] = m[2];
  }
  if (!(opts.auditLevel in SEVERITY_ORDER)) fail(`invalid --audit-level: ${opts.auditLevel}`);
  return opts;
}

function fail(message) {
  console.error(`npm-audit-vex: ${message}`);
  process.exit(1);
}

function loadAuditReport(opts) {
  if (opts.input) {
    return JSON.parse(readFileSync(opts.input, 'utf8'));
  }
  // npm audit's nonzero exit merely means "vulnerabilities found"; the report
  // itself is the payload. The registry value is interpolated into a shell
  // command on purpose: npm is a .cmd shim on Windows (shell-less spawn is
  // refused by Node), and every source of the value — the default constant,
  // the pinned CI workflow, a maintainer-facing CLI flag — is already trusted.
  const result = spawnSync(
    `npm audit --omit=dev --json --registry=${opts.registry}`,
    { cwd: opts.auditDir, shell: true, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
  if (result.error || !result.stdout) {
    fail(`npm audit failed to run: ${result.error ?? result.stderr?.slice(-400) ?? 'no output'}`);
  }
  let report;
  try {
    report = JSON.parse(result.stdout);
  } catch {
    fail(`npm audit produced unparseable output:\n${result.stdout.slice(-400)}\n${result.stderr?.slice(-400) ?? ''}`);
  }
  if (!report.vulnerabilities) {
    fail('npm audit JSON carries no vulnerabilities map; npm version too old?');
  }
  return report;
}

// Collect one entry per advisory (not per package): npm attaches each advisory
// object to the directly vulnerable packages and string references to chain
// members, so dedupe by GHSA id extracted from the advisory URL. Advisories
// without an identifiable GHSA id stay anonymous and can never be exempted —
// fail closed.
function collectAdvisories(report) {
  const byId = new Map();
  for (const [packageName, entry] of Object.entries(report.vulnerabilities)) {
    for (const via of entry.via ?? []) {
      if (typeof via === 'string') continue; // indirect propagation record
      const ghsa = /^.*\/(GHSA-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4})$/.exec(via.url ?? '')?.[1]
        ?? (typeof via.source === 'string' && /^GHSA-/.test(via.source) ? via.source : null);
      const hasGhsa = ghsa !== null;
      const id = hasGhsa ? ghsa : `unidentified-advisory-${via.source}`;
      if (!byId.has(id)) {
        byId.set(id, { id, hasGhsa, packages: new Set(), severity: via.severity, title: via.title, url: via.url });
      }
      byId.get(id).packages.add(packageName);
    }
  }
  return [...byId.values()];
}

function purlName(identifier) {
  // purl percent-encodes "@" in scopes (%40scope); npm audit does not.
  const decoded = decodeURIComponent(identifier);
  return /^pkg:npm\/(@[^/\s]+\/[^/@\s]+|[^/@\s]+?)(?:@|$)/.exec(decoded)?.[1] ?? null;
}

// Returns Map: uppercase GHSA id -> { statements: [...], packages: Set<string> }
function loadVexCoverage(opts) {
  const coverage = new Map();
  if (!existsSync(opts.vexDir)) {
    return coverage; // no VEX documents at all: gate still works, nothing is exempt
  }
  for (const file of readdirSync(opts.vexDir).filter((f) => f.endsWith('.json'))) {
    const path = join(opts.vexDir, file);
    let doc;
    try {
      doc = JSON.parse(readFileSync(path, 'utf8'));
    } catch (e) {
      fail(`VEX document ${path} is not valid JSON: ${e.message}`);
    }
    const statements = doc.statements;
    if (!Array.isArray(statements)) fail(`VEX document ${path} carries no statements array`);
    statements.forEach((s, i) => {
      const where = `${path} statement #${i + 1}`;
      const vulnName = s.vulnerability?.name;
      if (typeof vulnName !== 'string' || vulnName.length === 0) fail(`${where}: missing vulnerability.name`);
      const products = s.products ?? [];
      if (products.length === 0) fail(`${where}: no products listed`);
      const names = products.map((p) => purlName(p.identifier ?? p['@id'] ?? p));
      if (names.some((n) => n === null)) fail(`${where}: product identifiers are not npm purls`);

      if (s.status !== 'not_affected') return; // other statuses never exempt anything
      if (!s.justification && !s.impact_statement) {
        fail(`${where}: not_affected without justification or impact_statement`);
      }
      const provenance = s.provenance;
      if (!provenance || typeof provenance !== 'object' || !provenance.policy || !provenance.advisory) {
        fail(`${where}: not_affected without provenance (policy + advisory link are mandatory)`);
      }
      const key = vulnName.toUpperCase();
      if (!coverage.has(key)) coverage.set(key, { statements: [], packages: new Set() });
      const bucket = coverage.get(key);
      bucket.statements.push({ where, statement: s });
      names.forEach((n) => bucket.packages.add(n));
    });
  }
  return coverage;
}

function main() {
  const opts = parseArgs(process.argv.slice(2));
  const report = loadAuditReport(opts);
  const advisories = collectAdvisories(report);
  const coverage = loadVexCoverage(opts);
  const threshold = SEVERITY_ORDER[opts.auditLevel];

  const failures = [];
  const exempted = [];
  const informational = [];

  for (const advisory of advisories) {
    const rank = SEVERITY_ORDER[advisory.severity] ?? SEVERITY_ORDER.critical;
    const label = `${advisory.id} (${advisory.severity}) ${[...advisory.packages].join(', ')}`;

    const bucket = advisory.hasGhsa ? coverage.get(advisory.id.toUpperCase()) : undefined;
    if (bucket) {
      const crossPackage = [...advisory.packages].filter((p) => !bucket.packages.has(p));
      if (crossPackage.length > 0) {
        failures.push(`${label} — VEX statement names different package(s): ${[...bucket.packages].join(', ')} but audit affects ${crossPackage.join(', ')}`);
        continue;
      }
    }

    if (rank >= threshold) {
      if (bucket) {
        exempted.push(advisory);
        for (const { where, statement } of bucket.statements) {
          console.log(`EXEMPT  ${label}`);
          console.log(`  status: not_affected${statement.justification ? ` / ${statement.justification}` : ''}`);
          if (statement.impact_statement) console.log(`  impact: ${statement.impact_statement}`);
          console.log(`  provenance: ${where}`);
          console.log(`  policy: ${statement.provenance.policy}`);
          console.log(`  advisory: ${statement.provenance.advisory}`);
          if (statement.provenance.tracking) console.log(`  tracking: ${statement.provenance.tracking}`);
        }
      } else {
        failures.push(`${label} — no covering VEX not_affected statement${advisory.url ? ` (see ${advisory.url})` : ''}`);
      }
    } else if (bucket) {
      informational.push(label);
      console.log(`ALLOWED (below --audit-level=${opts.auditLevel}, covered by VEX)  ${label}`);
      console.log(`  provenance: ${bucket.statements[0].statement.provenance.advisory}`);
    } else {
      informational.push(label);
      console.log(`ALLOWED (below --audit-level=${opts.auditLevel})  ${label}`);
    }
  }

  console.log('');
  console.log(`audit advisories: ${advisories.length} total, ${exempted.length} exempted by VEX, ${informational.length} below threshold, ${failures.length} blocking`);

  if (failures.length > 0) {
    console.error('');
    console.error('npm-audit-vex: BLOCKING advisories without a valid VEX exemption:');
    for (const f of failures) console.error(`  - ${f}`);
    process.exit(1);
  }
  console.log('npm-audit-vex: gate green');
  process.exit(0);
}

main();
