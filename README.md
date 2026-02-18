# Hedge: Hamlet on Edge

F# full-stack on Cloudflare Workers. Shared types, shared codecs, one language.

## Architecture

```
src/
├── Shared/          # Domain types + Thoth encoders/decoders
│   ├── Domain.fs    # User, Comment, Item types
│   ├── Codecs.fs    # JSON encode/decode (the contract)
│   └── Api.fs       # Request/Response types, route constants
├── Client/          # Fable → JS (Feliz + Elmish)
│   ├── Api.fs       # Typed fetch wrappers
│   └── App.fs       # Elmish app (Model, Update, View)
└── Server/          # Fable → JS (Cloudflare Workers)
    ├── Env.fs       # D1, KV bindings
    ├── Router.fs    # Minimal pattern-matching router
    ├── Handlers.fs  # API handlers using Shared.Codecs
    └── Worker.fs    # Workers entry point
```

## Quick Start

```bash
# Install tools
dotnet tool restore
npm install

# Development (client + server concurrently)
npm run dev

# Or separately:
npm run dev:client   # Vite on :3000
npm run dev:server   # Wrangler on :8787
```

## Spikes

### Spike B: Reflection Test

Verify custom attributes survive Fable compilation:

```bash
npm run spike:reflection
```

Expected output:
```
SUCCESS: Field 'Name' has label: 'User Name'
SUCCESS: Field 'Email' has label: 'User Email'
VERDICT: PASS - Custom Attributes are preserved.
```

## Stack

| Layer | Library | Purpose |
|-------|---------|---------|
| Shared | Thoth.Json | Type-safe JSON codecs |
| Client | Feliz + Elmish | React with F# Elm architecture |
| Server | Fable → Workers | Edge functions with D1/KV |
| Build | Vite + vite-plugin-fable | HMR for both client and server |

## Why Thoth + Fetch instead of Fable.Remoting?

Fable.Remoting's server requires .NET CLR - it doesn't compile to JS.
For Workers, we need pure Fable → JS on both ends.
Thoth + Fetch gives us the same type-safe serialization without the runtime dependency.
Trade-off: ~50 extra lines per app (manual fetch wrappers). Worth it.
