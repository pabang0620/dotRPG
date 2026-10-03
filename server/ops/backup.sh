#!/usr/bin/env bash
# 일일 논리 백업(phase7_ops.md 7.2 A): pg_dump -Fc -> (age 암호화) -> 목적지에 저장 -> 목록 확인 -> 하트비트.
#
# 사용:  ./backup.sh [태그]        태그 기본값 daily. 배포 전에는 predeploy-<git 해시>
# 호스트 cron 예(KST 04:10 = UTC 19:10):  10 19 * * * cd /opt/dotrpg/server/ops && ./backup.sh >> /var/log/dotrpg-backup.log 2>&1
#
# 환경변수(서버 .env 또는 cron 환경에서 읽는다. 값은 이 저장소에 두지 않는다)
#   BACKUP_DEST             필수. 목적지.
#                             로컬 경로:  /var/backups/dotrpg  (같은 머신 디스크. 개발·훈련용이거나 외부 디스크 마운트)
#                             rclone:     rclone:<remote>:<bucket>/dotrpg  (오프사이트 S3 호환 저장소, 자리표시: 업체가 정해지면 rclone config)
#   BACKUP_AGE_RECIPIENT    age 공개키(age1...). 개인키는 서버에 두지 않는다. 없으면 BACKUP_ALLOW_PLAINTEXT=1 이 있어야 한다
#   BACKUP_ALLOW_PLAINTEXT  1이면 암호화 없이 저장(로컬 시험 전용)
#   PG_DUMP_CMD             pg_dump 를 실행하는 명령(덤프를 표준출력으로). 기본: compose 의 postgres 컨테이너에서 실행
#   BACKUP_VERIFY           0이면 pg_restore --list 확인을 건너뛴다(기본 1)
#   BACKUP_HEARTBEAT_URL    성공하면 GET 으로 핑(외부 서비스가 핑이 끊기면 알린다)
#   BACKUP_STATE_DIR        마지막 성공 시각 파일 위치(host-check.sh 가 읽는다). 기본 /var/lib/dotrpg
#   BACKUP_KEEP_DAILY / _WEEKLY / _MONTHLY   로컬 목적지의 보관 개수(기본 14 / 8 / 6)
#
# 오프사이트(rclone)는 쓰기 전용·삭제 불가 권한을 권장한다: 서버가 털려도 과거 백업을 못 지운다. 보관 개수는 저장소의 수명 주기 규칙으로 맞춘다.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TAG="${1:-daily}"
: "${BACKUP_DEST:?BACKUP_DEST 가 필요합니다 (로컬 경로 또는 rclone:<remote>:<bucket>/<path>)}"
STATE_DIR="${BACKUP_STATE_DIR:-/var/lib/dotrpg}"
KEEP_DAILY="${BACKUP_KEEP_DAILY:-14}"
KEEP_WEEKLY="${BACKUP_KEEP_WEEKLY:-8}"
KEEP_MONTHLY="${BACKUP_KEEP_MONTHLY:-6}"
DUMP_CMD="${PG_DUMP_CMD:-docker compose -f ${SCRIPT_DIR}/compose.prod.yml exec -T postgres pg_dump -U dotrpg -d dotrpg -Fc --no-owner}"

log() { printf '%s backup: %s\n' "$(date -u +%FT%TZ)" "$*"; }
fail() { log "실패: $*"; exit 1; }

