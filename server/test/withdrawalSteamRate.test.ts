// W3 Steam 경로 속도 제한: 티켓 검증 전에 로그인 Steam 경로와 같은 버킷(steam-ip)을 쓴다
import { setClockOverride } from '../src/utils/clock';
import { resetDb, shutdown } from './helpers';
import { payApp, resetPay } from './payHelpers';
import { cancelWithdrawalApi, devUser, type WUser } from './withdrawalHelpers';

const app = payApp({ RATE_STEAM_IP_MAX: '2' });
beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

describe('W3 Steam 경로 속도 제한', () => {
  it('같은 IP 의 Steam 철회 시도는 분당 RATE_STEAM_IP_MAX 를 넘으면 429, 비밀번호 경로는 영향이 없다', async () => {
    const dev = await devUser(app);
    const ghost = { ...dev, steamId: '76561198123456789', loginId: null } as WUser;
    expect((await cancelWithdrawalApi(app, ghost)).status).toBe(404);
    expect((await cancelWithdrawalApi(app, ghost)).status).toBe(404);
    const limited = await cancelWithdrawalApi(app, ghost);
    expect(limited.status).toBe(429);
    // 같은 IP 라도 dev 비밀번호 경로는 steam 버킷을 쓰지 않는다
    expect((await cancelWithdrawalApi(app, dev)).status).toBe(404);
  });
});
