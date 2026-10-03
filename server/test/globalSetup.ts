import { Client } from 'pg';
import { migrateUp } from '../src/db/migrate';

// 테스트 전용 DB를 새로 만들고 마이그레이션을 적용한다
export default async function globalSetup(): Promise<void> {
  const admin = process.env.TEST_PG_ADMIN_URL;
  if (!admin) {
    throw new Error('TEST_PG_ADMIN_URL 이 없습니다. npm test 로 실행하세요(embedded PostgreSQL을 띄웁니다).');
  }
  const dbName = 'dotrpg_test';
  const client = new Client({ connectionString: admin });
  await client.connect();
  await client.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
  await client.query(`CREATE DATABASE ${dbName}`);
  await client.end();

  const url = new URL(admin);
  url.pathname = `/${dbName}`;
  process.env.TEST_DATABASE_URL = url.toString();
  // migrations/ 의 모든 마이그레이션(0007 포함)을 적용한다
  await migrateUp(url.toString());
}
