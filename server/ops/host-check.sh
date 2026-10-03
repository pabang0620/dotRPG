#!/usr/bin/env bash
# 호스트 점검(phase7_ops.md 8.3): 디스크·메모리·컨테이너·WAL 아카이브·백업 하트비트·복구 훈련 주기·TLS 만료를 확인해 웹훅으로 알린다.
# 호스트 cron(5분):  */5 * * * * cd /opt/dotrpg/server/ops && ./host-check.sh
# 환경: ALERT_WEBHOOK_URL(비면 표준 오류로만 출력), ALERT_WEBHOOK_FORMAT(discord|slack|json), SERVER_NAME, API_DOMAIN(TLS 만료 확인용, 선택)
#       BACKUP_STATE_DIR(기본 /var/lib/dotrpg), HOST_CHECK_STATE(기본 /tmp/dotrpg-host-check), ALERT_MIN_INTERVAL_MINUTES(기본 30)
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/../.env}"
if [ -f "$ENV_FILE" ]; then set -a; . "$ENV_FILE"; set +a; fi
STATE_DIR="${BACKUP_STATE_DIR:-/var/lib/dotrpg}"
MEMO="${HOST_CHECK_STATE:-/tmp/dotrpg-host-check}"
MIN_INTERVAL=$(( ${ALERT_MIN_INTERVAL_MINUTES:-30} * 60 ))
SERVER="${SERVER_NAME:-dotrpg}"
DC=(docker compose -f "$SCRIPT_DIR/compose.prod.yml" --env-file "$ENV_FILE")
mkdir -p "$MEMO"

send() {
  local level="$1" key="$2" text="$3" now last
  now="$(date +%s)"; last="$(cat "$MEMO/$key" 2>/dev/null || echo 0)"
  [ $(( now - last )) -ge "$MIN_INTERVAL" ] || return 0
  echo "$now" > "$MEMO/$key"
  echo "[$level] $SERVER $text" >&2
  [ -n "${ALERT_WEBHOOK_URL:-}" ] || return 0
  local body
  case "${ALERT_WEBHOOK_FORMAT:-discord}" in
    slack) body="{\"text\":\"[$level] $SERVER $text\"}" ;;
    json) body="{\"server\":\"$SERVER\",\"level\":\"$level\",\"detail\":\"$text\"}" ;;
    *) body="{\"content\":\"[$level] $SERVER $text\"}" ;;
  esac
  curl -fsS -m 10 -H 'content-type: application/json' -d "$body" "$ALERT_WEBHOOK_URL" >/dev/null || echo "웹훅 전송 실패" >&2
}
clear_key() { rm -f "$MEMO/$1"; }

# 디스크: 80% 경고, 90% 긴급
USED="$(df --output=pcent / | tail -n 1 | tr -dc '0-9')"
if [ "${USED:-0}" -ge 90 ]; then send 긴급 disk "디스크 사용 ${USED}%"; elif [ "${USED:-0}" -ge 80 ]; then send 경고 disk "디스크 사용 ${USED}%"; else clear_key disk; fi

# 메모리(사용 가능 비율 10% 미만이면 경고)
AVAIL="$(awk '/MemAvailable/ {a=$2} /MemTotal/ {t=$2} END {if (t>0) printf "%d", a*100/t}' /proc/meminfo 2>/dev/null || echo 100)"
if [ "${AVAIL:-100}" -lt 10 ]; then send 경고 mem "사용 가능 메모리 ${AVAIL}%"; else clear_key mem; fi

# 컨테이너: api, postgres 가 실행 중이어야 한다
for svc in api postgres; do
  if ! "${DC[@]}" ps --status running --services 2>/dev/null | grep -qx "$svc"; then send 긴급 "svc_$svc" "컨테이너 $svc 가 실행 중이 아닙니다"; else clear_key "svc_$svc"; fi
done

# WAL 아카이브(PITR을 켠 경우): 실패가 늘거나 10분 넘게 지연되면 긴급
if "${DC[@]}" exec -T postgres psql -U dotrpg -d dotrpg -At -c "SHOW archive_mode" 2>/dev/null | grep -qx on; then
  LAG="$("${DC[@]}" exec -T postgres psql -U dotrpg -d dotrpg -At -c "SELECT coalesce(extract(epoch FROM now() - last_archived_time)::int, 999999) FROM pg_stat_archiver" 2>/dev/null | tr -d '\r')"
  if [ "${LAG:-0}" -gt 600 ]; then send 긴급 wal "WAL 아카이브가 ${LAG}초 동안 없습니다"; else clear_key wal; fi
fi

# 일일 백업 성공 시각: 26시간 넘게 없으면 긴급
if [ -f "$STATE_DIR/last_backup_ok" ]; then
  AGE=$(( $(date +%s) - $(cat "$STATE_DIR/last_backup_ok") ))
  if [ "$AGE" -gt $(( 26 * 3600 )) ]; then send 긴급 backup "마지막 백업 성공이 $(( AGE / 3600 ))시간 전입니다"; else clear_key backup; fi
else
  send 경고 backup "백업 성공 기록($STATE_DIR/last_backup_ok)이 없습니다"
fi

# 복구 훈련: DRILL_LOG.md 의 마지막 날짜가 35일을 넘으면 경고
LAST_DRILL="$(grep -E '^[0-9]{4}-[0-9]{2}-[0-9]{2} \|' "$SCRIPT_DIR/DRILL_LOG.md" 2>/dev/null | tail -n 1 | cut -d' ' -f1)"
if [ -z "$LAST_DRILL" ] || [ $(( ( $(date +%s) - $(date -d "$LAST_DRILL" +%s 2>/dev/null || echo 0) ) / 86400 )) -gt 35 ]; then
  send 경고 drill "마지막 복구 훈련이 35일을 넘었습니다(${LAST_DRILL:-기록 없음})"
else clear_key drill; fi

# TLS 인증서 만료 14일 이내
if [ -n "${API_DOMAIN:-}" ] && command -v openssl >/dev/null 2>&1; then
  END="$(echo | openssl s_client -servername "$API_DOMAIN" -connect "$API_DOMAIN:443" 2>/dev/null | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2)"
  if [ -n "$END" ]; then
    DAYS=$(( ( $(date -d "$END" +%s) - $(date +%s) ) / 86400 ))
    if [ "$DAYS" -lt 14 ]; then send 경고 tls "TLS 인증서가 ${DAYS}일 뒤 만료됩니다"; else clear_key tls; fi
  fi
fi
