module Gen.Scaffold

open System
open System.IO

// ============================================================
// Helpers
// ============================================================

let ensureDir (path: string) =
    let dir = Path.GetDirectoryName(path)
    if dir <> "" && dir <> null && not (Directory.Exists dir) then
        Directory.CreateDirectory(dir) |> ignore

let writeFile (root: string) (relPath: string) (content: string) =
    let fullPath = Path.Combine(root, relPath)
    ensureDir fullPath
    File.WriteAllText(fullPath, content)
    printfn "  %s" relPath

/// Triple-quote placeholder — templates use TQTQ which gets replaced with """ at emit time
let private tq = "\"\"\""
let private fixTQ (s: string) = s.Replace("TQTQ", tq)

let toPascalCase (s: string) =
    s.Split([|'-'; '_'|], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun part -> string (Char.ToUpper part.[0]) + part.[1..])
    |> String.concat ""

// ============================================================
// Static files (100% generic, no parameterization)
// ============================================================

let nvmrc = "stable\n"

let private viteConfigTmpl = """import { defineConfig } from 'vite';
import { resolve } from 'path';

// -- Per-deployment configuration --
// Set at build time so one branch can produce every deployment's site:
//   BASE_PATH=/x SITE_TITLE=... SITE_SLUG=... npm run build
const basePath = (process.env.BASE_PATH || '').replace(/\/$/, '');
const siteTitle = process.env.SITE_TITLE || '{{TITLE}}';
const adminTitle = process.env.ADMIN_TITLE || '{{ADMIN_TITLE}}';
const siteLogo = process.env.SITE_LOGO || '';
// BCP-47 locale for date formatting (read via Hedge.Tenant). "" = viewer's own locale.
const siteLocale = process.env.SITE_LOCALE || '';
// Per-deployment CSS hook: adds `tenant-<slug>` to <body> so styles.css can scope rules.
const siteSlug = process.env.SITE_SLUG || '';
// Per-deployment feature flags (comma list) — read via Hedge.Tenant.hasFeature.
const siteFeatures = process.env.SITE_FEATURES || '';

/// Resolve the __BASE__ / __SITE_TITLE__ placeholders in the HTML entry points and
/// hand the client its runtime config on window (read by Hedge.Tenant).
function siteConfig() {
  return {
    name: 'hedge-site-config',
    // 'pre' so __BASE__ resolves before vite scans the HTML → vite bundles the theme CSS (and its
    // @imports, e.g. identity.css) into a hashed asset. One prescriptive CSS delivery across hedge.
    transformIndexHtml: {
    order: 'pre',
    handler(html, ctx) {
      const isAdmin = ctx.filename.endsWith('admin.html');
      const injected =
        `<script>window.BASE_PATH=${JSON.stringify(basePath)};` +
        `window.SITE_LOGO=${JSON.stringify(siteLogo)};` +
        `window.SITE_LOCALE=${JSON.stringify(siteLocale)};` +
        `window.SITE_SLUG=${JSON.stringify(siteSlug)};` +
        `window.SITE_TITLE=${JSON.stringify(siteTitle)};` +
        `window.SITE_FEATURES=${JSON.stringify(siteFeatures)};</script>`;
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${injected}`)
        // The deployment theme is for the public site only, never the shared admin tool.
        .replace('<body>', (siteSlug && !isAdmin) ? `<body class="tenant-${siteSlug}">` : '<body>');
      }
    }
  };
}

export default defineConfig({
  base: basePath + '/',
  plugins: [siteConfig()],
  build: {
    outDir: '_site' + basePath,
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        admin: resolve(__dirname, 'admin.html')
      }
    }
  },
  publicDir: false,
  server: {
    port: 3030,
    host: true,
    allowedHosts: true,
    watch: {
      ignored: ['!**/dist/**']
    },
    proxy: {
      '/api': {
        target: 'http://localhost:8787',
        changeOrigin: true,
        ws: true
      },
      '/blobs': {
        target: 'http://localhost:8787',
        changeOrigin: true
      }
    }
  }
});
"""

