// Loader for the CP-A reorder fixtures (test.sh Step 1j). Copied next to the Fable-compiled
// Program.js (under apps/articles/, so bare `react` imports resolve from its node_modules) and
// run with node. The update path reads no browser globals at load, but stub the few a stray
// reference could touch so a regression surfaces as an assertion failure, not a ReferenceError.
globalThis.window = { BASE_PATH: '', MOUNT_BASE: '', SITE_TITLE: '' };
globalThis.document = { title: '' };
globalThis.WebSocket = class {};
await import('./Program.js');
