# Demo asset pipeline (simulated terminal)

`docs/assets/demo.gif` is generated from a **simulated** terminal screenplay — no real CLI,
registry, or machine data is involved. This replaced the old `vhs` tape workflow, which
recorded a real binary against the live registry.

| File | Role |
|------|------|
| `script.json` | Screenplay constants — commands, canned outputs, timings. All paths (`C:\demo\…`) and the version string are fictional demo data; review changes here. |
| `terminal.html` | Terminal renderer + typing engine. Reads `window.__DEMO_SCRIPT__` injected by the capture script. |
| `capture.mjs` | Playwright `recordVideo` (1280x720) → ffmpeg two-pass palette gif → `docs/assets/demo.gif` (≤5 MB target). |

## Regenerate

```
node scripts/demo/capture.mjs
```

Requires Node.js, the pinned Playwright chromium headless shell (`frontend/package.json`
`@playwright/test`, `chromium_headless_shell-1243` under `%LOCALAPPDATA%\ms-playwright`;
override with `PW_SHELL`), and `ffmpeg` + `ffprobe` on PATH. Output path override:
`--out <path>` or `DEMO_GIF_OUT`.
