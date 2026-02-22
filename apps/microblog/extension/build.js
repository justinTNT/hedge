/**
 * Hedge Extension — Build Script
 *
 * Bundles popup.js (with TipTap npm deps) into extension/dist/ via esbuild.
 * Copies static files (manifest, popup.html, background.js) alongside.
 */

import * as esbuild from 'esbuild'
import { copyFileSync, mkdirSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const dist = resolve(__dirname, 'dist')

mkdirSync(dist, { recursive: true })

// Bundle popup.js with TipTap dependencies
await esbuild.build({
  entryPoints: [resolve(__dirname, 'popup.js')],
  bundle: true,
  outdir: dist,
  format: 'iife',
  target: 'chrome120',
  minify: false,
  sourcemap: false,
})

// Copy static files
const statics = ['manifest.json', 'popup.html', 'background.js']
for (const file of statics) {
  copyFileSync(resolve(__dirname, file), resolve(dist, file))
}

console.log('Extension built → extension/dist/')
