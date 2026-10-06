// 테스트용 환경값 주입 (.env는 쓰지 않는다)
process.env.NODE_ENV = 'test';
process.env.DATABASE_URL = process.env.TEST_DATABASE_URL ?? '';
process.env.JWT_SECRET = 'test-secret-test-secret-test-secret-0123456789';
process.env.MIN_CLIENT_VERSION = '0.2.0';
process.env.AUTH_DEV_ENABLED = 'true';
// 일반 테스트는 한도에 걸리지 않게 크게 둔다. 속도 제한 테스트가 직접 낮춘다
process.env.RATE_REGISTER_IP_MAX = '10000';
process.env.RATE_LOGIN_IP_MAX = '10000';
process.env.RATE_LOGIN_FAIL_MAX = '10000';
process.env.RATE_REFRESH_ACCOUNT_MAX = '10000';
process.env.RATE_CHAR_CREATE_MAX = '10000';
process.env.RATE_STATE_SAVE_MAX = '10000';
process.env.RATE_GENERAL_IP_MAX = '100000';
// 3단계 경제 API 테스트는 속도 제한에 걸리지 않게 크게 둔다. 속도 제한 테스트가 직접 낮춘다
process.env.RATE_ECONOMY_IP_MAX = '100000';
process.env.RATE_KILL_PER_SEC = '10000';
process.env.RATE_KILL_PER_MIN = '100000';
process.env.RATE_CLAIM_PER_SEC = '10000';
process.env.RATE_GATHER_PER_SEC = '10000';
process.env.RATE_NODES_PER_SEC = '10000';
process.env.RATE_SHOP_PER_SEC = '10000';
process.env.RATE_ENHANCE_PER_SEC = '10000';
process.env.RATE_USE_PER_SEC = '10000';
process.env.RATE_SLOW_PER_SEC = '10000';
// 같은 아이템 연속 사용 간격은 끈다(쿨다운 테스트만 켠다)
process.env.ITEM_USE_MIN_GAP_MS = '0';
// 4단계: 파티 경로 속도 제한은 크게, Steam은 mock
process.env.RATE_PARTY_LIST_PER_SEC = '10000';
process.env.RATE_PARTY_POLL_PER_SEC = '10000';
process.env.RATE_PARTY_CREATE_PER_3SEC = '10000';
process.env.RATE_PARTY_ACTION_PER_SEC = '10000';
process.env.RATE_PARTY_RUN_PER_SEC = '10000';
process.env.RATE_HEARTBEAT_PER_2SEC = '10000';
process.env.RATE_STEAM_IP_MAX = '10000';
process.env.RATE_STEAM_LINK_MAX = '10000';
process.env.STEAM_AUTH_MODE = 'mock';
process.env.PARTY_TRANSPORT = 'dev';
// 5단계: 소셜 속도 제한은 크게(속도 제한 테스트가 직접 낮춘다), WebSocket 핸드셰이크 한도도 크게
process.env.RATE_SOCIAL_IP_MAX = '100000';
process.env.RATE_SOCIAL_LIST_PER_SEC = '10000';
process.env.RATE_SOCIAL_REQUEST_PER_MIN = '10000';
process.env.RATE_SOCIAL_ACTION_PER_SEC = '10000';
process.env.RATE_SOCIAL_BLOCK_PER_MIN = '10000';
process.env.WS_HANDSHAKE_PER_MIN_IP = '100000';
process.env.WS_UNAUTH_PER_IP = '1000';
// 6단계: 경매·우편 속도 제한은 크게(속도 제한 테스트가 직접 낮춘다). 등록 자격은 시험 편의로 낮춘다(자격 테스트가 직접 올린다)
for (const k of [
  'RATE_AUCTION_SEARCH_PER_SEC',
  'RATE_AUCTION_PRICE_PER_SEC',
  'RATE_AUCTION_SELLABLE_PER_SEC',
  'RATE_AUCTION_LIST_PER_SEC',
  'RATE_AUCTION_LIST_PER_MIN',
  'RATE_AUCTION_ACTION_PER_SEC',
  'RATE_AUCTION_MINE_PER_SEC',
  'RATE_MAIL_CLAIM_PER_SEC',
  'RATE_MAIL_CLAIM_ALL_PER_SEC',
  'RATE_MAIL_SUMMARY_PER_5SEC',
]) {
  process.env[k] = '10000';
}
process.env.AUCTION_MIN_LEVEL = '1';
process.env.AUCTION_MIN_ACCOUNT_AGE_DAYS = '0';
process.env.AUCTION_TICK_ENABLED = 'false';
// 7단계: 관리자 비밀키(테스트 전용 값), 정리 배치 간 대기 없음
process.env.ADMIN_SECRET_KEY = Buffer.alloc(32, 7).toString('base64');
process.env.PURGE_BATCH_SLEEP_MS = '0';
// 8단계: 전송 우선순위, 속도 제한은 크게(속도 제한 테스트가 직접 낮춘다), 호스트 인계 유예는 짧게
process.env.COMBAT_TRANSPORT_ORDER = 'relay,steam';
for (const k of [
  'RATE_RELAY_TICKET_PER_10S',
  'RATE_RELAY_TICKET_PER_MIN',
  'RATE_TRANSPORT_SWITCH_PER_10S',
  'RATE_FIELD_ENTER_PER_SEC',
  'RATE_FIELD_ENTER_PER_MIN',
  'RATE_FIELD_GET_PER_SEC',
  'RATE_FIELD_LEAVE_PER_SEC',
  'RATE_FIELD_CLAIM_PER_SEC',
  'RATE_FIELD_OBSERVE_PER_5SEC',
  'RATE_FIELD_ME_PER_SEC',
]) {
  process.env[k] = '10000';
}
process.env.RELAY_HOST_GRACE_MS = '300';
process.env.TRANSPORT_SWITCH_COOLDOWN_SECONDS = '0';
process.env.RELAY_HANDSHAKE_PER_MIN_IP = '100000';
process.env.RELAY_UNAUTH_PER_IP = '1000';
process.env.RELAY_CONN_PER_IP = '1000';
// 9단계(부정 행위 방지): 프레즌스·전직 속도 제한은 크게(속도 제한 테스트가 직접 낮춘다). 기기 지문 HMAC 비밀(테스트 전용 값)
process.env.DEVICE_HASH_PEPPER = 'test-device-pepper-test-device-pepper-0123456789';
for (const k of ['RATE_PRESENCE_PER_10S', 'RATE_PRESENCE_PER_MIN', 'RATE_CAREER_PER_SEC']) {
  process.env[k] = '10000';
}
// 구매자 자격은 시험 편의로 낮춘다(자격 테스트가 직접 올린다)
process.env.AUCTION_BUYER_MIN_LEVEL = '1';
process.env.AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS = '0';
// 10단계(소탕·운영 우편): 속도 제한은 크게(속도 제한 테스트가 직접 낮춘다). 기능 플래그는 테스트 파일이 켠다(기본 꺼짐 확인용)
for (const k of [
  'RATE_SWEEP_STATUS_PER_SEC',
  'RATE_SWEEP_RUN_PER_SEC',
  'RATE_SWEEP_ALL_PER_SEC',
  'RATE_SWEEP_BUY_PER_SEC',
  'RATE_SWEEP_CLAIM_PER_SEC',
]) {
  process.env[k] = '10000';
}
