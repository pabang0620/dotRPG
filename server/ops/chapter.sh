#!/usr/bin/env bash
# 장 잠금 열기·닫기 (시험 서버 35.216.2.121). 재빌드·재배포 없이 env 하나와 API 재시작으로 끝난다.
# 사용: server/ops/chapter.sh status | open 3 | close 3
#   open 3  -> /opt/dotrpg/.env 에 CHAPTER3_OPEN=true, dotrpg-api 재시작, /meta 의 level_cap·chapter3_open 확인
#   close 3 -> CHAPTER3_OPEN=false 로 되돌린다(이미 Lv41 이상인 캐릭터의 레벨은 그대로 남는다)
# 클라이언트는 다음 로그인 때 /meta 를 다시 읽고, 열려 있으면 3장 이야기 묶음(GET /content/chapter3)을 받는다.
set -euo pipefail

HOST="${DOTRPG_HOST:-lee@35.216.2.121}"
KEY="${DOTRPG_KEY:-$HOME/.ssh/id_ed25519}"
API="${DOTRPG_API:-https://35-216-2-121.sslip.io}"

meta() {
  curl -s "$API/meta" | python3 -c 'import json,sys; d=json.load(sys.stdin)["data"]; print("data_version", d.get("data_version"), "| level_cap", d.get("level_cap"), "| chapter3_open", d.get("chapter3_open"))'
}

set_flag() {
  local name="$1" value="$2"
  ssh -i "$KEY" "$HOST" "sudo bash -c 'if grep -q \"^$name=\" /opt/dotrpg/.env; then sed -i \"s/^$name=.*/$name=$value/\" /opt/dotrpg/.env; else echo \"$name=$value\" >> /opt/dotrpg/.env; fi' && sudo systemctl restart dotrpg-api && sleep 6 && systemctl is-active dotrpg-api"
}

case "${1:-status}" in
  status) meta ;;
  open|close)
    n="${2:-}"
    [[ "$n" =~ ^[0-9]+$ ]] || { echo "장 번호가 필요합니다: $0 $1 3" >&2; exit 2; }
    v=$([[ "$1" == open ]] && echo true || echo false)
    set_flag "CHAPTER${n}_OPEN" "$v"
    meta
    ;;
  *) echo "사용: $0 status | open <장> | close <장>" >&2; exit 2 ;;
esac
