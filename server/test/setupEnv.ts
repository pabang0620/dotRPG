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
