import { getReady } from '../domains/system/systemService';

/** 감시자 자신이 보는 ready 상태(종료 중이면 실패로 치지 않는다: 종료는 정상 동작) */
export async function getDomainReady(): Promise<boolean> {
  const r = await getReady();
  const checks = (r.data as { checks?: { shutting_down?: boolean } }).checks;
  return r.ok || checks?.shutting_down === true;
}
