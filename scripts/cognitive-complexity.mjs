#!/usr/bin/env node
// Cognitive complexity guard (ticket 29, architecture-recovery) - Sonar-style
// method-level cognitive complexity scanner for the C# CLI engine (src/*.cs).
//
// Modes:
//   node scripts/cognitive-complexity.mjs                     report mode (exit 0; CI report step)
//   node scripts/cognitive-complexity.mjs --gate              hard-gate mode (exit 1 on NEW violations only)
//   node scripts/cognitive-complexity.mjs --update-baseline   rewrite the committed baseline JSON
//   node scripts/cognitive-complexity.mjs --selftest          golden snippets (exit 1 on mismatch)
//   node scripts/cognitive-complexity.mjs --demo-gate         demonstrate the hard gate (checkpoint C)
//   node scripts/cognitive-complexity.mjs --json <path>       also write the full scan as JSON
//   node scripts/cognitive-complexity.mjs --root <dir>        scan <dir> instead of src/ (demo/tests)
//
// Algorithm: SonarSource Cognitive Complexity (G. Ann Campbell, 2021 refresh), C# subset:
//   +1 (+ current nesting level) for: if / else if / else / switch (per case label group) /
//      for / foreach / while / do-while (counted once, at its do) / catch / ternary ?:
//   +1 per && or ||; +1 more when the operator differs from the previous logical operator
//      in the same expression context (sequence-break bonus, conservative reading of the spec)
//   +1 flat (no nesting bonus) for: goto / labeled break / labeled continue
//   finally, plain return / throw / break / continue: no increment (guard clauses stay cheap)
//   nesting +1 inside: if/else bodies, switch bodies, loop bodies, catch bodies, ternary branches,
//      lambda / anonymous-method bodies; local functions are measured as separate methods
// Documented deviations from the Roslyn-based Sonar analyzer (see reports/29):
//   - token-level C# subset parser, no type resolution; property accessor bodies are not measured
//   - pattern-matching combinators (and/or/not in is-patterns) are not counted
//   - switch-expression arms are not counted per-arm (classic switch statements are)
//   - exception filters (catch ... when) are not counted; null-coalescing ?? is not counted

import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import url from 'node:url';

const ROOT = path.resolve(path.dirname(url.fileURLToPath(import.meta.url)), '..');
const SRC_DIR = path.join(ROOT, 'src');
const BASELINE_PATH = path.join(ROOT, 'scripts', 'cognitive-complexity-baseline.json');
const THRESHOLD = 15; // SonarWay default for the method-level rule

