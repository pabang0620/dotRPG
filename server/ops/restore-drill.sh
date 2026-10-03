#!/usr/bin/env bash
# 복구 훈련(phase7_ops.md 7.5): 최신 덤프를 훈련용 PostgreSQL 컨테이너에 복구하고 확인 항목을 점검한다. 운영 DB는 건드리지 않는다.
#
# 사용:  ./restore-drill.sh [덤프 파일]      덤프를 주지 않으면 BACKUP_DEST(로컬 경로)의 가장 최근 daily 덤프를 쓴다
#   .age 파일이면 BACKUP_AGE_IDENTITY(개인키 파일)로 복호화한다. 개인키는 서버에 두지 않는 것이 원칙이므로 훈련은 개인 PC에서 해도 된다.
# 환경변수: BACKUP_DEST, BACKUP_AGE_IDENTITY, DRILL_IMAGE(운영과 같은 api 이미지. 있으면 /health/ready 도 확인),
#          DRILL_PG_IMAGE(기본 postgres:16), MIGRATIONS_DIR(기본 ../migrations)
# 확인: ① pg_restore 종료 코드 0 ② 표별 행 수 요약 ③ schema_migrations 마지막 이름 = 저장소의 마지막 파일 ④ (api 이미지가 있으면) /health/ready 200 ⑤ 걸린 시간(RTO) -> DRILL_LOG.md 한 줄
# 훈련용 DB에는 게임 계정 실데이터가 있다: 끝나면 컨테이너를 지우고 외부에 올리지 않는다.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MIGRATIONS_DIR="${MIGRATIONS_DIR:-$SCRIPT_DIR/../migrations}"
PG_IMAGE="${DRILL_PG_IMAGE:-postgres:16}"
NAME="dotrpg-drill-$$"
WORK="$(mktemp -d)"
START="$(date +%s)"
log() { printf '%s drill: %s\n' "$(date -u +%FT%TZ)" "$*"; }
fail() { log "실패: $*"; exit 1; }
cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; docker rm -f "${NAME}-api" >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT

DUMP="${1:-}"
if [ -z "$DUMP" ]; then
  : "${BACKUP_DEST:?덤프 파일을 인자로 주거나 BACKUP_DEST(로컬 경로)를 설정하세요}"
  DUMP="$(ls -1 "$BACKUP_DEST"/dotrpg-daily-*.dump* 2>/dev/null | sort | tail -n 1 || true)"
fi
[ -n "$DUMP" ] && [ -f "$DUMP" ] || fail "덤프 파일을 찾지 못했습니다"
log "덤프: $DUMP"

PLAIN="$DUMP"
if [[ "$DUMP" == *.age ]]; then
  : "${BACKUP_AGE_IDENTITY:?암호화된 덤프에는 BACKUP_AGE_IDENTITY(개인키 파일)가 필요합니다}"
  command -v age >/dev/null 2>&1 || fail "age 가 필요합니다"
  PLAIN="$WORK/restore.dump"
  age -d -i "$BACKUP_AGE_IDENTITY" -o "$PLAIN" "$DUMP" || fail "복호화 실패(개인키 확인)"
fi

docker run -d --name "$NAME" -e POSTGRES_PASSWORD=drill -e POSTGRES_DB=dotrpg "$PG_IMAGE" >/dev/null
for _ in $(seq 1 60); do
  docker exec "$NAME" pg_isready -U postgres -d dotrpg >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$NAME" pg_isready -U postgres -d dotrpg >/dev/null 2>&1 || fail "훈련용 PostgreSQL 이 뜨지 않았습니다"

# ① 복구(종료 코드 0이어야 한다)
docker exec -i "$NAME" pg_restore -U postgres -d dotrpg --no-owner --exit-on-error < "$PLAIN" || fail "pg_restore 실패"
log "① pg_restore 성공"

# ② 표별 행 수
docker exec "$NAME" psql -U postgres -d dotrpg -At -c \
  "SELECT relname || ' ' || n_live_tup FROM pg_stat_user_tables ORDER BY n_live_tup DESC LIMIT 10" | sed 's/^/   /'

# ③ 마이그레이션 일치
LAST_DB="$(docker exec "$NAME" psql -U postgres -d dotrpg -At -c "SELECT name FROM schema_migrations ORDER BY name DESC LIMIT 1")"
LAST_REPO="$(ls -1 "$MIGRATIONS_DIR" | grep -E '^[0-9]+_.+\.sql$' | sort | tail -n 1)"
[ "$LAST_DB" = "$LAST_REPO" ] || fail "③ 마이그레이션 불일치: DB=$LAST_DB 저장소=$LAST_REPO"
log "③ schema_migrations 마지막 = $LAST_DB"

# ④ 운영과 같은 이미지로 /health/ready (선택)
READY="건너뜀"
if [ -n "${DRILL_IMAGE:-}" ]; then
  docker run -d --name "${NAME}-api" --link "$NAME:postgres" \
    -e NODE_ENV=production -e AUCTION_TICK_ENABLED=false -e ADMIN_ENABLED=false -e JOBS_ENABLED=false \
    -e DATABASE_URL="postgres://postgres:drill@postgres:5432/dotrpg" \
    -e JWT_SECRET="drill-$(head -c 32 /dev/urandom | base64 | tr -d '=+/')" \
    -e MIN_CLIENT_VERSION=0.0.1 -e TRUST_PROXY=1 -e STEAM_AUTH_MODE=off -e PARTY_TRANSPORT=dev \
    -e AUTH_DEV_ENABLED=true -e ALLOW_DEV_AUTH_IN_PRODUCTION=true -e AUTH_DEV_REGISTER_ENABLED=false \
    "$DRILL_IMAGE" >/dev/null
  READY="실패"
  for _ in $(seq 1 40); do
    if docker exec "${NAME}-api" node -e "fetch('http://127.0.0.1:3000/health/ready').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))" 2>/dev/null; then READY="200"; break; fi
    sleep 1
  done
  [ "$READY" = "200" ] || fail "④ /health/ready 가 200이 아닙니다"
  log "④ /health/ready 200"
fi

ELAPSED=$(( $(date +%s) - START ))
log "⑤ 걸린 시간(RTO): ${ELAPSED}초"
printf '%s | %s | RTO %ss | ready %s | migrations %s\n' "$(date -u +%F)" "$(basename "$DUMP")" "$ELAPSED" "$READY" "$LAST_DB" >> "$SCRIPT_DIR/DRILL_LOG.md"
log "DRILL_LOG.md 에 한 줄을 남겼습니다. 정합성 점검(I1~I5)은 api 를 붙여 관리자 CLI 'ops run integrity-nightly --full' 로 확인하세요."
