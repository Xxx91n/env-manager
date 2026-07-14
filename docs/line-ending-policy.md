## Line Ending Policy (canonical source: `.gitattributes`)

The repository enforces line endings via `.gitattributes` at the repo root. It
is the single source of truth; it overrides `core.autocrlf`, global git config,
and editor defaults.

- All text files use LF line endings on disk and in the index, except
  Windows-native shell scripts (`.bat`, `.ps1`, `.cmd`) which use CRLF.
- Binary assets (`.png`, `.ico`, `.icns`, `.jpg`, `.jpeg`, `.gif`, `.webp`,
  `.exe`, `.dll`, `.node`, `.pdb`, `.msi`, `.so`, `.dylib`) are marked `binary`
  in `.gitattributes` and are never normalized, never diffed.
- `core.autocrlf` is set to `false` at the repository level
  (`git config core.autocrlf false`). Do not re-enable it.
- `frontend/node_modules/` is gitignored and must NOT be tracked. If you find
  tracked node_modules files, remove them with `git rm -r --cached
  frontend/node_modules` (keeps disk files) and commit.
- After any change to `.gitattributes`, run `git add --renormalize .` so the
  index is re-normalized. Then commit the line-ending-only diff.

### Writing files in this repo

When writing or modifying source files on this machine, preserve the
repository convention:

- New files: LF for every text type except `.bat`/`.ps1`/`.cmd` (CRLF).
- `apply_patch` does byte-exact matching. If a patch fails for context that
  looks identical, suspect CRLF/LF mismatch on disk and re-inspect the target
  region before retrying. Do not blindly retry with altered context.
- Use UTF-8 without BOM for all text files.
- Never write a file with mixed line endings (both `\r\n` and lone `\n`).
  After a sequence of edits from multiple tools, if you suspect the working
  tree has drifted, normalize the specific file or run
  `git add --renormalize . ` then commit.
- When committing large multi-file changes that might mix in EOL drift, add a
  final `git add --renormalize . ` pass before the commit so the index is
  clean. CI must fail the build when `git diff --check` reports CRLF errors or
  mixed line endings on tracked text files.