// ---------------------------------------------------------------- tokenizer
// Strips comments and literals (newlines inside them are preserved for line math),
// emits { v, line }: identifiers/keywords, multi-char operators, single punctuators.
function tokenize(source) {
  const tokens = [];
  const n = source.length;
  let i = 0, line = 1;
  const push = (v, ws) => tokens.push({ v, line, ws: !!ws });
  const nl = () => { line++; push('\n'); };
  while (i < n) {
    const c = source[i];
    const c2 = i + 1 < n ? c + source[i + 1] : '';
    const c3 = i + 2 < n ? c2 + source[i + 2] : '';
    if (c === '\r') { i++; continue; }
    if (c === '\n') { nl(); i++; continue; }
    if (c === ' ' || c === '\t') { push(' ', true); i++; continue; }
    if (c2 === '//') { while (i < n && source[i] !== '\n') i++; continue; }
    if (c2 === '/*') {
      let end = source.indexOf('*/', i + 2);
      const stop = end === -1 ? n : end + 2;
      while (i < stop) { if (source[i] === '\n') nl(); i++; }
      continue;
    }
    if (c3 === '"""') { // C# 11 raw string literal
      let end = source.indexOf('"""', i + 3);
      const stop = end === -1 ? n : end + 3;
      while (i < stop) { if (source[i] === '\n') nl(); i++; }
      push('<lit>');
      continue;
    }
    if (c2 === '@"' || c3 === '$@"' || c3 === '@$"') { // verbatim / interpolated-verbatim
      const start = (c2 === '@"') ? i + 2 : i + 3;
      let j = start;
      while (j < n) {
        if (source[j] === '"' && source[j + 1] === '"') { j += 2; continue; }
        if (source[j] === '"') break;
        j++;
      }
      const stop = Math.min(j + 1, n);
      for (let k = i; k < stop; k++) if (source[k] === '\n') nl();
      push('<lit>');
      i = j + 1;
      continue;
    }
    if (c2 === '$"') { // interpolated string: honor {hole} nesting, {{ }} escapes
      let j = i + 2, brace = 0;
      while (j < n) {
        if (source[j] === '\\') { j += 2; continue; }
        if (source[j] === '{') {
          if (source[j + 1] === '{') { j += 2; continue; }
          brace++; j++; continue;
        }
        if (source[j] === '}') {
          if (source[j + 1] === '}' && brace === 0) { j += 2; continue; }
          if (brace > 0) brace--;
          j++; continue;
        }
        if (source[j] === '"' && brace === 0) break;
        j++;
      }
      const stop = Math.min(j + 1, n);
      for (let k = i; k < stop; k++) if (source[k] === '\n') nl();
      push('<lit>');
      i = j + 1;
      continue;
    }
    if (c === '"') {
      let j = i + 1;
      while (j < n) {
        if (source[j] === '\\') { j += 2; continue; }
        if (source[j] === '"') break;
        j++;
      }
      const stop = Math.min(j + 1, n);
      for (let k = i; k < stop; k++) if (source[k] === '\n') nl();
      push('<lit>');
      i = j + 1;
      continue;
    }
    if (c === "'") {
      let j = i + 1;
      while (j < n) {
        if (source[j] === '\\') { j += 2; continue; }
        if (source[j] === "'") break;
        j++;
      }
      push('<lit>');
      i = j + 1;
      continue;
    }
    if (/[A-Za-z_]/.test(c)) {
      let j = i;
      while (j < n && /[A-Za-z0-9_]/.test(source[j])) j++;
      push(source.slice(i, j));
      i = j;
      continue;
    }
    if (/[0-9]/.test(c)) {
      let j = i;
      while (j < n && /[0-9a-fA-FxXbB._]/.test(source[j])) j++;
      push(source.slice(i, j));
      i = j;
      continue;
    }
    const ops = ['??=', '&&', '||', '??', '?.', '=>', '==', '!=', '::', '++', '--'];
    let matched = false;
    for (const op of ops) {
      if (c2 === op) { push(op); i += 2; matched = true; break; }
    }
    if (matched) continue;
    push(c);
    i++;
  }
  return tokens;
}

// ---------------------------------------------------------------- method extraction
// A callable opens at identifier + '(' ... ')' [+ where clause] [+ : base(...)] then '{' or '=>'.
// Control keywords, lambdas, delegate types and object initializers are excluded by the
// preceding-token and following-token checks. Local functions ARE extracted (Sonar measures
// them separately); their ranges are skipped when measuring the enclosing method.
const CTRL_NAMES = new Set(['if', 'else', 'while', 'for', 'foreach', 'switch', 'catch', 'using',
  'lock', 'fixed', 'return', 'new', 'throw', 'do', 'case', 'when', 'typeof', 'nameof', 'sizeof',
  'get', 'set', 'add', 'remove', 'async', 'delegate', 'and', 'or', 'not']);
const BAD_PREV = new Set(['new', 'return', 'case', 'throw', '=', '=>', ',', '(', '&&', '||', '??', '?.', '?:']);

function isIdent(v) { return /^[A-Za-z_][A-Za-z0-9_]*$/.test(v); }

function prevMeaningful(tokens, idx) {
  for (let k = idx; k >= 0; k--) {
    const v = tokens[k].v;
    if (v !== '\n' && v !== ' ') return { idx: k, v };
  }
  return null;
}

