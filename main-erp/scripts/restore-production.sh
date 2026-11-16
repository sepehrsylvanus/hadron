#!/usr/bin/env bash
set -Eeuo pipefail

umask 077

usage() {
  printf '%s\n' \
    'Usage: scripts/restore-production.sh --backup DIRECTORY --confirm-restore [--env FILE]' \
    '' \
    'WARNING: This replaces the PostgreSQL schema/data and mirrors backed-up' \
    'MinIO buckets exactly. Run first in an isolated staging environment.'
}

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
repository_root="$(CDPATH= cd -- "${script_dir}/.." && pwd -P)"
compose_file="${COMPOSE_FILE:-${repository_root}/compose.production.yml}"
environment_file="${ENV_FILE:-${repository_root}/deploy/production.env}"
backup_directory=""
confirmed=0

while (($# > 0)); do
  case "$1" in
    --backup)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      backup_directory="$2"
      shift 2
      ;;
    --env)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      environment_file="$2"
      shift 2
      ;;
    --confirm-restore)
      confirmed=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown argument: %s\n' "$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

[[ "$confirmed" -eq 1 ]] || { printf 'Restore requires --confirm-restore.\n' >&2; exit 2; }
[[ -n "$backup_directory" && -d "$backup_directory" ]] || {
  printf 'Backup directory is missing or invalid: %s\n' "$backup_directory" >&2
  exit 1
}
[[ -f "$compose_file" ]] || { printf 'Compose file not found: %s\n' "$compose_file" >&2; exit 1; }
[[ -f "$environment_file" ]] || { printf 'Environment file not found: %s\n' "$environment_file" >&2; exit 1; }

backup_directory="$(CDPATH= cd -- "$backup_directory" && pwd -P)"
[[ "$backup_directory" != "/" ]] || { printf 'Refusing to restore from /.\n' >&2; exit 1; }
[[ -f "${backup_directory}/postgres.dump" ]] || { printf 'postgres.dump is missing.\n' >&2; exit 1; }
[[ -f "${backup_directory}/SHA256SUMS" ]] || { printf 'SHA256SUMS is missing.\n' >&2; exit 1; }

(
  cd -- "$backup_directory"
  sha256sum --check SHA256SUMS
)

compose=(docker compose --env-file "$environment_file" -f "$compose_file")
"${compose[@]}" config --quiet
"${compose[@]}" up -d --wait --wait-timeout 180 postgres minio
"${compose[@]}" stop caddy web api

restore_completed=0
report_incomplete_restore() {
  if [[ "$restore_completed" -ne 1 ]]; then
    printf '%s\n' \
      'Restore did not complete. Application services remain stopped for safety.' \
      'Inspect the command output and restore into staging before retrying.' >&2
  fi
}
trap report_incomplete_restore EXIT INT TERM

"${compose[@]}" exec -T postgres sh -ec \
  'pg_restore --clean --if-exists --exit-on-error --no-owner --no-acl --username "$POSTGRES_USER" --dbname "$POSTGRES_DB"' \
  < "${backup_directory}/postgres.dump"

BACKUP_HOST_PATH="$backup_directory" "${compose[@]}" --profile maintenance run --rm maintenance-tools '
  mc alias set target "$MINIO_ENDPOINT" "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null
  if [ -d /backup/minio ]; then
    for source_directory in /backup/minio/*; do
      [ -d "$source_directory" ] || continue
      bucket="$(basename "$source_directory")"
      mc mb --ignore-existing "target/${bucket}" >/dev/null
      mc mirror --overwrite --remove "$source_directory" "target/${bucket}"
    done
  fi
  if [ -d /backup/data-protection ]; then
    cp -R /backup/data-protection/. /data-protection/
    chown -R 10001:10001 /data-protection
    chmod 0700 /data-protection
    chmod 0600 /data-protection/* 2>/dev/null || true
  fi
'

"${compose[@]}" start api web caddy
restore_completed=1
trap - EXIT INT TERM

printf 'Restore completed from: %s\n' "$backup_directory"
printf '%s\n' 'Verify /health/ready internally and complete the post-restore smoke test.'
