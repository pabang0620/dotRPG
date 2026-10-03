// 첫 관리자 만들기와 2FA 기기 분실 복구(phase7_ops.md 5.2). 셸 접근은 곧 서버 장악이라 API 인증을 요구하지 않는다.
//   docker compose exec api node dist/admin/bootstrap.js create-owner <login_id>
//   docker compose exec api node dist/admin/bootstrap.js reset-totp <login_id>
import { initConfig } from '../config/env';
import { closePool, getPool, withTransaction } from '../db/pool';
import * as authRepo from './auth/adminAuthRepository';
import { hashPassword, newTempPassword } from './auth/adminAuthService';
import { writeAudit } from './common/audit';

const LOGIN_RE = /^[a-z0-9_.-]{3,32}$/;

/** owner 계정을 만들고 임시 비밀번호를 돌려준다(한 번만 출력한다). 감사 bootstrap.create_owner */
export async function createOwner(loginId: string): Promise<{ loginId: string; tempPassword: string }> {
  if (!LOGIN_RE.test(loginId)) throw new Error('아이디는 영문 소문자·숫자·_.- 3~32자입니다');
  const temp = newTempPassword();
  const hash = await hashPassword(temp);
  await withTransaction(async (client) => {
    const r = await client.query(
      `INSERT INTO admin_users (login_id, display_name, role, password_hash, must_change_password)
       VALUES ($1, $1, 'owner', $2, true) ON CONFLICT (login_id) DO NOTHING RETURNING uuid`,
      [loginId, hash],
    );
    if (r.rows.length === 0) throw new Error(`이미 있는 아이디입니다: ${loginId}`);
    await writeAudit(client, { adminId: null, loginIdTried: loginId, action: 'bootstrap.create_owner', targetType: 'admin', targetUuid: (r.rows[0] as { uuid: string }).uuid, result: 'ok', ip: 'shell' });
  });
  return { loginId, tempPassword: temp };
}

/** 2FA 초기화(기기 분실): 비밀키를 지우고 모든 세션을 폐기한다. 감사 bootstrap.reset_totp */
export async function resetTotp(loginId: string): Promise<void> {
  await withTransaction(async (client) => {
    const a = await authRepo.findByLoginId(client, loginId, true);
    if (!a) throw new Error(`없는 아이디입니다: ${loginId}`);
    await client.query('UPDATE admin_users SET totp_secret_enc = NULL, totp_confirmed_at = NULL, totp_last_step = NULL WHERE id = $1', [a.id]);
    await authRepo.revokeAllSessions(client, a.id, new Date());
    await writeAudit(client, { adminId: null, loginIdTried: loginId, action: 'bootstrap.reset_totp', targetType: 'admin', targetUuid: a.uuid, result: 'ok', ip: 'shell' });
  });
}

async function main(): Promise<void> {
  const [cmd, loginId] = process.argv.slice(2);
  initConfig();
  await getPool().query('SELECT 1');
  if (cmd === 'create-owner' && loginId) {
    const r = await createOwner(loginId);
    console.log(`owner 계정을 만들었습니다: ${r.loginId}`);
    console.log(`임시 비밀번호(지금 한 번만 표시됩니다): ${r.tempPassword}`);
    console.log('첫 로그인에서 비밀번호를 바꾸고 2단계 인증을 등록하세요: dotrpg-admin login');
  } else if (cmd === 'reset-totp' && loginId) {
    await resetTotp(loginId);
    console.log(`2단계 인증을 초기화했습니다: ${loginId} (다음 로그인에서 다시 등록)`);
  } else {
    console.error('사용법: bootstrap.js create-owner <login_id> | reset-totp <login_id>');
    process.exitCode = 2;
  }
  await closePool();
}

if (require.main === module) {
  main().catch((err: unknown) => {
    console.error(err instanceof Error ? err.message : err);
    process.exit(1);
  });
}