function extractMethods(tokens) {
  const methods = [];
  const n = tokens.length;
  let i = 0;
  while (i < n) {
    const t = tokens[i];
    if (t.v === ' ') { i++; continue; }
    if (t.v === '(') {
      const nameTok = i > 0 ? tokens[i - 1] : null;
      if (nameTok && isIdent(nameTok.v) && !CTRL_NAMES.has(nameTok.v)) {
        const prev = prevMeaningful(tokens, i - 2);
        // walk back over generic/type chains (Dictionary<string, int>) to reject object initializers
        let scan = prev, genericDepth = 0, prevOk = true;
        while (scan) {
          if (scan.v === '>') { genericDepth++; scan = prevMeaningful(tokens, scan.idx - 1); continue; }
          if (genericDepth > 0 && (isIdent(scan.v) || [',', '<', '.', '?'].includes(scan.v))) {
            if (scan.v === '<') genericDepth--;
            scan = prevMeaningful(tokens, scan.idx - 1);
            continue;
          }
          break;
        }
        prevOk = !scan
          || ['{', '}', ';', '[', ']', ')', '?', '.', '<lit>', '='].includes(scan.v)
          || (isIdent(scan.v) && !BAD_PREV.has(scan.v));
        if (prevOk) {
          let d = 0, j = i;
          for (; j < n; j++) {
            if (tokens[j].v === '(') d++;
            else if (tokens[j].v === ')') { d--; if (d === 0) break; }
          }
          if (j < n) {
            let k = j + 1;
            let guard = 0;
            while (k < n && guard < 64) {
              const v = tokens[k].v;
              if (v === '\n' || v === ' ') { k++; guard++; continue; }
              if (v === 'where') { // generic constraints: skip to body start
                while (k < n && tokens[k].v !== '{' && tokens[k].v !== '=>' && tokens[k].v !== ';') k++;
                continue;
              }
              if (v === ':') {
                let q = k + 1;
                while (q < n && (tokens[q].v === ' ' || tokens[q].v === '\n')) q++;
                if (q < n && (tokens[q].v === 'base' || tokens[q].v === 'this')) {
                  k = q + 1; // now at '(' of the ctor-initializer arg list
                  let dd = 0;
                  while (k < n) {
                    if (tokens[k].v === '(') dd++;
                    else if (tokens[k].v === ')') { dd--; if (dd === 0) { k++; break; } }
                    k++;
                  }
                  continue;
                }
                break;
              }
              break;
            }
            if (k < n && (tokens[k].v === '{' || tokens[k].v === '=>')) {
              let bodyEnd;
              if (tokens[k].v === '{') {
                let dd = 0, m = k;
                for (; m < n; m++) {
                  if (tokens[m].v === '{') dd++;
                  else if (tokens[m].v === '}') { dd--; if (dd === 0) break; }
                }
                bodyEnd = Math.min(m, n - 1);
              } else { // expression body: until ';' at relative depth 0
                let dd = 0, m = k + 1;
                for (; m < n; m++) {
                  const v = tokens[m].v;
                  if (v === '(' || v === '[' || v === '{') dd++;
                  else if (v === ')' || v === ']') { if (dd === 0) break; dd--; }
                  else if (v === '}') { if (dd === 0) break; dd--; }
                  else if (v === ';' && dd === 0) break;
                }
                bodyEnd = Math.min(m, n - 1);
              }
              methods.push({ name: nameTok.v, line: nameTok.line, bodyStart: k, bodyEnd, expr: tokens[k].v === '=>' });
              i = bodyEnd + 1;
              continue;
            }
          }
        }
      }
    }
    i++;
  }
  return methods;
}

