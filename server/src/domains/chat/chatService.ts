// chat.send 처리(phase5_api.md 4.2). 첫 실패에서 중단하고, 모든 판정은 서버가 최종이다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { hasObfuscatedBannedWord, maskBannedWords } from '../../utils/bannedWords';
import { logger } from '../../utils/logger';
import { checkInterval, cleanText, codePointLength, pruneWindow, recordSend } from './chatRules';
import type { ChatSession } from './chatSession';
import * as repo from './chatRepository';
import { getChatWriter } from './chatWriter';
import { getLimiterStore, type LimiterState } from './limiterStore';
import { registry } from './realtimeNotifier';
import { activeMuteUntil, createAutoMute } from './sanctionService';
import type { ChatErrCode, ChatSendFrame, Frame } from './wsProtocol';
import { titleOf } from '../achievements/achievementRepository';
import { titleName } from '../achievements/achievementService';

const iso = (ms: number): string => new Date(ms).toISOString();

function fail(s: ChatSession, cid: string, code: ChatErrCode, message: string, extra: Record<string, unknown> = {}): void {
  s.send({ t: 'chat.err', cid, code, message, ...extra });
}

function ackFrom(cid: string, line: { id: number; text: string; filtered: boolean; created_at: Date }, replay: boolean): Frame {
  return { t: 'chat.ack', cid, seq: line.id, at: line.created_at.toISOString(), text: line.text, filtered: line.filtered, replay };
}

/** 금칙어 가림·거절 한 번을 센다. 창 안에서 기준에 닿으면 자동 채팅 금지 */
async function filterStrike(s: ChatSession, st: LimiterState, now: number): Promise<void> {
  const cfg = getConfig().social;
  st.filterStrikes = pruneWindow(st.filterStrikes, now, cfg.chatFilterWindowMinutes * 60_000);
  st.filterStrikes.push(now);
  if (st.filterStrikes.length >= cfg.chatFilterStrikes) {
    st.filterStrikes = [];
    await createAutoMute(s.accountId, 'auto_filter');
  }
}

async function repeatStrike(s: ChatSession, st: LimiterState, now: number): Promise<void> {
  const cfg = getConfig().social;
  st.repeatMuteStrikes = pruneWindow(st.repeatMuteStrikes, now, cfg.chatFilterWindowMinutes * 60_000);
  st.repeatMuteStrikes.push(now);
  if (st.repeatMuteStrikes.length >= cfg.chatRepeatMuteStrikes) {
    st.repeatMuteStrikes = [];
    await createAutoMute(s.accountId, 'auto_spam');
  }
}

const badFrame = (s: ChatSession, message: string): void => s.send({ t: 'error', ref: 'chat.send', code: 'BAD_FRAME', message });

