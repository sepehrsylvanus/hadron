#!/usr/bin/env bash
set -Eeuo pipefail

# Backups contain business data and Data Protection keys. New files must be
# accessible only to the operator account unless moved into encrypted storage.
umask 077

usage() {
  printf '%s\n' \
    'Usage: scripts/backup-production.sh [--env FILE] [--output DIRECTORY]' \
    '' \
    'Creates a timestamped, checksummed backup directory. The application is' \
    'quiesced briefly so PostgreSQL, MinIO, and Data Protection keys represent' \
    'one operational point in time.'
}

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
repository_root="$(CDPATH= cd -- "${script_dir}/.." && pwd -P)"
compose_file="${COMPOSE_FILE:-${repository_root}/compose.production.yml}"
environment_file="${ENV_FILE:-${repository_root}/deploy/production.env}"
backup_root="${BACKUP_ROOT:-${repository_root}/backups/production}"

while (($# > 0)); do
  case "$1" in
    --env)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      environment_file="$2"
      shift 2
      ;;
    --output)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      backup_root="$2"
      shift 2
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

for command_name in docker sha256sum mktemp; do
  command -v "$command_name" >/dev/null 2>&1 || {
    printf 'Required command is missing: %s\n' "$command_name" >&2
    exit 1
  }
done

[[ -f "$compose_file" ]] || { printf 'Compose file not found: %s\n' "$compose_file" >&2; exit 1; }
[[ -f "$environment_file" ]] || { printf 'Environment file not found: %s\n' "$environment_file" >&2; exit 1; }

mkdir -p -- "$backup_root"
backup_root="$(CDPATH= cd -- "$backup_root" && pwd -P)"
[[ "$backup_root" != "/" ]] || { printf 'Refusing to use / as backup root.\n' >&2; exit 1; }

timestamp="$(date -u +'%Y%m%dT%H%M%SZ')"
backup_name="hadron-erp-${timestamp}"
destination="${backup_root}/${backup_name}"
[[ ! -e "$destination" ]] || { printf 'Backup already exists: %s\n' "$destination" >&2; exit 1; }

staging="$(mktemp -d "${backup_root}/.${backup_name}.XXXXXX")"
case "$staging" in
  "${backup_root}"/.*) ;;
  *) printf 'Unsafe staging path: %s\n' "$staging" >&2; exit 1 ;;
esac

compose=(docker compose --env-file "$environment_file" -f "$compose_file")
quiesced=0

resume_application() {
  if [[ "$quiesced" -eq 1 ]]; then
    printf 'Restarting application services after interrupted backup...\n' >&2
    "${compose[@]}" start api web caddy >/dev/null || true
  fi
}
trap resume_application EXIT INT TERM

"${compose[@]}" config --quiet
"${compose[@]}" up -d --wait --wait-timeout 180 postgres minio

# Stop writers, but leave durable stores online for their native backup tools.
"${compose[@]}" stop caddy web api
quiesced=1

"${compose[@]}" exec -T postgres sh -ec \
  'pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --no-owner --no-acl' \
  > "${staging}/postgres.dump"

BACKUP_HOST_PATH="$staging" "${compose[@]}" --profile maintenance run --rm maintenance-tools '
  mc alias set source "$MINIO_ENDPOINT" "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null
  mkdir -p /backup/minio /backup/data-protection
  bucket_list=$(printf "%s" "$MINIO_BACKUP_BUCKETS" | tr "," " ")
  for bucket in $bucket_list; do
    if mc stat "source/${bucket}" >/dev/null 2>&1; then
      mkdir -p "/backup/minio/${bucket}"
      mc mirror --overwrite "source/${bucket}" "/backup/minio/${bucket}"
    else
      printf "%s\n" "$bucket" >> /backup/minio-missing-buckets.txt
    fi
  done
  cp -R /data-protection/. /backup/data-protection/
'

{
  printf 'backup_format=hadron-erp-v1\n'
  printf 'created_utc=%s\n' "$timestamp"
  printf 'compose_project=hadron-erp-production\n'
  printf 'database_format=postgresql-custom\n'
  printf 'application_was_quiesced=true\n'
  printf '\nimages:\n'
  "${compose[@]}" config --images
} > "${staging}/manifest.txt"

(
  cd -- "$staging"
  find . -type f ! -name SHA256SUMS -print0 \
    | sort -z \
    | xargs -0 sha256sum > SHA256SUMS
)

"${compose[@]}" start api web caddy
quiesced=0

mv -- "$staging" "$destination"
trap - EXIT INT TERM

printf 'Backup completed: %s\n' "$destination"
printf '%s\n' 'Move this directory to encrypted, off-host storage and run a restore drill.'