// ---------------------------------------------------------------- complexity engine
// measure(tokens, lo, hi, name, skipRanges) walks one method body; skipRanges hide
// nested local-function bodies (measured as their own methods).
function measure(tokens, lo, hi, name, skipRanges) {
  let complexity = 0;
  let nesting = 0;
  let parenDepth = 0;
  let lastLogical = {};
  let expectBlock = null;   // true: next '{' nests (control/lambda body)
  let pendingLambda = false;
  const markers = [];
  let prevSig = null;
  const skip = (idx) => skipRanges.some((r) => idx >= r[0] && idx <= r[1]);
  const nextSig = (idx) => {
    let j = idx + 1;
    while (j <= hi && (tokens[j].v === '\n' || tokens[j].v === ' ')) j++;
    return j <= hi ? tokens[j] : null;
  };
  for (let i = lo; i <= hi; i++) {
    const t = tokens[i];
    const v = t.v;
    if (v === '\n' || v === ' ') continue;
    if (skip(i)) { prevSig = null; continue; }
    if (v === 'if') { complexity += 1 + nesting; expectBlock = true; }
    else if (v === 'else') {
      const nx = nextSig(i);
      if (nx && nx.v === 'if') { /* else-if chain: counted by its if */ }
      else complexity += 1;
      expectBlock = true;
    }
    else if (v === 'for' || v === 'foreach' || v === 'while') { complexity += 1 + nesting; expectBlock = true; }
    else if (v === 'do') { expectBlock = true; } // do-while counted once, at its while
    else if (v === 'switch') { complexity += 1 + nesting; expectBlock = true; }
    else if (v === 'case' || v === 'default') {
      if (!(prevSig === 'case' || prevSig === 'default' || prevSig === ':')) complexity += 1;
      // fallthrough labels of one group stay free
    }
    else if (v === 'catch') { complexity += 1 + nesting; expectBlock = true; }
    else if (v === 'finally') { /* no increment, body does not nest */ }
    else if (v === 'goto') { complexity += 1; }
    else if (v === 'break' || v === 'continue') {
      const nx = nextSig(i);
      if (nx && nx.v !== ';' && nx.v !== '}') complexity += 1; // labeled jump
    }
    else if (v === '&&' || v === '||') {
      complexity += 1 + nesting;
      if (lastLogical[parenDepth] && lastLogical[parenDepth] !== v) complexity += 1;
      lastLogical[parenDepth] = v;
    }
    else if (v === '(') { parenDepth++; delete lastLogical[parenDepth]; }
    else if (v === ')') { delete lastLogical[parenDepth]; if (parenDepth > 0) parenDepth--; }
    else if (v === ';' || v === ',') { delete lastLogical[parenDepth]; }
    else if (v === '?') {
      const wsBefore = i > lo && tokens[i - 1].v === ' ';
      if (wsBefore && prevSig && (isIdent(prevSig) || ['<lit>', ')', ']'].includes(prevSig))) {
        complexity += 1 + nesting;
      }
    }
    else if (v === ':') { /* ternary else-arm: no count */ }
    else if (v === '{') {
      delete lastLogical[parenDepth];
      if (pendingLambda || expectBlock) { nesting++; markers.push('ctl'); }
      else markers.push('plain');
      pendingLambda = false;
      expectBlock = null;
    }
    else if (v === '}') {
      delete lastLogical[parenDepth];
      const m = markers.pop();
      if (m === 'ctl') nesting = Math.max(0, nesting - 1);
    }
    else if (v === '=>') {
      delete lastLogical[parenDepth];
      const nx = nextSig(i);
      if (nx && nx.v === '{') pendingLambda = true; // statement lambda body nests
    }
    else if (v === 'delegate') { pendingLambda = true; }
    else if (v === name) {
      const nx = nextSig(i);
      if (nx && nx.v === '(') complexity += 1; // direct recursion
    }
    prevSig = v;
  }
  return complexity;
}

// ---------------------------------------------------------------- scan driver
function scanDir(dir) {
  const out = [];
  if (!fs.existsSync(dir)) return out;
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...scanDir(full));
    else if (e.name.endsWith('.cs')) out.push(full);
  }
  return out;
}

