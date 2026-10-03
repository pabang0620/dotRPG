// TOTP 비밀키 암호화: AES-256-GCM, 저장 형식 = nonce(12) || 암호문 || 태그(16)
import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto';
import { getConfig } from '../../config/env';

function key(): Buffer {
  const k = getConfig().admin.secretKey;
  if (!k) throw new Error('ADMIN_SECRET_KEY 가 필요합니다');
  return k;
}

export function encryptSecret(plain: Buffer, k: Buffer = key()): Buffer {
  const nonce = randomBytes(12);
  const c = createCipheriv('aes-256-gcm', k, nonce);
  const enc = Buffer.concat([c.update(plain), c.final()]);
  return Buffer.concat([nonce, enc, c.getAuthTag()]);
}

export function decryptSecret(blob: Buffer, k: Buffer = key()): Buffer {
  if (blob.length < 12 + 16) throw new Error('암호문이 너무 짧습니다');
  const nonce = blob.subarray(0, 12);
  const tag = blob.subarray(blob.length - 16);
  const enc = blob.subarray(12, blob.length - 16);
  const d = createDecipheriv('aes-256-gcm', k, nonce);
  d.setAuthTag(tag);
  return Buffer.concat([d.update(enc), d.final()]);
}
