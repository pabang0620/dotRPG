import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { verifyDataHashes } from '../src/gamedata/loader';

const SRC = path.resolve(__dirname, '../data');
let tmp: string;

beforeEach(() => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'dotrpg-gd-'));
  for (const f of fs.readdirSync(SRC)) fs.copyFileSync(path.join(SRC, f), path.join(tmp, f));
});
afterEach(() => fs.rmSync(tmp, { recursive: true, force: true }));

describe('게임 데이터 해시 대조', () => {
  it('정상: 저장소의 실제 data가 data_version.json과 일치', () => {
    expect(() => verifyDataHashes(SRC)).not.toThrow();
  });

  it('파일 한 글자 변조 시 파일명을 담아 거부', () => {
    const f = path.join(tmp, 'shop.json');
    const text = fs.readFileSync(f, 'utf8');
    fs.writeFileSync(f, text.replace('1', '2'));
    expect(() => verifyDataHashes(tmp)).toThrow(/shop\.json/);
  });

  it('파일 누락 시 파일명을 담아 거부', () => {
    fs.rmSync(path.join(tmp, 'sweep.json'));
    expect(() => verifyDataHashes(tmp)).toThrow(/sweep\.json/);
  });

  it('files 해시는 맞아도 version이 다르면 거부', () => {
    const f = path.join(tmp, 'data_version.json');
    const v = JSON.parse(fs.readFileSync(f, 'utf8'));
    v.version = '0000000000000000';
    fs.writeFileSync(f, JSON.stringify(v));
    expect(() => verifyDataHashes(tmp)).toThrow(/version/);
  });
});