function scanRoot(rootDir) {
  const results = [];
  for (const file of scanDir(rootDir)) {
    const tokens = tokenize(fs.readFileSync(file, 'utf8'));
    const methods = extractMethods(tokens);
    for (const m of methods) {
      const skipRanges = methods
        .filter((o) => o !== m && o.bodyStart > m.bodyStart && o.bodyEnd <= m.bodyEnd)
        .map((o) => [o.bodyStart, o.bodyEnd]);
      const c = measure(tokens, m.bodyStart, m.bodyEnd, m.name, skipRanges);
      results.push({ file: file, name: m.name, line: m.line, complexity: c });
    }
  }
  return results;
}

// ---------------------------------------------------------------- baseline + gate
function methodKey(rootDir, r) {
  const rel = path.relative(rootDir, r.file).split(path.sep).join('/');
  return rel + '#' + r.name;
}

function buildBaseline(results, rootDir) {
  const methods = {};
  const perFile = {};
  const sorted = results.slice().sort((a, b) => a.file === b.file ? a.line - b.line : (a.file < b.file ? -1 : 1));
  for (const r of sorted) {
    const rel = path.relative(rootDir, r.file).split(path.sep).join('/');
    const ord = perFile[rel + '#' + r.name] || 0;
    perFile[rel + '#' + r.name] = ord + 1;
    methods[rel + '#' + r.name + ':' + ord] = r.complexity;
  }
  return { version: 1, threshold: THRESHOLD, methods };
}

function gateCheck(results, baseline, rootDir) {
  const current = buildBaseline(results, rootDir);
  const newViolations = [];
  const stillViolating = [];
  const improved = [];
  for (const [k, c] of Object.entries(current.methods)) {
    if (c <= THRESHOLD) continue;
    const b = baseline.methods ? baseline.methods[k] : undefined;
    if (b === undefined) newViolations.push([k, c]);
    else if (b <= THRESHOLD) newViolations.push([k, c]);
    else stillViolating.push([k, c, b]);
  }
  for (const [k, b] of Object.entries(baseline.methods || {})) {
    if (b > THRESHOLD && current.methods[k] !== undefined && current.methods[k] <= THRESHOLD) improved.push(k);
  }
  return { current, newViolations, stillViolating, improved };
}

// ---------------------------------------------------------------- selftest
const SELFTEST = [
  ['empty method', 0, 'class C { int M() { return 1; } }'],
  ['simple if', 1, 'class C { int M(int a) { if (a > 0) { return 1; } return 0; } }'],
  ['if else', 2, 'class C { int M(int a) { if (a > 0) { return 1; } else { return 0; } } }'],
  ['if else-if else', 3, 'class C { int M(int a) { if (a > 0) return 1; else if (a < 0) return -1; else return 0; } }'],
  ['for loop', 1, 'class C { int M(int n) { int s = 0; for (int i = 0; i < n; i++) { s += i; } return s; } }'],
  ['nested for', 3, 'class C { int M(int n) { int s = 0; for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) { s++; } } return s; } }'],
  ['while loop', 1, 'class C { int M(int n) { int s = 0; while (n > 0) { s++; n--; } return s; } }'],
  ['do-while', 1, 'class C { int M(int n) { int s = 0; do { s++; n--; } while (n > 0); return s; } }'],
  ['switch 3 groups', 4, 'class C { int M(int x) { switch (x) { case 1: return 1; case 2: return 2; default: return 0; } } }'],
  ['try catch', 1, 'class C { int M() { try { return 1; } catch (System.Exception) { return 0; } } }'],
  ['ternary', 1, 'class C { int M(bool a) { return a ? 1 : 0; } }'],
  ['ternary + logical', 2, 'class C { int M(bool a, bool b) { return a && b ? 1 : 0; } }'],
  ['nullable type not ternary', 0, 'class C { void M() { int? x = 1; } }'],
  ['lambda nesting', 2, 'class C { void M(System.Action a) { a(() => { if (1 > 0) { } }); } }'],
  ['goto', 1, 'class C { int M() { goto end; end: return 0; } }'],
  ['sequence break &&||', 3, 'class C { bool M(bool a, bool b, bool c) { return a && b || c; } }'],
  ['same-operator run', 2, 'class C { bool M(bool a, bool b, bool c) { return a && b && c; } }'],
  ['local function separate', 0, 'class C { void M() { int Helper() { return 1; } Helper(); } }'],
  ['object initializer not method', 0, 'class C { void M() { var d = new System.Collections.Generic.Dictionary<string, int>(); } }'],
  ['ctor with base initializer', 0, 'class D : C { public D(int x) : base(x) { } }'],
  ['expression-bodied', 1, 'class C { int M(bool a) => a ? 1 : 0; }'],
];