# 로컬 보관 정리(함수는 사용 전에 정의한다)
prune_local() {
  local dir="$1" kd="$2" kw="$3" km="$4"
  local -a daily=() other=()
  local f base day
  while IFS= read -r f; do
    base="$(basename "$f")"
    if [[ "$base" == dotrpg-daily-* ]]; then daily+=("$f"); else other+=("$f"); fi
  done < <(ls -1 "$dir"/dotrpg-*.dump* 2>/dev/null | sort -r)
  local -A keep=()
  local n=0 w=0 m=0
  for f in "${daily[@]}"; do
    base="$(basename "$f")"
    day="$(echo "$base" | sed -E 's/^dotrpg-daily-([0-9]{8})T.*/\1/')"
    n=$((n + 1))
    if [ "$n" -le "$kd" ]; then keep["$f"]=1; fi
    if [ "$(date -u -d "$day" +%u 2>/dev/null)" = "7" ] && [ "$w" -lt "$kw" ]; then keep["$f"]=1; w=$((w + 1)); fi
    if [ "${day:6:2}" = "01" ] && [ "$m" -lt "$km" ]; then keep["$f"]=1; m=$((m + 1)); fi
  done
  n=0
  for f in "${other[@]}"; do
    n=$((n + 1))
    if [ "$n" -le 10 ]; then keep["$f"]=1; fi
  done
  for f in "${daily[@]}" "${other[@]}"; do
    [ -n "${keep[$f]:-}" ] || { rm -f "$f"; log "오래된 백업 삭제: $(basename "$f")"; }
  done
}

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
NAME="dotrpg-${TAG}-${STAMP}.dump"
nice -n 10 bash -c "$DUMP_CMD" > "$WORK/$NAME" || fail "pg_dump 가 실패했습니다"
[ -s "$WORK/$NAME" ] || fail "덤프 파일이 비어 있습니다"
log "덤프 완료: $(wc -c < "$WORK/$NAME") 바이트"

# 목록이 읽히는지 확인(암호화 전의 원본으로)
if [ "${BACKUP_VERIFY:-1}" != "0" ]; then
  if command -v pg_restore >/dev/null 2>&1; then
    pg_restore --list "$WORK/$NAME" >/dev/null || fail "pg_restore --list 확인에 실패했습니다"
    log "pg_restore --list 확인 완료"
  else
    log "경고: pg_restore 가 없어 목록 확인을 건너뜁니다(호스트에 postgresql-client 설치 권장)"
  fi
fi

OUT="$WORK/$NAME"
if [ -n "${BACKUP_AGE_RECIPIENT:-}" ]; then
  command -v age >/dev/null 2>&1 || fail "age 가 설치되어 있지 않습니다"
  age -r "$BACKUP_AGE_RECIPIENT" -o "$WORK/$NAME.age" "$WORK/$NAME" || fail "age 암호화에 실패했습니다"
  OUT="$WORK/$NAME.age"
elif [ "${BACKUP_ALLOW_PLAINTEXT:-0}" != "1" ]; then
  fail "BACKUP_AGE_RECIPIENT 가 없습니다(암호화 없이 저장하려면 BACKUP_ALLOW_PLAINTEXT=1)"
fi
FINAL="$(basename "$OUT")"

case "$BACKUP_DEST" in
  rclone:*)
    command -v rclone >/dev/null 2>&1 || fail "rclone 이 설치되어 있지 않습니다"
    rclone copyto "$OUT" "${BACKUP_DEST#rclone:}/${FINAL}" || fail "오프사이트 업로드에 실패했습니다"
    log "업로드 완료: ${BACKUP_DEST#rclone:}/${FINAL}"
    ;;
  *)
    mkdir -p "$BACKUP_DEST"
    cp "$OUT" "$BACKUP_DEST/.${FINAL}.part" && mv "$BACKUP_DEST/.${FINAL}.part" "$BACKUP_DEST/${FINAL}"
    log "저장 완료: $BACKUP_DEST/${FINAL}"
    # 로컬 목적지의 보관: daily 태그 파일 중 최근 N개 + 일요일 최근 M개 + 매월 1일 최근 K개를 남기고 지운다.
    # 다른 태그(predeploy-*)는 최근 10개만 남긴다.
    prune_local "$BACKUP_DEST" "$KEEP_DAILY" "$KEEP_WEEKLY" "$KEEP_MONTHLY" 2>/dev/null || true
    ;;
esac

mkdir -p "$STATE_DIR" 2>/dev/null && date -u +%s > "$STATE_DIR/last_backup_ok" 2>/dev/null || log "경고: $STATE_DIR 에 성공 시각을 쓰지 못했습니다"

if [ -n "${BACKUP_HEARTBEAT_URL:-}" ]; then
  curl -fsS -m 10 "$BACKUP_HEARTBEAT_URL" >/dev/null || log "경고: 하트비트 핑에 실패했습니다"
fi
log "끝"