let viteConfig (appName: string) =
    viteConfigTmpl
        .Replace("{{TITLE}}", toPascalCase appName)
        .Replace("{{ADMIN_TITLE}}", toPascalCase appName + " Admin")

let workerEntryJs = """export { default } from "./dist/server/Worker.js";
export { EventHub } from "./dist/server/packages/hedge/src/Hedge/EventHub.js";
"""

let workerFs = """module Server.Worker

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Server.Env

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        Server.Routes.dispatch request (env :?> Env) ctx
    Admin = Some (fun request env route ->
        Hedge.Admin.handleRequest Server.AdminConfig.adminConfig request (env :?> Env) route)
    OAuth = None
    // No guest identity/comments by default; wire Server.GuestConfig.deps (and a GUEST_SECRET
    // binding) when the app adds guest commenting/uploads.
    GuestSession = None
    Mounts = []
    BlobServing = { PrivatePrefixes = [] }
    // No cron handler by default; set to Some to run a scheduled job (needs a [triggers] block).
    Scheduled = None
}
"""

let envFs = """module Server.Env

open Hedge.Workers

type Env = {
    DB: D1Database
    EVENTS: DurableObjectNamespace
    BLOBS: R2Bucket
    ADMIN_KEY: string
    ENVIRONMENT: string
}
"""

let adminConfigFs = """module Server.AdminConfig

open Hedge.Workers
open Hedge.Admin
open Server.Env

/// Everything admin-generic - the AdminTable type and the schema-driven CRUD -
/// lives in Hedge.Admin. This module just wires the app in: which tables (from
/// generated AdminGen), how to reach the D1 database, and how to authorise a
/// request against this app's admin key.
let adminConfig : AdminConfig<Env> =
    { Tables = Server.AdminGen.tables
      GetDb = fun env -> env.DB
      CheckKey = fun request env ->
        let key = getHeader request "X-Admin-Key"
        key <> "" && key = env.ADMIN_KEY }
"""


// ============================================================
// Parameterized files (app name substitution)
// ============================================================

// __BASE__ / __SITE_TITLE__ are resolved by vite's siteConfig plugin at build time
// (see viteConfig). Module scripts use an absolute "/..." path (vite rewrites them
// with `base`); the non-module guest-session tag + stylesheets use __BASE__ so they
// resolve under a sub-path mount. The rich-text bootstrap is a module (bundled by
// vite), so a fresh app's admin rich-text editing works out of the box.
let indexHtml = """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>__SITE_TITLE__</title>
    <link rel="stylesheet" href="__BASE__/public/styles.css">
    <script src="__BASE__/lib/guest-session.js"></script>
</head>
<body>
    <div id="app"></div>
    <script type="module" src="/lib/rich-text/bootstrap.js"></script>
    <script type="module" src="/dist/client/App.js"></script>
</body>
</html>
"""

let adminHtml = """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>__SITE_TITLE__</title>
    <link rel="stylesheet" href="__BASE__/admin.css">
    <link rel="stylesheet" href="__BASE__/public/styles.css">
</head>
<body>
    <div id="app"></div>
    <script>
      var m = window.location.hash.match(/key=([^&]*)/);
      if (m) {
        localStorage.setItem('adminKey', decodeURIComponent(m[1]));
        history.replaceState(null, '', window.location.pathname);
      }
    </script>
    <script type="module" src="/lib/rich-text/bootstrap.js"></script>
    <script type="module" src="/dist/admin/App.js"></script>
</body>
</html>
"""

