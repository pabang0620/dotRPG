// 10단계 테스트 공용: 캠페인 작성·승인, 배달, 우편 시드
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { awaitCampaignDeliveries, deliverCampaignsFor } from '../src/domains/mail/campaignDelivery';
import { invalidateCampaignCache } from '../src/domains/mail/campaignCache';
import { auth } from './helpers';
import { accountIdOf } from './sweepHelpers';
import { adminPost, makeAdmin, type TestAdmin } from './opsHelpers';
import type { Hero } from './economyHelpers';

export const DAY = 86_400_000;

export async function twoOwners(): Promise<[TestAdmin, TestAdmin]> {
  return [await makeAdmin('owner'), await makeAdmin('owner')];
}

/** 기본 캠페인 본문(골드 + 아이템 + 이벤트 클리어권). 시각은 실제 시계 기준(관리자 세션이 실제 시각을 쓴다) */
export function campaignBody(over: Record<string, unknown> = {}): Record<string, unknown> {
  const now = Date.now();
  return {
    title: '정기 점검 보상',
    body: '점검에 협조해 주셔서 감사합니다.\n작은 선물을 드립니다.',
    category: 'maintenance',
    delivery_unit: 'account',
    target: { all: true },
    mail_days: 14,
    starts_at: new Date(now - 3_600_000).toISOString(),
    ends_at: new Date(now + 7 * DAY).toISOString(),
    cap_count: 100,
    attachments: [
      { kind: 'gold', amount: 5000 },
      { kind: 'item', item_key: 'potion_hp', count: 10 },
      { kind: 'sweep_ticket', count: 1 },
    ],
    memo: '테스트 지급',
    ...over,
  };
}

export async function createCampaign(admin: Express, a: TestAdmin, over: Record<string, unknown> = {}): Promise<string> {
  const res = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody(over));
  if (res.status !== 201) throw new Error(`campaign create ${res.status} ${JSON.stringify(res.body)}`);
  return res.body.data.campaign.id as string;
}

/** 작성(a) -> 승인(b)해서 진행 중으로 만든다. 캐시도 비운다 */
export async function activeCampaign(admin: Express, a: TestAdmin, b: TestAdmin, over: Record<string, unknown> = {}): Promise<string> {
  const id = await createCampaign(admin, a, over);
  const ok = await adminPost(admin, b, `/admin/mail-campaigns/${id}/approve`);
  if (ok.status !== 200) throw new Error(`campaign approve ${ok.status} ${JSON.stringify(ok.body)}`);
  invalidateCampaignCache();
  return id;
}

/** 그 캐릭터가 접속한 것으로 보고 캠페인 우편을 배달한다(프레즌스 진입과 같은 함수) */
export async function deliverTo(h: Hero): Promise<number> {
  return deliverCampaignsFor(await accountIdOf(h), h.dbId);
}

export const presenceBeat = (app: Express, h: Hero) =>
  request(app).post(`/characters/${h.id}/presence`).set(auth(h.s)).send({ map_id: 'village', auto_play: false, input_recent: true });

export const summary = (app: Express, h: Hero) => request(app).get(`/characters/${h.id}/mail/summary`).set(auth(h.s));

export { awaitCampaignDeliveries, randomUUID };

export async function campaignMails(campaignUuid: string): Promise<{ uuid: string; character_id: string; claimed_at: Date | null; expired_at: Date | null; attach_n: number }[]> {
  const r = await getPool().query(
    `SELECT m.uuid, m.character_id, m.claimed_at, m.expired_at, m.attach_n FROM mails m JOIN mail_campaigns c ON c.id = m.campaign_id WHERE c.uuid = $1 ORDER BY m.id`,
    [campaignUuid],
  );
  return r.rows as { uuid: string; character_id: string; claimed_at: Date | null; expired_at: Date | null; attach_n: number }[];
}

export const campaignRow = async (uuid: string): Promise<Record<string, unknown>> =>
  (await getPool().query('SELECT * FROM mail_campaigns WHERE uuid = $1', [uuid])).rows[0] as Record<string, unknown>;

/** 승인 전 캠페인의 배달 기간을 과거로 돌린다(내용 불변 트리거를 이 트랜잭션에서만 끈다. 시계를 밀면 관리자 세션이 만료되기 때문) */
export async function expireWindow(uuid: string): Promise<void> {
  const c = await getPool().connect();
  try {
    await c.query('BEGIN');
    await c.query('SET LOCAL session_replication_role = replica');
    await c.query("UPDATE mail_campaigns SET starts_at = now() - interval '9 days', ends_at = now() - interval '1 day' WHERE uuid = $1", [uuid]);
    await c.query('COMMIT');
  } catch (err) {
    await c.query('ROLLBACK');
    throw err;
  } finally {
    c.release();
  }
}
