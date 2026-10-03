import pino from 'pino';

// 비밀번호·토큰·비밀키는 로그에 남기지 않는다(phase7_ops.md 3.4)
export const LOG_REDACT = [
  'req.headers.authorization',
  'password',
  'refresh_token',
  'access_token',
  '*.token',
  '*.password',
  '*.secret',
  '*.STEAM_WEB_API_KEY',
  'STEAM_WEB_API_KEY',
  'ALERT_WEBHOOK_URL',
  'OPS_HEARTBEAT_URL',
];

export const logger = pino({
  level: process.env.NODE_ENV === 'test' ? 'silent' : (process.env.LOG_LEVEL ?? 'info'),
  redact: LOG_REDACT,
});
