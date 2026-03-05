/**
 * Hedge Extension — Build Script
 *
 * 1. Fable-compiles Extension.fsproj to JS
 * 2. Bundles the Fable output (with TipTap npm deps) into extension/dist/ via esbuild
 * 3. Copies static files (manifest, popup.html, background.js) alongside
 */

import { execSync } from 'child_process'
import * as esbuild from 'esbuild'
import { copyFileSync, mkdirSync, existsSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const dist = resolve(__dirname, 'dist')
const fableOut = resolve(__dirname, 'fable_output')

mkdirSync(dist, { recursive: true })

// Step 1: Fable compile F# to JS
console.log('Fable compiling Extension.fsproj...')
execSync('dotnet fable Extension.fsproj -o fable_output', {
  cwd: __dirname,
  stdio: 'inherit',
})

// Step 2: Bundle Fable output with TipTap dependencies
await esbuild.build({
  entryPoints: [resolve(fableOut, 'Popup.js')],
  bundle: true,
  outfile: resolve(dist, 'popup.js'),
  format: 'iife',
  target: 'chrome120',
  minify: false,
  sourcemap: false,
})

// Step 3: Copy static files
const statics = ['manifest.json', 'popup.html', 'background.js']
for (const file of statics) {
  copyFileSync(resolve(__dirname, file), resolve(dist, file))
}

// Copy sites.json if present (gitignored, developer-specific)
const sitesJson = resolve(__dirname, 'sites.json')
if (existsSync(sitesJson)) {
  copyFileSync(sitesJson, resolve(dist, 'sites.json'))
}

console.log('Extension built → extension/dist/')
