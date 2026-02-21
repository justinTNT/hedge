# Hedge

F# full-stack web framework compiled to JavaScript via Fable. Server runs on Cloudflare Workers, client uses Feliz/Elmish.

## Environment

All shell commands should be prefixed with the environment setup:

```
export PATH="/usr/local/share/dotnet:$HOME/.nvm/versions/node/v22.19.0/bin:$HOME/.dotnet/tools:$PATH"
```

## Common commands

- Build shared: `dotnet build src/Shared/Shared.fsproj`
- Build server: `dotnet build src/Server/Server.fsproj`
- Build client: `dotnet build src/Client/Client.fsproj`
- Fable compile: `dotnet fable <project-dir> --outDir <project-dir>/dist`
- Run JS output: `node <path-to-js>`
