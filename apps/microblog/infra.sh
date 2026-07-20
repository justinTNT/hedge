#!/usr/bin/env bash
set -euo pipefail

# Reads database_name from wrangler.toml
db_name() {
  grep 'database_name' wrangler.toml | head -1 | sed 's/.*= *"\(.*\)"/\1/'
}

USAGE="Usage: ./infra.sh <command> [site-name]

Commands:
  create <site-name>          Create D1 database + R2 bucket for a new site
  migrate                     Apply pending migrations locally
  migrate-remote              Apply pending migrations in production
  add-alert <topic> <url>     Register a Google Alerts RSS feed (add --remote for prod)
  deploy                      Build and deploy

Examples:
  ./infra.sh create wt-fail
  ./infra.sh migrate
  ./infra.sh add-alert climate 'https://www.google.com/alerts/feeds/12345/67890'
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

  add-alert)
    topic="${2:-}"
    url="${3:-}"
    scope="--local"
    [ "${4:-}" = "--remote" ] && scope="--remote"
    [ -z "$topic" ] || [ -z "$url" ] && echo "Usage: ./infra.sh add-alert <topic> <feed-url> [--remote]" && exit 1
    DB=$(db_name)
    # single-quote the URL to protect &-separated query params; escape embedded quotes
    esc_topic=${topic//\'/\'\'}
    esc_url=${url//\'/\'\'}
    id=$(npx wrangler d1 execute "$DB" $scope --json --command "SELECT lower(hex(randomblob(16))) AS id" | grep -o '"id":"[^"]*"' | head -1 | sed 's/"id":"\(.*\)"/\1/')
    echo "==> Registering alert '$topic' on $DB ($scope)"
    npx wrangler d1 execute "$DB" $scope --command \
      "INSERT INTO alert_sources (id, topic, feed_url, enabled, created_at) VALUES ('$id', '$esc_topic', '$esc_url', 1, strftime('%s','now'))"
    ;;

  deploy)
    echo "==> Building and deploying"
    npm run deploy
    ;;

  *)
    echo "$USAGE"
    ;;
esac

