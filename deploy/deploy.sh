#!/usr/bin/env bash
# Deploys one image version, and returns to the previous version if it doesn't become healthy.
#
#   bash deploy.sh sha-abc1234
#
# Stored data is kept, because volumes are never removed here.
set -euo pipefail

cd "$(dirname "$0")"

version="${1:?Usage: bash deploy.sh IMAGE_TAG (for example sha-abc1234 or v1.0.0)}"

if [ ! -f .env ]; then
  echo "No .env here. Copy .env.example to .env and fill in the secrets first." >&2
  exit 1
fi

compose() {
  docker compose -f docker-compose.prod.yml "$@"
}

start() {
  export IMAGE_TAG="$1"
  # SKIP_PULL is for testing with images built on this machine.
  if [ -z "${SKIP_PULL:-}" ]; then
    compose pull api frontend || return 1
  fi
  compose up -d --wait --wait-timeout 180 --remove-orphans
}

previous="$(cat .deployed-version 2>/dev/null || true)"

echo "Deploying ${version}"
if start "$version"; then
  echo "$version" > .deployed-version
  echo "Deployed ${version}"
  exit 0
fi

echo "Version ${version} did not become healthy." >&2
compose logs --tail 40 >&2 || true

if [ -n "$previous" ] && [ "$previous" != "$version" ]; then
  echo "Rolling back to ${previous}" >&2
  if start "$previous"; then
    echo "Rolled back to ${previous}" >&2
  else
    echo "Rollback to ${previous} also failed." >&2
  fi
fi
exit 1
