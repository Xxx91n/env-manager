# Env Manager - Architecture & Technical Details

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Env Manager System                       │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────┐           ┌──────────────────────┐
│   GUI Layer         │           │   CLI Layer          │
│  (Desktop App)      │           │  (Command Line)      │
├─────────────────────┤           ├──────────────────────┤
│  Tauri Window       │◄──IPC────►│  env-manager.exe     │
│  (Rust Runtime)     │  (Rust)   │  (C# .NET 10)        │
├─────────────────────┤           ├──────────────────────┤
│  WebView (Edge)     │           │  CLI Handler         │
│  HTML/CSS/JS        │           │  (215 lines C#)      │
├─────────────────────┤           ├──────────────────────┤
│  Svelte App         │           │  Registry API        │
│  TypeScript         │           │  (Windows Native)    │
├─────────────────────┤           ├──────────────────────┤
│  dist/index.html    │           │  JSON Backup Format  │
│  (42KB compressed)  │           │  (Portable, Git OK)  │
└─────────────────────┘           └──────────────────────┘
```

## GUI Detailed Architecture

### NOT a Browser App

```
❌ What it's NOT:
  - Not running in Chrome/Firefox/Edge browser
  - Not a web server (no localhost:port)
  - Not a PWA (Progressive Web App)
  - Not Electron (which bundles Chromium)

✅ What it IS:
  - Native Windows application (Tauri desktop wrapper)
  - Embedded WebView (uses OS Edge WebView2)
  - Real executable binary (.exe or MSI installer)
  - Direct Windows API access (via Rust)
```

### Tauri Runtime Flow

```
User double-clicks env-manager.exe (or MSI shortcut)
  ↓
Tauri Rust runtime initializes
  ↓
WebView window created (embedded, not separate browser)
  ↓
Loads dist/index.html + assets (from embedded bundle)
  ↓
Svelte app runs in WebView JavaScript context
  ↓
User interacts with UI (search, filter, edit, delete)
  ↓
Svelte triggers TypeScript API call
  ↓
TypeScript calls invoke('run_cli', {...})
  ↓
Tauri IPC bridge transfers command to Rust backend
  ↓
Rust backend spawns env-manager.exe subprocess
  ↓
CLI reads Windows Registry, outputs JSON/table
  ↓
Rust backend captures output, sends back to TypeScript
  ↓
Svelte store updates, UI re-renders
  ↓
User sees result (all in <200ms)
```

### Why Not a Browser?

**Performance + Security + Native Integration**:

| Aspect | Tauri (Native) | Browser App | Electron |
|--------|---|---|---|
| Memory | 40MB | 100MB+ | 200MB+ |
| Startup | <2s | 5-10s | 3-5s |
| API Access | Full (Registry, Files) | Limited (sandboxed) | Partial (Node.js) |
| Deployment | Single .exe or MSI | Network + framework | Massive bundle |
| Offline | Works | Requires service worker | Works |

---

## CLI Architecture (Backend)

### Single File Implementation

```
Program.cs (215 lines)
├── Main() - Entry point, command routing
├── List() - Read HKEY_CURRENT_USER\Environment
├── Get(name) - Single variable retrieval
├── Set(name, value, scope) - Write to Registry
├── Delete(name, scope) - Remove Registry key
├── Backup(file) - Export to JSON with timestamp
├── Restore(file, scope) - Import from JSON
├── Diff(old, new) - Compare two backups
├── Merge(old, new) - Combine backups
└── Validate(file) - Verify JSON format
```

### Registry Operations

```csharp
// User scope (no admin required)
HKEY_CURRENT_USER\Environment

// System scope (requires admin)
HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Session Manager\Environment
```

### Data Flow

```
CLI Input (command + args)
  ↓
Registry Read/Write via Microsoft.Win32.Registry
  ↓
Parse & format output
  ↓
Stdout (table format) or JSON
  ↓
Tauri captures stdout
  ↓
Frontend parses table/JSON
  ↓
UI renders result
```

---

## GUI-CLI Communication (IPC)

### Tauri Command Handler

```rust
#[tauri::command]
fn run_cli(command: String, args: Vec<String>) -> CliResponse {
    // 1. Locate CLI executable
    // 2. Build command with arguments
    // 3. Spawn subprocess
    // 4. Capture stdout/stderr
    // 5. Parse output
    // 6. Return JSON response
}
```

### TypeScript Caller

```typescript
async function listVariables() {
  const result = await invoke('run_cli', {
    command: 'list',
    args: []
  })
  // Parse result, update store
}
```

### Response Format

```json
{
  "success": true,
  "data": "table output or JSON string",
  "error": null
}
```

---

## Component Structure (Frontend)

```
frontend/
├── src/
│   ├── App.svelte                    ← Root component
│   │   └── Renders header + main content
│   │
│   └── lib/
│       ├── api.ts                    ← API layer (invoke wrapper)
│       │   ├── listVariables()
│       │   ├── setVariable()
│       │   ├── deleteVariable()
│       │   └── createBackup()
│       │
│       ├── stores.ts                 ← Reactive state (Svelte stores)
│       │   ├── variables (list)
│       │   ├── selectedScope (user|system|all)
│       │   ├── loading (boolean)
│       │   └── error (string|null)
│       │
│       └── components/
│           ├── Variables.svelte      ← Main table component
│           │   └── Search, filter, actions
│           │
│           ├── EditDialog.svelte     ← Create/edit modal
│           │   └── Name, value, scope inputs
│           │
│           └── BackupDialog.svelte   ← Backup/restore modal
│               └── File upload/download
└── src-tauri/
    └── src/main.rs                   ← Tauri command handler
```

---

## State Management (Svelte Stores)

```typescript
// stores.ts
export const variables = writable([])
export const selectedScope = writable('all')
export const loading = writable(false)
export const error = writable(null)

// Reactive computed value
$: filteredVars = $variables.filter(v => {
  if ($selectedScope !== 'all' && v.scope !== $selectedScope) return false
  if (search && !v.name.includes(search)) return false
  return true
})

// In component: {#each filteredVars as var}...{/each}
// Automatically re-renders when $variables or $selectedScope changes
```

---

## Build & Deployment Modes

### Development Mode

```bash
npm run tauri-dev
```

**What happens**:
1. Vite dev server starts on http://localhost:5173 (for hot reload)
2. Tauri opens dev window
3. WebView loads from localhost (enables hot reload)
4. Rust code runs with debug symbols
5. Open DevTools with F12

**Use case**: Active development, testing

**Features**:
- Hot reload on file changes
- DevTools available (F12)
- Unminified code (easier debugging)

### Production Build

```bash
npm run tauri-build
```

**What happens**:
1. Vite builds dist/ (minified, optimized)
2. Tauri compiles Rust in release mode
3. Creates dist/index.html + assets
4. Bundles everything into exe + MSI
5. Output: `frontend/src-tauri/target/release/bundle/msi/*.msi`

**Use case**: Release to users

**Features**:
- Minified + optimized
- Single .msi file (~40MB)
- Auto-update capable
- No DevTools in release

---

## File Locations After Build

```
env-manager/
├── bin/Release/net10.0/
│   └── env-manager.exe              ← CLI binary (158KB)
│
├── dist/
│   ├── index.html
│   └── assets/
│       ├── index-*.css (11KB)
│       └── index-*.js (22KB)
│
└── frontend/src-tauri/target/release/
    └── bundle/msi/
        └── env-manager-0.3.0.msi    ← Installer (40MB)
```

---

## Why Each Technology Was Chosen

| Component | Technology | Why |
|-----------|-----------|-----|
| **Desktop Wrapper** | Tauri | Lightweight, native, secure, Rust-based |
| **Frontend UI** | Svelte | Tiny compiled size, reactive, fast |
| **Frontend Language** | TypeScript | Type safety, excellent IDE support |
| **Styling** | TailwindCSS | Utility-first, minimal CSS bundle |
| **CLI** | C# .NET 10 | Native Registry API, fast, single executable |
| **Backend Language** | Rust (Tauri) | Memory-safe, zero-cost abstractions, Windows API |

---

## Performance Characteristics

| Operation | Time | Bottleneck |
|-----------|------|-----------|
| List 40 vars | ~200ms | Registry read |
| Search filter | <50ms | DOM update |
| Add variable | ~300ms | Registry write + reload |
| Delete variable | ~300ms | Registry delete + reload |
| Backup create | ~800ms | File I/O |
| GUI startup | ~2s | Tauri init + WebView load |

---

## Security Model

### No Remote Communication
- ✅ All operations local
- ✅ No cloud sync
- ✅ No telemetry
- ✅ No internet access

### IPC Security
- Tauri command invocation is type-safe
- Rust backend validates all input
- CLI sub-process has same privileges as parent

### Registry Access
- User scope: Always accessible
- System scope: Requires elevation prompt
- No cross-account access

---

## Testing the Architecture

### Test 1: CLI Only (No GUI)
```bash
./bin/Release/net10.0/env-manager.exe list
./bin/Release/net10.0/env-manager.exe backup --output test.json
```
**Purpose**: Verify backend works independently

### Test 2: GUI with Dev Mode
```bash
npm run tauri-dev
# Then in GUI: search, edit, delete variables
# Watch browser DevTools console for errors
```
**Purpose**: Verify GUI-CLI communication

### Test 3: GUI Build (MSI)
```bash
npm run tauri-build
# Install MSI
# Launch from Start Menu
# Verify all operations work
```
**Purpose**: Verify production package

### Test 4: Direct HTML (Should Fail)
```bash
start ./dist/index.html
# → Blank page (no Tauri API available)
```
**Purpose**: Confirm why HTML alone doesn't work

---

## Summary

| Aspect | Detail |
|--------|--------|
| **Is it a web app?** | No - it's a native Tauri app with embedded WebView |
| **Do I need localhost?** | No - dev mode uses localhost only, production is bundled |
| **Do I package MSI every test?** | No - use `npm run tauri-dev` for development |
| **Can I open dist/index.html?** | No - requires Tauri runtime and IPC bridge |
| **How do I test GUI?** | Run `npm run tauri-dev` in frontend/ directory |
| **How do I release?** | Run `npm run tauri-build` to generate MSI |
| **Is CLI independent?** | Yes - can use env-manager.exe without GUI |
| **Can GUI work without CLI?** | No - all operations go through CLI subprocess |