function runSelftest() {
  let fails = 0;
  for (const [label, expected, snippet] of SELFTEST) {
    const tokens = tokenize(snippet);
    const methods = extractMethods(tokens);
    const m = methods[0];
    const got = m ? measure(tokens, m.bodyStart, m.bodyEnd, m.name, []) : null;
    const ok = got === expected && methods.length === 1;
    if (!ok) fails++;
    console.log((ok ? 'PASS' : 'FAIL') + '  ' + label + ': expected ' + expected + ', got ' + got + (methods.length !== 1 ? ' (methods found: ' + methods.length + ')' : ''));
  }
  console.log(fails === 0 ? 'selftest: all ' + SELFTEST.length + ' golden snippets PASS' : 'selftest: ' + fails + ' FAILURES');
  process.exitCode = fails === 0 ? 0 : 1;
}

// ---------------------------------------------------------------- CLI dispatch
function printReport(results, rootDir, title) {
  const violations = results.filter((r) => r.complexity > THRESHOLD);
  const byFile = {};
  for (const r of results) {
    const rel = path.relative(rootDir, r.file).split(path.sep).join('/');
    byFile[rel] = (byFile[rel] || 0) + 1;
  }
  console.log('=== ' + title + ' (threshold ' + THRESHOLD + ') ===');
  console.log('scanned methods: ' + results.length + ' across ' + Object.keys(byFile).length + ' files');
  console.log('violations (complexity > ' + THRESHOLD + '): ' + violations.length);
  const sorted = violations.slice().sort((a, b) => b.complexity - a.complexity);
  for (const v of sorted) {
    const rel = path.relative(rootDir, v.file).split(path.sep).join('/');
    console.log('  ' + rel + ':' + v.line + '  ' + v.name + '  complexity=' + v.complexity);
  }
  return { results, violations };
}

function runDefault(rootDir, jsonPath) {
  const { results, violations } = printReport(scanRoot(rootDir), rootDir, 'cognitive complexity report');
  if (jsonPath) fs.writeFileSync(jsonPath, JSON.stringify({ threshold: THRESHOLD, results }, null, 2), 'utf8');
  console.log('report mode: exit 0 always (CI report step, does not block PRs)');
  process.exitCode = 0;
}

function runUpdateBaseline(rootDir, jsonPath) {
  const results = scanRoot(rootDir);
  const baseline = buildBaseline(results, rootDir);
  fs.writeFileSync(BASELINE_PATH, JSON.stringify(baseline, null, 2) + '\n', 'utf8');
  console.log('baseline updated: ' + Object.keys(baseline.methods).length + ' methods, threshold ' + THRESHOLD);
  if (jsonPath) fs.writeFileSync(jsonPath, JSON.stringify({ threshold: THRESHOLD, results }, null, 2), 'utf8');
}