let private packageJsonTmpl = """{
  "name": "{{APP}}",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "prep:lib": "mkdir -p lib/rich-text && cp ../../packages/rich-text/bootstrap.js ../../packages/rich-text/tiptap-editor.js ../../packages/rich-text/styles.css lib/rich-text/ && cp ../../packages/hedge/lib/guest-session.js lib/guest-session.js && cp ../../packages/content-client/identity.css public/identity.css",
    "predev": "npm run prep:lib",
    "dev": "concurrently -n client,server,gen -c blue,green,yellow \"npm run dev:client\" \"npm run dev:server\" \"npm run gen:watch\"",
    "dev:client": "concurrently -n fable,fable-admin,vite -c cyan,magenta,blue \"npm run fable:watch\" \"npm run fable:watch:admin\" \"vite\"",
    "dev:server": "concurrently -n fable-server,wrangler -c green,yellow \"npm run fable:watch:server\" \"wrangler dev\"",
    "build": "npm run build:client && npm run build:admin && npm run build:server && npm run build:site",
    "build:client": "dotnet fable src/Client/Client.fsproj -o dist/client",
    "build:admin": "dotnet fable ../../packages/hedge/src/Admin/Admin.fsproj -o dist/admin",
    "build:server": "dotnet fable src/Server/Server.fsproj -o dist/server",
    "build:site": "npm run prep:lib && vite build && mkdir -p _site${BASE_PATH}/public _site${BASE_PATH}/lib && cp -r public/* _site${BASE_PATH}/public/ && cp lib/guest-session.js _site${BASE_PATH}/lib/ && cp ../../packages/hedge/src/Admin/admin.css _site${BASE_PATH}/admin.css && { [ -z \"${BASE_PATH}\" ] || { cp _site${BASE_PATH}/index.html _site/index.html && printf '/ %s/ 302\\n' \"${BASE_PATH}\" > _site/_redirects; }; }",
    "deploy": "npm run build && wrangler deploy",
    "fable:watch": "dotnet fable watch src/Client/Client.fsproj -o dist/client",
    "fable:watch:server": "dotnet fable watch src/Server/Server.fsproj -o dist/server",
    "fable:watch:admin": "dotnet fable watch ../../packages/hedge/src/Admin/Admin.fsproj -o dist/admin",
    "gen": "dotnet run --project src/Gen/Gen.fsproj",
    "gen:watch": "dotnet watch run --project src/Gen/Gen.fsproj",
    "migrate": "dotnet run --project src/Gen/Gen.fsproj -- migrate",
    "migrate:dry": "dotnet run --project src/Gen/Gen.fsproj -- migrate --dry-run",
    "migrate:remote:dry": "dotnet run --project src/Gen/Gen.fsproj -- migrate --remote --dry-run",
    "migrate:remote": "dotnet run --project src/Gen/Gen.fsproj -- migrate --remote"
  },
  "devDependencies": {
    "@vitejs/plugin-react": "^4.3.0",
    "concurrently": "^8.2.2",
    "esbuild": "^0.24.0",
    "vite": "^5.4.0",
    "vite-plugin-fable": "0.0.31",
    "wrangler": "^4.67.0"
  },
  "dependencies": {
    "@tiptap/core": "^2.11.0",
    "@tiptap/extension-color": "^2.11.0",
    "@tiptap/extension-highlight": "^2.11.0",
    "@tiptap/extension-image": "^2.11.0",
    "@tiptap/extension-link": "^2.11.0",
    "@tiptap/extension-text-align": "^2.11.0",
    "@tiptap/extension-text-style": "^2.11.0",
    "@tiptap/pm": "^2.11.0",
    "@tiptap/starter-kit": "^2.11.0",
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  }
}
"""

let packageJson (appName: string) = packageJsonTmpl.Replace("{{APP}}", appName)

let private wranglerTomlTmpl = """name = "{{APP}}"
main = "worker-entry.js"
compatibility_date = "2024-01-01"
compatibility_flags = ["nodejs_compat"]

# Local development
[dev]
port = 8787
local_protocol = "http"

# D1 Database binding
[[d1_databases]]
binding = "DB"
database_name = "{{APP}}-db"
database_id = "local"  # Replace with real ID after `wrangler d1 create {{APP}}-db`

# Durable Objects
[durable_objects]
bindings = [
    { name = "EVENTS", class_name = "EventHub" }
]

[[migrations]]
tag = "v1"
new_classes = ["EventHub"]

# R2 blob storage
[[r2_buckets]]
binding = "BLOBS"
bucket_name = "{{APP}}-blobs"

# Static assets
[assets]
directory = "./_site"
binding = "ASSETS"
html_handling = "auto-trailing-slash"
not_found_handling = "single-page-application"

# Environment variables
[vars]
ENVIRONMENT = "development"
ADMIN_KEY = "dev-admin-key"
"""

