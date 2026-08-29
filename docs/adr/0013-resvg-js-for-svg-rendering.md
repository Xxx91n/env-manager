# ADR 0013: Use @resvg/resvg-js as Committed npm devDependency for SVG Rendering

Date: 2026-08-29
Status: Accepted

## Context

The README hero.gif rendering pipeline (`render_motion_gif.py` from the
beautify-github-readme skill) requires an `rsvg-convert` binary to convert
SVG layers to PNG frames. On Windows, native `rsvg-convert` has known
defects:

- stdout binary corruption (librsvg#676, #812; msys2#9906)
- no official winget package (pandoc docs confirm only choco path)
- MSYS2/choco installation introduces version drift in CI

The previous workaround placed a `.cmd` wrapper + `@resvg/resvg-js` in
`.codex-tmp/` (gitignored, ephemeral, not reproducible across clones or CI).

## Decision

Use `@resvg/resvg-js@2.6.2` as a committed npm devDependency in
`package.json`, with a `scripts/rsvg-convert.js` CLI wrapper and
`scripts/rsvg-convert.cmd` Windows shim. The wrapper mimics the
`rsvg-convert <input.svg> -o <output.png>` CLI interface that
`render_motion_gif.py` discovers via `shutil.which("rsvg-convert")`.

### Rationale (atomcode research, 13 sources, high confidence)

- `@resvg/resvg-js` is a napi-rs prebuilt native module: zero system
dependencies, no postinstall, no node-gyp. Platform optionalDependencies
auto-select at install time.
- resvg core guarantees pixel-level cross-platform reproducibility
(x86 Windows and ARM macOS render identically).
- The project already follows a prebuilt-native-module convention
(better-sqlite3, onnxruntime-node). resvg-js is the same pattern.
- resvg does NOT support SMIL/CSS animation. This is mitigated by the
existing `hero-motion.json` per-frame composition architecture — resvg-js
only replaces the per-frame rasterization step, not the animation pipeline.

### Avoided

- Installing native `rsvg-convert` via winget/MSYS2/choco on Windows CI.
- Using `sharp` (depends on libvips internal librsvg availability, which
breaks in hardened environments — 2026-08 production incident report).
- Using Python `cairosvg` (hard Cairo native dependency on Windows).

## Consequences

- `npm install` provides the SVG renderer. No system binary needed.
- `scripts/` must be on PATH when running `render_motion_gif.py`.
- hero.gif is a committed artifact; re-rendering is a dev-only operation,
not required in CI.
- If future animation source changes to SMIL/CSS animated SVG, the only
fidelity path is Chromium (Playwright); resvg-js would be replaced then.
