#!/usr/bin/env bash
# 배포 도우미(phase7_ops.md 3.6). 서버 호스트의 server/ops 폴더에서 실행한다.
#
#   ./deploy.sh hot <새 이미지 태그>         A. 핫 배포: 마이그레이션·데이터 버전·프로토콜 변경이 없을 때(재시작 5~25초)
#   ./deploy.sh maintenance <새 이미지 태그> B. 점검 배포: 점검 창이 active 이고 drain 이 ready_to_stop 일 때 시작한다
#   ./deploy.sh rollback <이전 이미지 태그>  구 이미지로 되돌려 기동(스키마는 그대로 둔다: 추가형 마이그레이션 규칙)
#
# 이미지 이름은 .env 의 API_IMAGE 로 정한다(dotrpg-api:<태그>). 이미지는 미리 호스트에 올려 둔다(docker load 또는 레지스트리).
# 점검 창 예약·종료는 이 스크립트가 하지 않는다: 사람이 관리자 CLI 로 한다(공지·확인이 필요하다).
#   docker compose -f compose.prod.yml exec api node dist/admin/cli/cli.js maint schedule --in 30m --duration 20m --notice "업데이트"
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/../.env}"
IMAGE_REPO="${IMAGE_REPO:-dotrpg-api}"
DC=(docker compose -f "$SCRIPT_DIR/compose.prod.yml" --env-file "$ENV_FILE")
CLI=(node dist/admin/cli/cli.js)

log() { printf '%s deploy: %s\n' "$(date -u +%FT%TZ)" "$*"; }
fail() { log "실패: $*"; exit 1; }

MODE="${1:-}"; TAG="${2:-}"
[ -n "$MODE" ] && [ -n "$TAG" ] || fail "사용법: deploy.sh hot|maintenance|rollback <이미지 태그>"
[ -f "$ENV_FILE" ] || fail "$ENV_FILE 가 없습니다"
docker image inspect "${IMAGE_REPO}:${TAG}" >/dev/null 2>&1 || fail "이미지 ${IMAGE_REPO}:${TAG} 가 호스트에 없습니다"

current_image() { grep -E '^API_IMAGE=' "$ENV_FILE" | tail -n 1 | cut -d= -f2- || true; }
set_image() {
  if grep -qE '^API_IMAGE=' "$ENV_FILE"; then sed -i -E "s|^API_IMAGE=.*|API_IMAGE=$1|" "$ENV_FILE"; else printf 'API_IMAGE=%s\n' "$1" >> "$ENV_FILE"; fi
}
wait_ready() {
  for _ in $(seq 1 60); do
    if "${DC[@]}" exec -T api node -e "fetch('http://127.0.0.1:3000/health/ready').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))" 2>/dev/null; then return 0; fi
    sleep 2
  done
  return 1
}

PREV="$(current_image)"
log "현재 이미지: ${PREV:-없음} -> 새 이미지: ${IMAGE_REPO}:${TAG} (모드: $MODE)"

case "$MODE" in
  hot)
    set_image "${IMAGE_REPO}:${TAG}"
    "${DC[@]}" up -d api
    wait_ready || { log "새 컨테이너가 ready 가 되지 않습니다. 롤백: ./deploy.sh rollback <이전 태그>"; exit 1; }
    log "ready 확인. 1분 동안 'docker compose logs api' 와 /meta 의 data_version 을 확인하세요. 이상하면 rollback."
    ;;
  maintenance)
    log "1) 점검 상태 확인(drain)"
    DRAIN="$("${DC[@]}" exec -T api "${CLI[@]}" maint drain --json)" || fail "drain 조회 실패(관리자 로그인 필요: cli.js login)"
    echo "$DRAIN" | grep -q '"ready_to_stop":true' || fail "ready_to_stop 이 아닙니다: $DRAIN"
    log "2) 배포 전 스냅샷(predeploy-${TAG})"
    "$SCRIPT_DIR/backup.sh" "predeploy-${TAG}" || fail "백업 실패: 스냅샷 없이 마이그레이션하지 않습니다"
    LAST_MIG="$("${DC[@]}" exec -T postgres psql -U dotrpg -d dotrpg -At -c 'SELECT name FROM schema_migrations ORDER BY name DESC LIMIT 1' | tr -d '\r')"
    log "   배포 전 마지막 마이그레이션: ${LAST_MIG}"
    log "3) api 정지"
    "${DC[@]}" stop api
    log "4) 마이그레이션(새 이미지). 실패하면 그 파일은 롤백되어 스키마가 그대로이므로 구 이미지로 다시 띄운다"
    if ! API_IMAGE="${IMAGE_REPO}:${TAG}" "${DC[@]}" run --rm api node dist/db/migrate.js up; then
      log "마이그레이션 실패: 구 이미지(${PREV})로 기동합니다"
      "${DC[@]}" up -d api
      exit 1
    fi
    log "5) 새 이미지로 기동(점검 창 행이 DB에 있으므로 기동 직후부터 점검 상태)"
    set_image "${IMAGE_REPO}:${TAG}"
    "${DC[@]}" up -d api
    wait_ready || { log "ready 가 되지 않습니다. 구 이미지 롤백 또는 배포 전 스냅샷 복구를 검토하세요(ops/README.md 롤백 표)"; exit 1; }
    log "6) 점검 중 확인: cli.js ops status, 개발 계정으로 접속·핵심 동선 확인 후 'maint end <창 uuid>'"
    ;;
  rollback)
    set_image "${IMAGE_REPO}:${TAG}"
    "${DC[@]}" up -d api
    wait_ready || fail "롤백 이미지가 ready 가 되지 않습니다"
    log "롤백 완료(스키마는 그대로). 파괴적 변경이 포함된 배포였다면 ops/README.md 의 '스냅샷 복구'를 따르세요."
    ;;
  *) fail "알 수 없는 모드: $MODE" ;;
esac