export async function handleChatSend(s: ChatSession, f: ChatSendFrame): Promise<void> {
  const cfg = getConfig().social;
  const rules = getGameData().chat;
  const now = Date.now();

  // 3. 멱등성: 이미 저장된 cid면 처음 결과를 그대로(한도를 쓰지 않는다)
  const dup = await repo.findByCid(s.accountId, f.cid);
  if (dup) {
    s.send(ackFrom(f.cid, dup, true));
    return;
  }
  if (f.channel !== 'general' && f.channel !== 'party' && f.channel !== 'whisper') {
    fail(s, f.cid, 'CHANNEL_INVALID', '알 수 없는 채널입니다.');
    return;
  }
  const channel = f.channel;
  if (channel !== 'whisper' && (f.to !== undefined || f.to_name !== undefined)) {
    badFrame(s, '귓속말이 아니면 상대를 지정할 수 없습니다.');
    return;
  }
  if (channel === 'whisper' && f.to !== undefined && f.to_name !== undefined) {
    badFrame(s, '귓속말 상대는 하나만 지정합니다.');
    return;
  }

  // 4. 제재·반복 금지
  const st = getLimiterStore().get(s.accountId);
  if (s.sanctionMuteUntil > now) {
    fail(s, f.cid, 'MUTED_SANCTION', '채팅이 제한된 상태입니다.', { muted_until: iso(s.sanctionMuteUntil) });
    return;
  }
  if (st.repeatMutedUntil > now) {
    const sec = Math.ceil((st.repeatMutedUntil - now) / 1000);
    fail(s, f.cid, 'MUTED_REPEAT', `같은 말을 반복해 ${sec}초 동안 채팅이 막혔습니다.`, { muted_until: iso(st.repeatMutedUntil) });
    return;
  }

  // 5. 문장 정리
  const text = cleanText(f.text);
  if (text === '') {
    fail(s, f.cid, 'TEXT_EMPTY', '내용을 입력해 주세요.');
    return;
  }
  if (codePointLength(text) > rules.maxLength) {
    fail(s, f.cid, 'TEXT_TOO_LONG', `채팅은 ${rules.maxLength}자까지 보낼 수 있습니다.`);
    return;
  }

  // 6. 최소 간격, 분당 상한
  const gate = checkInterval(st, now, rules.minIntervalSeconds * 1000 * cfg.chatIntervalTolerance, cfg.chatPerMinute);
  if (!gate.ok) {
    fail(s, f.cid, 'RATE_LIMITED', '채팅을 너무 빠르게 보내고 있습니다.', { retry_after_ms: gate.retryAfterMs });
    return;
  }

  // 7. 채널별 검사
  let partyId: number | null = null;
  let partyMembers: number[] = [];
  let target: repo.TargetCharacter | null = null;
  let targetBlockedMe = false;
  if (channel === 'party') {
    const p = await repo.activeParty(s.characterId);
    if (!p) {
      fail(s, f.cid, 'NOT_IN_PARTY', '파티에 속해 있지 않습니다.');
      return;
    }
    partyId = p.partyId;
    partyMembers = p.memberIds;
  } else if (channel === 'whisper') {
    if (f.to === undefined && f.to_name === undefined) {
      fail(s, f.cid, 'TARGET_REQUIRED', '귓속말 상대를 적어 주세요. (/w 이름 내용)');
      return;
    }
    const found = f.to !== undefined ? await repo.findCharacterByUuid(getPool(), f.to) : await repo.findAliveByName(f.to_name as string);
    if (!found || found.deleted) {
      fail(s, f.cid, 'TARGET_NOT_FOUND', '그런 이름의 모험가를 찾을 수 없습니다.');
      return;
    }
    target = found;
    if (target.id === s.characterId) {
      fail(s, f.cid, 'SELF_WHISPER', '자기 자신에게는 귓속말을 보낼 수 없습니다.');
      return;
    }
    if (s.blocks.has(target.account_id)) {
      fail(s, f.cid, 'TARGET_BLOCKED', `${target.name}님은 차단 중입니다.`);
      return;
    }
    // 상대가 나를 차단했으면 접속 여부와 무관하게 정상 ack(차단·접속 상태가 드러나지 않게). 접속 확인은 그 뒤에 한다
    const ts = registry.ofCharacter(target.id);
    targetBlockedMe = ts ? ts.blocks.has(s.accountId) : await repo.isBlockedBy(target.account_id, s.accountId);
    if (!targetBlockedMe && !ts) {
      fail(s, f.cid, 'TARGET_OFFLINE', `${target.name}님은 접속 중이 아닙니다.`);
      return;
    }
    for (const [k, t] of st.whisperTargets) if (now - t >= 60_000) st.whisperTargets.delete(k);
    if (!st.whisperTargets.has(target.id) && st.whisperTargets.size >= cfg.chatWhisperTargetsPerMin) {
      fail(s, f.cid, 'WHISPER_TARGETS_LIMIT', '짧은 시간에 너무 많은 사람에게 귓속말을 보냈습니다.', { retry_after_ms: 60_000 });
      return;
    }
  }

  // 8. 금칙어: 숨기려는 변형은 거절, 그 밖에는 *로 가림
  if (hasObfuscatedBannedWord(text)) {
    await filterStrike(s, st, now);
    fail(s, f.cid, 'MESSAGE_BLOCKED', '사용할 수 없는 말이 들어 있습니다.');
    return;
  }
  const masked = maskBannedWords(text);
  if (masked.hit) await filterStrike(s, st, now);

  // 9. 보내기로 확정: 간격·반복 상태 갱신(같은 말 repeatLimit번째는 보내고 이후 muteSeconds 금지)
  const { repeats } = recordSend(st, now, text);
  if (target) st.whisperTargets.set(target.id, now);
  if (repeats >= rules.repeatLimit) {
    st.repeatMutedUntil = now + rules.muteSeconds * 1000;
    await repeatStrike(s, st, now);
  }

  // 10. 쓰기 큐: INSERT -> 전달 -> ack
  if (target && targetBlockedMe) {
    // 차단당한 사실을 드러내지 않는다: 저장·전달 없이 정상 ack(받을 수 없는 대화를 증거로 쌓지 않는다)
    s.send({ t: 'chat.ack', cid: f.cid, seq: 0, at: iso(now), text: masked.text, filtered: masked.hit, replay: false });
    return;
  }
  // 칭호: 보낼 때 장착한 것의 이름(바꾸면 다음 말부터 바뀐다)
  const senderTitle = titleName(await titleOf(getPool(), s.characterId));
  try {
    await getChatWriter().run(async () => {
      const saved = await repo.insertMessage({
        senderTitle,
        channel,
        shard: channel === 'general' ? s.shard : null,
        partyId,
        senderAccountId: s.accountId,
        senderCharacterId: s.characterId,
        senderName: s.characterName,
        recipientAccountId: target ? target.account_id : null,
        recipientCharacterId: target ? target.id : null,
        recipientName: target ? target.name : null,
        text: masked.text,
        filtered: masked.hit,
        clientMsgId: f.cid,
      });
      if (!saved) {
        // 같은 cid가 동시에 두 번 들어왔다: 먼저 저장된 결과를 돌려준다
        const first = await repo.findByCid(s.accountId, f.cid);
        if (first) s.send(ackFrom(f.cid, first, true));
        return;
      }
      const msg: Frame = {
        t: 'chat.msg',
        seq: saved.id,
        channel,
        from: { id: s.characterUuid, name: s.characterName, ...(senderTitle ? { title: senderTitle } : {}) },
        text: masked.text,
        at: saved.createdAt.toISOString(),
      };
      if (target) msg.to = { id: target.uuid, name: target.name };
      const receivers: ChatSession[] =
        channel === 'general'
          ? registry.inShard(s.shard)
          : channel === 'party'
            ? partyMembers.map((id) => registry.ofCharacter(id)).filter((x): x is ChatSession => x !== undefined)
            : [registry.ofCharacter((target as repo.TargetCharacter).id)].filter((x): x is ChatSession => x !== undefined);
      for (const r of receivers) {
        if (r === s || r.blocks.has(s.accountId)) continue;
        r.send(msg);
      }
      s.send(ackFrom(f.cid, { id: saved.id, text: masked.text, filtered: masked.hit, created_at: saved.createdAt }, false));
    });
  } catch (err) {
    logger.error({ err }, 'chat save failed');
    fail(s, f.cid, 'CHAT_UNAVAILABLE', '채팅을 보낼 수 없습니다. 잠시 후 다시 시도해 주세요.');
  }
}

/** 제재 금지 상태를 읽어 세션에 반영한다(hello) */
export async function loadMute(s: ChatSession): Promise<void> {
  s.sanctionMuteUntil = await activeMuteUntil(s.accountId);
}
