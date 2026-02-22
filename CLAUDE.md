# Hedge

F# full-stack web framework compiled to JavaScript via Fable. Server runs on Cloudflare Workers, client uses Feliz/Elmish.

## Common commands

- Build shared: `dotnet build src/Shared/Shared.fsproj`
- Build server: `dotnet build src/Server/Server.fsproj`
- Build client: `dotnet build src/Client/Client.fsproj`
- Build admin: `dotnet build src/Admin/Admin.fsproj`
- Fable compile: `dotnet fable <project-dir> --outDir <project-dir>/dist`
- Run JS output: `node <path-to-js>`
