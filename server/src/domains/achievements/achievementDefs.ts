// 업적 정의(서버가 원본): 달성하면 같은 이름의 칭호를 채팅 이름 앞에 달 수 있다.
// 판정은 모두 서버 기록으로 한다(클라이언트 값은 믿지 않는다). stat은 achievementRepository.statsOf가 계산한다.

export type AchievementStat =
  | 'enhance_best' // 강화로 도달한 가장 높은 단계
  | 'enhance_tries' // 강화 시도 횟수
  | 'enhance_gold' // 강화에 쓴 골드 합
  | 'enhance_destroyed' // 강화로 부서진 횟수
  | 'kills' // 처치 수
  | 'dungeon_clears' // 던전(레이드 포함) 클리어 수
  | 'party_clears' // 파티 판 클리어 수
  | 'raid_clears' // 레이드 클리어 수
  | 'level'
  | 'quests' // 보상을 받은 퀘스트 수
  | 'gold'; // 지금 가진 골드

export interface AchievementDef {
  id: string;
  /** 업적 이름이자 칭호 */
  title: string;
  description: string;
  stat: AchievementStat;
  goal: number;
}

export const ACHIEVEMENTS: AchievementDef[] = [
  { id: 'enhance_10', title: '강화의 시작', description: '장비를 +10까지 강화한다', stat: 'enhance_best', goal: 10 },
  { id: 'enhance_12', title: '강화 장인', description: '장비를 +12까지 강화한다', stat: 'enhance_best', goal: 12 },
  { id: 'enhance_15', title: '전설의 대장장이', description: '장비를 +15까지 강화한다', stat: 'enhance_best', goal: 15 },
  { id: 'enhance_tries_50', title: '망치질 50번', description: '강화를 50번 시도한다', stat: 'enhance_tries', goal: 50 },
  { id: 'enhance_gold_100k', title: '아낌없는 투자자', description: '강화에 골드 100,000을 쓴다', stat: 'enhance_gold', goal: 100_000 },
  { id: 'enhance_gold_1m', title: '골드를 녹이는 자', description: '강화에 골드 1,000,000을 쓴다', stat: 'enhance_gold', goal: 1_000_000 },
  { id: 'enhance_broken', title: '부서진 꿈', description: '강화하다 장비를 부순다', stat: 'enhance_destroyed', goal: 1 },
  { id: 'kills_1000', title: '해골 사냥꾼', description: '몬스터 1,000마리를 처치한다', stat: 'kills', goal: 1000 },
  { id: 'kills_10000', title: '해골의 재앙', description: '몬스터 10,000마리를 처치한다', stat: 'kills', goal: 10_000 },
  { id: 'dungeon_1', title: '첫 공략', description: '던전을 처음 클리어한다', stat: 'dungeon_clears', goal: 1 },
  { id: 'dungeon_30', title: '던전 단골', description: '던전을 30번 클리어한다', stat: 'dungeon_clears', goal: 30 },
  { id: 'party_10', title: '함께라면', description: '파티로 던전을 10번 클리어한다', stat: 'party_clears', goal: 10 },
  { id: 'raid_1', title: '레이드 정복자', description: '레이드를 처음 클리어한다', stat: 'raid_clears', goal: 1 },
  { id: 'level_20', title: '숙련 모험가', description: '레벨 20을 달성한다', stat: 'level', goal: 20 },
  { id: 'level_40', title: '정점에 선 자', description: '최고 레벨 40을 달성한다', stat: 'level', goal: 40 },
  { id: 'quests_10', title: '해결사', description: '퀘스트 10개를 완료한다', stat: 'quests', goal: 10 },
  { id: 'quests_30', title: '마을의 영웅', description: '퀘스트 30개를 완료한다', stat: 'quests', goal: 30 },
  { id: 'gold_1m', title: '부자', description: '골드 1,000,000을 모은다', stat: 'gold', goal: 1_000_000 },
];

export const ACHIEVEMENT_BY_ID = new Map(ACHIEVEMENTS.map((a) => [a.id, a] as const));
