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