let wranglerToml (appName: string) = wranglerTomlTmpl.Replace("{{APP}}", appName)

// ============================================================
// Starter content (minimal working example)
// ============================================================

let domainFs = """module Models.Domain

open Hedge.Interface

type Post = {
    Id: PrimaryKey<string>
    Title: string
    Body: string
    CreatedAt: CreateTimestamp
}
"""

let apiFs = """module Models.Api

open Hedge.Interface

module GetPosts =
    type PostItem = {
        Id: string
        Title: string
        Body: string
        Timestamp: int
    }

    type Response = {
        Posts: PostItem list
    }

    let endpoint : Get<Response> = Get "/api/posts"

module CreatePost =
    type Request = {
        Title: string
        Body: string
    }

    type Response = {
        Post: GetPosts.PostItem
    }

    let endpoint : Post<Request, Response> = Post "/api/post"
"""

let wsFs = """module Models.Ws

/// WebSocket event payloads.
/// Add event types here as needed.
"""


let private clientAppTmpl = """module Client.App

open Feliz
open Elmish

type Model = {
    Loading: bool
    Error: string option
}

type Msg =
    | NoOp

let init () =
    { Loading = false; Error = None }, Cmd.none

let update msg model =
    match msg with
    | NoOp -> model, Cmd.none

let view model dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            Html.h1 "{{NAME}}"
            match model.Error with
            | Some err ->
                Html.div [ prop.className "error"; prop.text err ]
            | None -> Html.none
            Html.p "Edit src/Client/App.fs to get started."
        ]
    ]

open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
"""

let clientAppFs (appName: string) = clientAppTmpl.Replace("{{NAME}}", toPascalCase appName)

let stylesCss = """@import "./identity.css";

/* Base styles */
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: system-ui, -apple-system, sans-serif; line-height: 1.5; }
.app { max-width: 800px; margin: 0 auto; padding: 1rem; }
header { border-bottom: 1px solid #eee; padding-bottom: 1rem; margin-bottom: 1rem; }
h1 { font-size: 1.5rem; }
.loading { color: #666; }
.error { background: #fee; border: 1px solid #fcc; padding: 0.5rem; border-radius: 4px; margin-bottom: 1rem; }
.feed-item { border-bottom: 1px solid #eee; padding: 1rem 0; }
.feed-item h2 { font-size: 1.1rem; margin-bottom: 0.25rem; }
"""

// ============================================================
// fsproj templates
// ============================================================

let genFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../../../packages/hedge/src/Gen/Program.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Models/Models.fsproj" />
  </ItemGroup>
</Project>
"""

let modelsFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Domain.fs" />
    <Compile Include="Ws.fs" />
    <Compile Include="Api.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
  </ItemGroup>
</Project>
"""

let codecsFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="generated/Codecs.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
    <ProjectReference Include="../Models/Models.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
    <PackageReference Include="Thoth.Json" Version="10.2.0" />
  </ItemGroup>
</Project>
"""

let serverFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Env.fs" />
    <Compile Include="generated/Db.fs" />
    <Compile Include="Handlers.fs" />
    <Compile Include="generated/AdminGen.fs" />
    <Compile Include="AdminConfig.fs" />
    <Compile Include="generated/Routes.fs" />
    <Compile Include="Worker.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
    <ProjectReference Include="../Models/Models.fsproj" />
    <ProjectReference Include="../Codecs/Codecs.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
    <PackageReference Include="Fable.Promise" Version="3.2.0" />
    <PackageReference Include="Thoth.Json" Version="10.2.0" />
  </ItemGroup>
</Project>
"""

