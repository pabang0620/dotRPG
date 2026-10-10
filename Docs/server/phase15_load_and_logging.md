# 부하 방지와 로그 보강 (2026-10-10)

대상: 시험 서버(구글 e2-micro, vCPU 공유 2개, RAM 953MB, API 메모리 상한 450MB, 같은 서버에 계산기·타이머 사이트), DB는 Supabase 무료(500MB, 넘으면 읽기 전용).
실측: 처치 1건 서버 처리 중앙값 144ms(상위 5% 263ms), 드롭 줍기 102ms, DB 왕복 4.7ms, DB 37MB, 풀 5개, journald 64MB(상한 없음), 24시간 로그 2,039줄 중 요청 로그 1,498줄(1~2명 플레이).

## 1. 부하 방지

| # | 문제 | 대책 | 위치 |
|---|---|---|---|
| L1 | DB 용량이 처치 수에 비례해 쌓임. 10명이면 수일 안에 500MB | request_log: 처치·드롭 줍기·채집·아이템 사용(`HOT_REQUEST_ENDPOINTS`)은 24시간만 보관(`REQUEST_LOG_HOT_RETENTION_HOURS`, 재전송 확인은 몇 분이면 충분). 상태 저장은 request_log를 쓰지 않는다. job_runs 보관 90일 -> 7일. DB 크기(이미 스냅샷에 있던 `db.size_mb`)로 400MB 경고, 470MB 긴급 | purge-hourly, env 기본값, alertRules |
| L2 | 처치 1건이 연결을 오래 붙잡는데 풀이 5개 | 코드 기본값은 이미 10이고 시험 서버 .env가 5로 낮춰 둔 것. 배포 때 시험 서버 .env의 `DB_POOL_MAX=10`으로 바꾼다(.env.example에 이유 기록) | 시험 서버 .env |
| L3 | 같은 공유기의 친구들이 경제 경로 IP 한도(분당 600)에 함께 걸림 | IP 한도 1500(`RATE_ECONOMY_IP_MAX`), 경로의 캐릭터 uuid 기준 분당 300(`RATE_ECONOMY_CHAR_MAX`)을 따로 둔다 | app.ts |
| L4 | Node 서버 시간 제한이 기본값(요청 300초) | requestTimeout 30초, headersTimeout 15초, keepAliveTimeout 65초(`HTTP_*_TIMEOUT_MS`) | server.ts |
| L5 | nginx에 빈도·동시 연결 제한이 없음 | dotRPG 블록에만 IP당 초당 30(버스트 60), 동시 연결 50, 본문 수신 10초, 프록시 응답 30초(웹소켓 경로는 길게) | 시험 서버 nginx, ops 문서 |
| L6 | 맵을 번갈아 보내면 프레임마다 DB 조회 | 프레임 수 상한은 이미 있었다(`WS_FRAMES_PER_SEC=10`, 넘으면 끊음). 추가: presence.set 맵 변경 알림은 소켓당 1초 1회로 묶어 마지막 맵만 보내고, 다른 마을 진입은 받되 1초 안의 재진입은 DB를 다시 읽지 않고 직전 프로필을 쓴다(`WS_MAP_CHANGE_MIN_MS`. 진입을 버리면 서 있는 동안 다른 사람에게 안 보인다) | wsServer, townPresence |
| L7 | 드롭 줍기 묶음 간격 0.25초가 거의 효과 없음(처치 392회에 줍기 467회) | 묶음 간격 1초. 화면 표시는 지금처럼 바로 | 클라이언트 드롭 줍기 |
| L8 | 메뉴가 120초마다 일일 의뢰를 다시 불러옴(조회 1회 13쿼리) | 창이 닫혀 있으면 300초, 열려 있으면 지금처럼 | SideMenuView |
| L9 | 파티 진행 중 1초 폴링 | 2초로(웹소켓 알림이 있으면 그쪽 우선) | PartyRunSession |

보류(실측 후 판단): 마을 위치 중계 방 정원(지금 150명, 20명부터 나가는 프레임이 사람 수 제곱으로 늘어남), xp_ledger의 처치 행 합산, 풀러 트랜잭션 모드.

## 2. 로그

| # | 빠진 것 | 대책 | 저장 |
|---|---|---|---|
| G1 | DB 풀 오류 리스너 없음: 쉬는 연결이 끊기면 프로세스가 죽고 원인이 안 남음 | `pool.on('error')` -> error 로그(프로세스 유지) | journald |
| G2 | 크래시 처리기 없음 | uncaughtException, unhandledRejection -> fatal 로그 후 종료(systemd가 재시작). 시작 로그에 버전·데이터 버전·풀 크기 | journald |
| G3 | 요청 로그에 오류 코드 없음, 레벨이 모두 info | 요청 로그에 `code`. 4xx는 warn, 5xx는 error | journald |
| G4 | 느린 요청·쿼리 표시 없음 | 요청 1초 이상 `request.slow`(warn, `SLOW_REQUEST_MS`). 쿼리 500ms 이상 `db.query.slow`(SQL 앞 80자, 값은 남기지 않음, `SLOW_QUERY_MS`) | journald |
| G5 | 잦은 경로의 성공 요청이 로그 대부분 | 처치·줍기·채집·상태 저장·프레즌스(`QUIET_ROUTES`)의 빠른 성공은 debug로 내리고, ops.snapshot `http.routes_5m`에 경로별 건수·p95·오류 수(상위 12개) | journald |
| G6 | `tip_` 안내 플래그가 저장마다 경고 | `tip_` 접두는 허용, 그 밖의 모르는 플래그만 경고 | journald |
| G7 | journald 크기 상한 없음 | SystemMaxUse=300M, MaxRetentionSec=30day | 시험 서버 설정 |
| G8 | 클라이언트(폰·친구 PC) 예외를 알 수 없음 | `POST /client-errors`(로그인 필요, 버전 검사 없음, 계정당 분당 5건, 본문 4KB 넘으면 413, 응답 204), 본문 `{version<=32, platform<=32, message<=500, stack<=2000, scene?<=64, at? ISO}`. 표 `client_errors`(0030, 14일 보관, 탈퇴 때 삭제). journald에는 `client.error` 한 줄. 클라이언트는 예외 로그를 같은 메시지 1회만, 세션당 20건까지 보낸다 | DB |
| G9 | DB 크기를 모름 | L1 스냅샷에 DB 크기, 400MB 경보 | journald |

이미 충분한 것: pino와 민감정보 가림, req_id 상관, 500 스택, 5xx 비율·풀 대기·p95·이벤트 루프 경보, 3분 ops 스냅샷, DB 감사 기록(anomaly_log, request_log, 원장, admin_audit_log, login_events, job_runs)과 정리 잡.

## 3. 검증

- 서버: 바뀐 경로의 테스트(정리 잡 보관 기간, 레이트리밋 캐릭터 한도, 웹소켓 프레임 제한, client-errors 한도·크기, 요청 로그 레벨), tsc, 전체 테스트 1회
- 클라이언트: 컴파일 1회
- 시험 서버 설정(L5, G7): `nginx -t` 통과 뒤 reload, journald 재시작, 적용 전 파일 백업
