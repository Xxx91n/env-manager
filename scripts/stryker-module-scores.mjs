#!/usr/bin/env node
/**
 * Ticket 18 per-module mutation score (模块分算).
 * Usage: node scripts/stryker-module-scores.mjs [mutation-report.html or mutation-report.json]
 * Accepts either the Stryker HTML report (extracts the embedded app.report JSON, the
 * format used by StrykerOutput reports) or a raw mutation-report.json.
 * Prints a per-module table plus totals; exits 0 always (reporting tool, not a gate).
 */
import { readFileSync } from 'node:fs';

const input = process.argv[2];
if (!input) { console.error('usage: node scripts/stryker-module-scores.mjs <mutation-report.html or .json>'); process.exit(2); }
let raw = readFileSync(input, 'utf8');
let report;
if (input.endsWith('.html')) {
  const marker = 'app.report = ';
  const start = raw.indexOf(marker) + marker.length;
  let i = raw.indexOf('{', start);
  let depth = 0, inStr = false, esc = false, end = -1;
  for (; i < raw.length; i++) {
    const c = raw[i];
    if (inStr) { if (esc) esc = false; else if (c === '\\') esc = true; else if (c === '"') inStr = false; }
    else if (c === '"') inStr = true;
    else if (c === '{') depth++;
    else if (c === '}') { depth--; if (depth === 0) { end = i + 1; break; } }
  }
  report = JSON.parse(raw.slice(start, end));
} else {
  report = JSON.parse(raw);
}

const rows = [];
let totals = { killed: 0, survived: 0, noCoverage: 0, timeout: 0, skipped: 0, runtimeError: 0, compileError: 0 };
for (const [file, data] of Object.entries(report.files)) {
  if (!file.endsWith('.cs') || data.mutants.length === 0) continue;
  const c = { killed: 0, survived: 0, noCoverage: 0, timeout: 0, skipped: 0, runtimeError: 0, compileError: 0 };
  for (const m of data.mutants) {
    switch (m.status) {
      case 'Killed': c.killed++; break;
      case 'Survived': c.survived++; break;
      case 'NoCoverage': c.noCoverage++; break;
      case 'Timeout': c.timeout++; break;
      case 'Skipped': c.skipped++; break;
      case 'RuntimeError': c.runtimeError++; break;
      case 'CompileError': c.compileError++; break;
    }
  }
  for (const k of Object.keys(totals)) totals[k] += c[k];
  const detected = c.killed + c.timeout;
  const tested = detected + c.survived;
  const score = tested > 0 ? (100 * detected / tested).toFixed(2) + '%' : 'n/a';
  rows.push({ file: file.split('\\').pop(), ...c, tested, score });
}
rows.sort((a, b) => a.file.localeCompare(b.file));
const detectedT = totals.killed + totals.timeout;
const testedT = detectedT + totals.survived;
const totalScore = testedT > 0 ? (100 * detectedT / testedT).toFixed(2) + '%' : 'n/a';
console.log('module'.padEnd(26), 'test'.padStart(5), 'kill'.padStart(5), 'surv'.padStart(5), 'noCov'.padStart(6), 't/o'.padStart(4), 'score'.padStart(8));
for (const r of rows) {
  console.log(r.file.padEnd(26), String(r.tested).padStart(5), String(r.killed).padStart(5), String(r.survived).padStart(5), String(r.noCoverage).padStart(6), String(r.timeout).padStart(4), r.score.padStart(8));
}
console.log('TOTAL'.padEnd(26), String(testedT).padStart(5), String(totals.killed).padStart(5), String(totals.survived).padStart(5), String(totals.noCoverage).padStart(6), String(totals.timeout).padStart(4), totalScore.padStart(8));
console.log('thresholds:', JSON.stringify(report.thresholds), '| schemaVersion:', report.schemaVersion);
