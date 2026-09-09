#!/usr/bin/env node
// scripts/gen-latest-json.mjs - Tauri v2 updater manifest generator (ticket 44, architecture-recovery)
//
// Reads release/ artifact directory and emits a `latest.json` manifest matching
// the schema expected by @tauri-apps/plugin-updater (v2) / tauri-plugin-updater.
//
// Usage:
//   node scripts/gen-latest-json.mjs --assets-dir <dir> --version <semver> --out <path>
//       [--url-base <prefix>]
//       [--notes-file <path> | --notes-text <string>]
//       [--pub-date <rfc3339>]
//       [--arch <x64|x86|arm64>]
//
// Schema (https://v2.tauri.app/plugin/updater/):
//   {
//     "version": "<semver>",
//     "notes": "<markdown>",
//     "pub_date": "<RFC3339 timestamp>",
//     "platforms": {
//       "<target-triple>": { "url": "<download url>", "signature": "<minisign base64>" }
//     }
//   }
//
// Artifact naming contract (matches scripts/build.mjs MSI bundle output + the
// updater sign step in .github/workflows/build.yml package job):
//   release/msi/Env Manager_<version>_<arch>.msi
//   release/msi/Env Manager_<version>_<arch>.msi.zip
//   release/msi/Env Manager_<version>_<arch>.msi.zip.sig
//
// Exit codes: 0 = manifest written; 1 = schema or input validation failure;
// 2 = usage error. Hard boundaries: never fabricates a `signature` value, never
// overwrites the .sig file, and emits no plaintext private key material. The
// pub_date defaults to the current UTC time in RFC3339 with second precision.

import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const PLATFORM_TRIPLE = {
  x64: 'windows-x86_64',
  x86: 'windows-i686',
  arm64: 'windows-aarch64',
};

function parseArgs(argv) {
  const opts = { arch: 'x64' };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const eq = a.indexOf('=');
    let key, value;
    if (a.startsWith('--') && eq > -1) {
      key = a.slice(2, eq);
      value = a.slice(eq + 1);
    } else if (a.startsWith('--')) {
      key = a.slice(2);
      value = argv[++i];
    } else {
      throw new Error(`unexpected positional argument: ${a}`);
    }
    opts[key] = value;
  }
  return opts;
}

function validateSemver(v) {
  if (typeof v !== 'string') return false;
  return /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(v);
}

function validateRfc3339(s) {
  if (typeof s !== 'string') return false;
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(s);
}

function readSigFile(sigPath) {
  // Tauri's `tauri signer sign` writes the minisign signature as raw text
  // (a base64-encoded line block). Pass the bytes verbatim; trim trailing newline.
  const raw = fs.readFileSync(sigPath, 'utf8');
  return raw.replace(/\r?\n$/, '');
}

function buildManifest(opts) {
  const version = String(opts.version);
  if (!validateSemver(version)) {
    throw new Error(`--version must be a semver string, got "${version}"`);
  }
  const pubDate = opts['pub-date']
    ? String(opts['pub-date'])
    : new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
  if (!validateRfc3339(pubDate)) {
    throw new Error(`--pub-date must be an RFC3339 timestamp, got "${pubDate}"`);
  }
  const arch = String(opts.arch);
  const triple = PLATFORM_TRIPLE[arch];
  if (!triple) {
    throw new Error(`--arch must be one of ${Object.keys(PLATFORM_TRIPLE).join(', ')} (got "${arch}")`);
  }
  const base = opts['url-base']
    ? String(opts['url-base'])
    : `https://github.com/Xxx91n/env-manager/releases/download/v${version}/`;
  const assetsDir = String(opts['assets-dir']);
  if (!fs.existsSync(assetsDir)) {
    throw new Error(`--assets-dir does not exist: ${assetsDir}`);
  }
  const zipName = `Env Manager_${version}_${arch}.msi.zip`;
  const sigName = `${zipName}.sig`;
  const zipPath = path.join(assetsDir, zipName);
  const sigPath = path.join(assetsDir, sigName);
  if (!fs.existsSync(zipPath)) {
    throw new Error(`missing updater bundle: ${zipPath} (build with TAURI_SIGNING_PRIVATE_KEY set to emit ${zipName})`);
  }
  if (!fs.existsSync(sigPath)) {
    throw new Error(`missing updater signature: ${sigPath}`);
  }
  const signature = readSigFile(sigPath);
  if (!signature || /\r?\n/.test(signature)) {
    throw new Error(`signature file ${sigName} must be a single base64 line`);
  }
  let notes = '';
  if (opts['notes-file']) {
    notes = fs.readFileSync(String(opts['notes-file']), 'utf8');
  } else if (opts['notes-text']) {
    notes = String(opts['notes-text']);
  } else {
    notes = `Env Manager ${version}`;
  }
  const url = base.replace(/\/?$/, '/') + zipName;
  return {
    version,
    notes,
    pub_date: pubDate,
    platforms: {
      [triple]: { url, signature },
    },
  };
}

function writeManifest(manifest, outPath) {
  const dir = path.dirname(outPath);
  if (dir && dir !== '.' && !fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  const body = JSON.stringify(manifest, null, 2) + '\n';
  fs.writeFileSync(outPath, body, { encoding: 'utf8' });
  return body;
}

export { buildManifest, writeManifest, validateSemver, validateRfc3339, PLATFORM_TRIPLE };

const isDirectRun = (() => {
  try {
    const entry = process.argv[1] ? path.resolve(process.argv[1]) : '';
    const here = path.resolve(new URL(import.meta.url).pathname.replace(/^\//, ''));
    return entry && entry === here;
  } catch {
    return false;
  }
})();

if (isDirectRun) {
  try {
    const opts = parseArgs(process.argv.slice(2));
    if (!opts['assets-dir'] || !opts.version || !opts.out) {
      process.stderr.write('usage: gen-latest-json.mjs --assets-dir <dir> --version <semver> --out <path> [--url-base <prefix>] [--notes-file <path>|--notes-text <s>] [--pub-date <rfc3339>] [--arch <x64|x86|arm64>]\n');
      process.exit(2);
    }
    const manifest = buildManifest(opts);
    const body = writeManifest(manifest, String(opts.out));
    process.stdout.write(`[gen-latest-json] wrote ${opts.out} (${Buffer.byteLength(body, 'utf8')} bytes)\n`);
    process.exit(0);
  } catch (err) {
    process.stderr.write(`[gen-latest-json] ERROR: ${err.message}\n`);
    process.exit(1);
  }
}
