// Fixed dataset for the screenshot baseline. Shapes mirror the real CLI JSON
// (see src/lib/api.ts): run_cli answers are { success, data: '<JSON string>', error }.
// Timestamps are frozen so re-shoots on the same machine render identically.

export type MockSettings = Record<string, string>

export const mockDataset = {
  version: '0.14.0',
  diagnostics: {
    resolved_cli_path: 'C:\\Program Files\\EnvManager\\env-manager.exe',
    gui_exe_dir: 'C:\\Program Files\\EnvManager',
    cwd: 'C:\\Users\\dev',
  },
  variables: [
    { name: 'JAVA_HOME', value: 'C:\\Program Files\\Java\\jdk-21', scope: 'user', isProtected: true },
    { name: 'NODE_OPTIONS', value: '--max-old-space-size=4096', scope: 'user' },
    { name: 'PYTHONPATH', value: 'C:\\dev\\libs', scope: 'user' },
    { name: 'GOPATH', value: 'C:\\Users\\dev\\go', scope: 'user' },
    { name: 'RUST_LOG', value: 'info', scope: 'user' },
    { name: 'API_BASE_URL', value: 'https://api.internal.example.com', scope: 'user', profileSource: 'dev-tools' },
    { name: 'EDITOR', value: 'code --wait', scope: 'user', isDisabled: true },
    { name: 'TEMP', value: 'C:\\Users\\dev\\AppData\\Local\\Temp', scope: 'user', isBuiltinProtected: true },
    { name: 'DOTNET_CLI_TELEMETRY_OPTOUT', value: '1', scope: 'system' },
    { name: 'JAVA_TOOL_OPTIONS', value: '-Dfile.encoding=UTF-8', scope: 'system' },
  ],
  pathUser: [
    { index: 0, path: 'C:\\Users\\dev\\.cargo\\bin', expandedPath: 'C:\\Users\\dev\\.cargo\\bin', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: false },
    { index: 1, path: 'C:\\Program Files\\nodejs', expandedPath: 'C:\\Program Files\\nodejs', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: false },
    { index: 2, path: 'C:\\Tools\\bin', expandedPath: 'C:\\Tools\\bin', isDuplicate: false, exists: true, isProtected: true, isBuiltinProtected: false },
    { index: 3, path: '%JAVA_HOME%\\bin', expandedPath: 'C:\\Program Files\\Java\\jdk-21\\bin', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: false },
    { index: 4, path: 'C:\\dev\\old-tools', expandedPath: 'C:\\dev\\old-tools', isDuplicate: false, exists: false, isProtected: false, isBuiltinProtected: false },
    { index: 5, path: 'C:\\Program Files\\nodejs', expandedPath: 'C:\\Program Files\\nodejs', isDuplicate: true, exists: true, isProtected: false, isBuiltinProtected: false },
  ],
  pathSystem: [
    { index: 0, path: 'C:\\Windows\\system32', expandedPath: 'C:\\Windows\\system32', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: true },
    { index: 1, path: 'C:\\Windows', expandedPath: 'C:\\Windows', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: true },
    { index: 2, path: 'C:\\Windows\\System32\\Wbem', expandedPath: 'C:\\Windows\\System32\\Wbem', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: false },
    { index: 3, path: 'C:\\Program Files\\dotnet', expandedPath: 'C:\\Program Files\\dotnet', isDuplicate: false, exists: true, isProtected: false, isBuiltinProtected: false },
  ],
  profiles: [
    {
      id: 'dev-tools', name: 'dev-tools', isEnabled: true, appliedAt: 1763000000000,
      inherits: [], pathEntries: ['C:\\Tools\\bin'], pathScopes: ['user'],
      variables: [{ name: 'API_BASE_URL', value: 'https://api.internal.example.com', scope: 'user' }],
      profileType: 'global',
    },
    {
      id: 'staging', name: 'staging', isEnabled: false, appliedAt: null,
      inherits: ['dev-tools'], pathEntries: [],
      variables: [{ name: 'API_BASE_URL', value: 'https://staging.internal.example.com', scope: 'user' }],
      profileType: 'global',
    },
  ],
  history: [
    { id: 'h-006', timestamp: '2026-11-12T09:15:00Z', command: 'set', name: 'NODE_OPTIONS', scope: 'user', oldValue: null, newValue: '--max-old-space-size=4096' },
    { id: 'h-005', timestamp: '2026-11-12T09:14:00Z', command: 'set', name: 'API_BASE_URL', scope: 'user', oldValue: 'https://old.internal.example.com', newValue: 'https://api.internal.example.com' },
    { id: 'h-004', timestamp: '2026-11-11T16:40:00Z', command: 'delete', name: 'LEGACY_FLAG', scope: 'user', oldValue: '1', newValue: null },
    { id: 'h-003', timestamp: '2026-11-11T16:32:00Z', command: 'rename', name: 'JAVA_HOME', scope: 'user', oldValue: 'JDK_HOME', newValue: 'JAVA_HOME' },
    { id: 'h-002', timestamp: '2026-11-10T11:05:00Z', command: 'set', name: 'DOTNET_CLI_TELEMETRY_OPTOUT', scope: 'system', oldValue: null, newValue: '1' },
    { id: 'h-001', timestamp: '2026-11-10T11:00:00Z', command: 'set', name: 'JAVA_TOOL_OPTIONS', scope: 'system', oldValue: null, newValue: '-Dfile.encoding=UTF-8' },
  ],
  protection: {
    protectedVars: { builtIn: ['PATH', 'PATHEXT', 'TEMP', 'TMP'], custom: ['JAVA_HOME'] },
    protectedPaths: { builtIn: ['C:\\Windows\\system32', 'C:\\Windows'], custom: ['C:\\Tools\\bin'] },
  },
  serviceStatus: {
    ok: true,
    data: {
      running: true,
      mode: 'background',
      mountsAvailable: true,
      mountPath: 'C:\\Users\\dev\\AppData\\Local\\EnvManager\\mount.json',
    },
  },
  serviceHealth: {
    ok: true,
    data: {
      mounts: [
        { id: 'm-001', name: 'api-token', provider: 'dpapi-current-user', state: 'valid', refreshPolicy: 'CreatedOnly', lastFetchedAt: '2026-11-13T08:00:00Z' },
        { id: 'm-002', name: 'db-password', provider: 'sops', state: 'unverified', reason: 'noExpiry', refreshPolicy: 'Periodic', lastFetchedAt: '2026-11-13T08:05:00Z' },
      ],
    },
  },
  servicePing: { ok: true, data: { pong: true } },
  // The real `audit list` returns a JSON string, and AuditPage only accepts an
  // array or { entries } — so on v3 the page renders its empty state even with
  // records present. The baseline keeps that behavior instead of faking a shape
  // the app never sees.
  audit: [
    { id: 'a-002', timestamp: '2026-11-13T08:05:00Z', command: 'mount.rotate', name: 'db-password', scope: 'user', oldValue: null, newValue: null },
    { id: 'a-001', timestamp: '2026-11-13T08:00:00Z', command: 'mount.create', name: 'api-token', scope: 'user', oldValue: null, newValue: null },
  ],
  notes: {
    version: 1,
    notes: {
      JAVA_HOME: { key: 'JAVA_HOME', note: 'JDK 21 LTS, required by the build scripts', updatedAt: '2026-11-10T10:00:00Z', pinned: true },
    },
  },
  providerList: [
    'Active provider: dpapi-current-user',
    'Available providers:',
    '  dpapi-current-user (active)',
    '  credential-manager',
    '  powershell-secretmanagement',
    '  sops (unavailable: network)',
    '  vault-kv2 (unavailable: auth)',
    '',
  ].join('\n'),
}
