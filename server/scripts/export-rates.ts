// 확률형 아이템 정보 공개 문서를 Markdown으로 출력한다(웹 페이지 GET /policies/:lang/probability와 같은 함수).
//   npx tsx scripts/export-rates.ts [ko|en] > rates.md
// 스토어·광고에 넣을 확률 공개 주소는 웹 페이지를 쓰고, 이 출력은 공지·심사 제출용 사본이다.
import fs from 'node:fs';
import path from 'node:path';
import { initConfig } from '../src/config/env';
import { buildRatesMarkdown } from '../src/domains/policies/ratesDisclosure';
import { resolveLang } from '../src/domains/policies/policyVersions';

const envFile = path.resolve(__dirname, '..', '.env');
if (fs.existsSync(envFile)) process.loadEnvFile(envFile);
const cfg = initConfig();
process.stdout.write(buildRatesMarkdown(cfg.gameDataDir, resolveLang(process.argv[2] ?? 'ko')) + '\n');