function runDemoGate(rootDir) {
  // Checkpoint C demonstration: a synthetic tree containing one over-complex NEW method.
  // Expected: baseline (src/) is green, the demo tree turns the gate RED, then --gate on
  // the same tree with --update-baseline turns it green again (grandfathering semantics).
  const demoDir = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'cc-demo-')), 'src');
  fs.mkdirSync(demoDir, { recursive: true });
  let s = 'class Demo {\n';
  s += '  int Existing_Grandfathered(bool a) { if (a) return 1; return 0; }\n';
  s += '  int Brand_New_OverComplex(int n) {\n';
  s += '    int x = 0;\n';
  for (let k = 0; k < 20; k++) s += '    if (n > ' + k + ') { x += ' + (k + 1) + '; }\n';
  s += '    return x;\n  }\n}\n';
  fs.writeFileSync(path.join(demoDir, 'Demo.cs'), s, 'utf8');
  console.log('--- demo step 1: --update-baseline on clean src/ then --gate on demo tree (expect RED, exit 1)');
  const baselineBefore = fs.existsSync(BASELINE_PATH);
  console.log('  (baseline present: ' + baselineBefore + ')');
  const results = scanRoot(demoDir);
  const demoBaseline = buildBaseline(results.filter(r => r.name !== 'Brand_New_OverComplex'), demoDir);
  const gate = gateCheck(results, demoBaseline, demoDir);
  console.log('  new violations detected: ' + gate.newViolations.length + ' -> ' + JSON.stringify(gate.newViolations));
  const wouldExit1 = gate.newViolations.length > 0;
  console.log('  gate would exit ' + (wouldExit1 ? '1 (RED: new violation blocked)' : '0'));
  console.log('--- demo step 2: same method committed to baseline (grandfathered) -> gate stays green');
  const grandfathered = buildBaseline(results, demoDir);
  const gate2 = gateCheck(results, grandfathered, demoDir);
  console.log('  new violations: ' + gate2.newViolations.length + ' -> gate exits 0 (grandfathered, scheduled not blocked)');
  console.log('--- demo step 3: complexity REGRESSION of a baseline method -> gate RED again');
  const worse = results.map(r => r.name === 'Existing_Grandfathered' ? { ...r, complexity: 20 } : r);
  const gate3 = gateCheck(worse, grandfathered, demoDir);
  console.log('  regression violations: ' + gate3.newViolations.length + ' -> ' + JSON.stringify(gate3.newViolations));
  fs.rmSync(path.dirname(demoDir), { recursive: true, force: true });
  console.log('demo-gate: ' + (wouldExit1 && gate2.newViolations.length === 0 && gate3.newViolations.length === 1 ? 'OK (red -> grandfathered-green -> regression-red)' : 'UNEXPECTED RESULT'));
  process.exitCode = wouldExit1 && gate2.newViolations.length === 0 && gate3.newViolations.length === 1 ? 0 : 1;
}

function main() {
  const argv = process.argv.slice(2);
  const jsonIdx = argv.indexOf('--json');
  const jsonPath = jsonIdx !== -1 ? argv[jsonIdx + 1] : null;
  const rootIdx = argv.indexOf('--root');
  const rootDir = rootIdx !== -1 ? path.resolve(argv[rootIdx + 1]) : SRC_DIR;
  if (argv.includes('--selftest')) return runSelftest();
  if (argv.includes('--demo-gate')) return runDemoGate(rootDir);
  if (argv.includes('--update-baseline')) return runUpdateBaseline(rootDir, jsonPath);
  if (argv.includes('--gate')) {
    if (!fs.existsSync(BASELINE_PATH)) {
      console.error('gate: baseline missing - run: node scripts/cognitive-complexity.mjs --update-baseline');
      process.exitCode = 2;
      return;
    }
    const baseline = JSON.parse(fs.readFileSync(BASELINE_PATH, 'utf8'));
    const results = scanRoot(rootDir);
    const { violations } = printReport(results, rootDir, 'cognitive complexity gate');
    const gate = gateCheck(results, baseline, rootDir);
    for (const [k, c] of gate.newViolations) console.log('NEW VIOLATION: ' + k + ' complexity=' + c);
    for (const [k, c, b] of gate.stillViolating) console.log('grandfathered: ' + k + ' complexity=' + c + ' (baseline ' + b + ')');
    for (const k of gate.improved) console.log('improved: ' + k);
    if (gate.newViolations.length > 0) {
      console.error('gate: ' + gate.newViolations.length + ' NEW violation(s) - commit blocked. Fix the method(s) or, only for intentional baseline growth, run --update-baseline with reviewer approval.');
      process.exitCode = 1;
    } else {
      console.log('gate: no new violations (exit 0)');
    }
    return;
  }
  runDefault(rootDir, jsonPath);
}

main();
