#!/usr/bin/env bash
set -euo pipefail

# Reads database_name from wrangler.toml
db_name() {
  grep 'database_name' wrangler.toml | head -1 | sed 's/.*= *"\(.*\)"/\1/'
}

USAGE="Usage: ./infra.sh <command> [site-name]

Commands:
  create <site-name>  Create D1 database + R2 bucket for a new site
  migrate             Apply pending migrations locally
  migrate-remote      Apply pending migrations in production
  deploy              Build and deploy

Examples:
  ./infra.sh create wt-fail
  ./infra.sh migrate
  ./infra.sh deploy"

cmd="${1:-}"

case "$cmd" in
  create)
    site="${2:-}"
    [ -z "$site" ] && echo "Usage: ./infra.sh create <site-name>" && exit 1
    echo "==> Creating D1 database: ${site}-db"
    npx wrangler d1 create "${site}-db"
    echo ""
    echo "==> Creating R2 bucket: ${site}-blobs"
    npx wrangler r2 bucket create "${site}-blobs"
    echo ""
    echo "Done. Update wrangler.toml with the database_id and bucket_name above."
    ;;

  migrate)
    DB=$(db_name)
    echo "==> Migrating $DB (local)"
    npx wrangler d1 migrations apply "$DB" --local
    ;;

  migrate-remote)
    DB=$(db_name)
    echo "==> Migrating $DB (remote/production)"
    npx wrangler d1 migrations apply "$DB" --remote
    ;;

  deploy)
    echo "==> Building and deploying"
    npm run deploy
    ;;

  *)
    echo "$USAGE"
    ;;
esac

