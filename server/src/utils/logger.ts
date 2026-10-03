import pino from 'pino';

// 비밀번호·토큰은 로그에 남기지 않는다
export const logger = pino({
  level: process.env.NODE_ENV === 'test' ? 'silent' : (process.env.LOG_LEVEL ?? 'info'),
  redact: ['req.headers.authorization', 'password', 'refresh_token', 'access_token'],
});
