# Hedge

F# full-stack web framework compiled to JavaScript via Fable.

## Structure

- `packages/hedge/` — framework (Hedge core, Admin client, Gen source)
- `apps/microblog/` — golden model app (reference implementation)

## Scaffold a new app

```bash
dotnet run --project packages/hedge/src/Gen/Scaffold.fsproj -- <app-name>
cd apps/<app-name>
npm install
npm run gen
npm run dev
```

## Common commands (run from app dir)

- Generate code: `npm run gen`
- Build server: `dotnet build src/Server/Server.fsproj`
- Build client: `dotnet build src/Client/Client.fsproj`
- Build admin: `dotnet build ../../packages/hedge/src/Admin/Admin.fsproj`
- Dev server: `npm run dev`
- Deploy: `npm run deploy`

## Test

```bash
./test.sh
```

Runs the microblog golden model build and a scaffold-then-build pipeline. If it passes, the framework is correct.
