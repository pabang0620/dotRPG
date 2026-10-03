import type { RequestHandler } from 'express';
import { getConfig } from '../config/env';
import { getGameData } from '../gamedata/loader';
import { AppError } from '../utils/AppError';
import { compareSemver, isSemver } from '../utils/semver';

const DATA_VERSION = /^[0-9a-f]{16}$/;

/** 설계 0.3: 클라이언트 버전 -> 데이터 버전 순서로 검사. 426은 이 미들웨어에서만 만든다. */
export const versionCheck =
  (opts: { data: boolean }): RequestHandler =>
  (req, _res, next) => {
    const client = req.header('x-client-version');
    if (!client || !isSemver(client)) {
      next(
        new AppError(400, 'X-Client-Version 헤더가 없거나 형식이 올바르지 않습니다.', 'CLIENT_VERSION_MISSING'),
      );
      return;
    }
    let dataVersion: string | undefined;
    if (opts.data) {
      dataVersion = req.header('x-data-version');
      if (!dataVersion || !DATA_VERSION.test(dataVersion)) {
        next(
          new AppError(400, 'X-Data-Version 헤더가 없거나 형식이 올바르지 않습니다.', 'DATA_VERSION_MISSING'),
        );
        return;
      }
    }
    const min = getConfig().minClientVersion;
    if (compareSemver(client, min) < 0) {
      next(
        new AppError(426, '게임을 업데이트해 주세요.', 'CLIENT_OUTDATED', {
          client_version: client,
          min_client_version: min,
        }),
      );
      return;
    }
    if (opts.data && dataVersion !== undefined) {
      const server = getGameData().dataVersion;
      if (dataVersion !== server) {
        next(
          new AppError(426, '게임을 업데이트해 주세요.', 'DATA_OUTDATED', {
            client_data_version: dataVersion,
            data_version: server,
          }),
        );
        return;
      }
    }
    next();
  };