let clientFsproj = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <!-- Shared client infra (one source for the estate); do not copy locally. -->
    <Compile Include="../../../../packages/hedge/src/Client/GuestSession.fs" />
    <Compile Include="../../../../packages/hedge/src/Client/Api.fs" />
    <Compile Include="generated/ClientGen.fs" />
    <Compile Include="../../../../packages/rich-text/RichText.fs" />
    <Compile Include="App.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../packages/hedge/src/Hedge/Hedge.fsproj" />
    <ProjectReference Include="../Models/Models.fsproj" />
    <ProjectReference Include="../Codecs/Codecs.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.3.0" />
    <PackageReference Include="Fable.Browser.Dom" Version="2.16.0" />
    <PackageReference Include="Feliz" Version="2.9.0" />
    <PackageReference Include="Feliz.Router" Version="4.0.0" />
    <PackageReference Include="Fable.Elmish" Version="4.2.0" />
    <PackageReference Include="Fable.Elmish.React" Version="4.0.0" />
    <PackageReference Include="Fable.Elmish.HMR" Version="7.0.0" />
    <PackageReference Include="Thoth.Fetch" Version="3.0.1" />
  </ItemGroup>
</Project>
"""

// ============================================================
// Main — scaffold command
// ============================================================

[<EntryPoint>]
let main (argv: string array) =
    if argv.Length = 0 then
        printfn "Usage: dotnet run --project packages/hedge/src/Gen/Scaffold.fsproj -- <app-name>"
        printfn ""
        printfn "Creates apps/<app-name>/ with a working Hedge app skeleton."
        1
    else
        let appName = argv.[0]
        let root = Path.Combine("apps", appName)

        if Directory.Exists root then
            printfn "ERROR: %s already exists" root
            1
        else
            printfn "Scaffolding %s..." root

            // Static files. Client HTTP helpers (Client.Api), the guest-session
            // accessor (Client.GuestSession) + its window.HedgeGuest runtime
            // (lib/guest-session.js) are shared — pulled from packages/hedge by the
            // Client.fsproj + `prep:lib`, never copied into the app.
            writeFile root ".nvmrc" nvmrc
            writeFile root "vite.config.js" (viteConfig appName)
            writeFile root "worker-entry.js" workerEntryJs

            // Server static
            writeFile root "src/Server/Worker.fs" workerFs
            writeFile root "src/Server/Env.fs" envFs
            writeFile root "src/Server/AdminConfig.fs" (fixTQ adminConfigFs)

            // Parameterized files
            writeFile root "index.html" indexHtml
            writeFile root "admin.html" adminHtml
            writeFile root "package.json" (packageJson appName)
            writeFile root "wrangler.toml" (wranglerToml appName)

            // Starter content
            writeFile root "src/Models/Domain.fs" domainFs
            writeFile root "src/Models/Api.fs" apiFs
            writeFile root "src/Models/Ws.fs" wsFs
            // Handlers.fs is generated by `npm run gen` on first run
            writeFile root "src/Client/App.fs" (clientAppFs appName)
            writeFile root "public/styles.css" stylesCss

            // fsproj templates
            writeFile root "src/Gen/Gen.fsproj" genFsproj
            writeFile root "src/Models/Models.fsproj" modelsFsproj
            writeFile root "src/Codecs/Codecs.fsproj" codecsFsproj
            writeFile root "src/Server/Server.fsproj" serverFsproj
            writeFile root "src/Client/Client.fsproj" clientFsproj

            // Empty generated dirs
            Directory.CreateDirectory(Path.Combine(root, "src/Server/generated")) |> ignore
            Directory.CreateDirectory(Path.Combine(root, "src/Client/generated")) |> ignore
            Directory.CreateDirectory(Path.Combine(root, "src/Codecs/generated")) |> ignore
            Directory.CreateDirectory(Path.Combine(root, "migrations")) |> ignore

            printfn ""
            printfn "Done! Next steps:"
            printfn "  cd %s" root
            printfn "  npm install"
            printfn "  npm run gen"
            printfn "  npm run dev"

            0
