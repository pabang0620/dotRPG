// [NET 2026-10-30] 교착 재시도(retryOnDeadlock): 진 요청을 통째로 다시 부르고, 다른 오류는 그대로 던진다
import { retryOnDeadlock } from '../src/db/pool';

const pgError = (code: string): Error & { code: string } => Object.assign(new Error(code), { code });

describe('retryOnDeadlock', () => {
  it('교착(40P01)·직렬화 실패(40001)는 다시 실행해 결과를 돌려준다', async () => {
    let calls = 0;
    const out = await retryOnDeadlock(async () => {
      calls++;
      if (calls === 1) throw pgError('40P01');
      if (calls === 2) throw pgError('40001');
      return 'ok';
    });
    expect(out).toBe('ok');
    expect(calls).toBe(3);
  });

  it('횟수를 다 쓰면 마지막 오류를 던진다', async () => {
    let calls = 0;
    await expect(
      retryOnDeadlock(async () => {
        calls++;
        throw pgError('40P01');
      }, 2),
    ).rejects.toMatchObject({ code: '40P01' });
    expect(calls).toBe(2);
  });

  it('다른 오류(고유 위반, 앱 오류)는 다시 실행하지 않는다', async () => {
    let calls = 0;
    await expect(
      retryOnDeadlock(async () => {
        calls++;
        throw pgError('23505');
      }),
    ).rejects.toMatchObject({ code: '23505' });
    expect(calls).toBe(1);
  });
});
