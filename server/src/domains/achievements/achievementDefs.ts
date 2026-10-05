// 업적 정의(서버가 원본): 달성하면 같은 이름의 칭호를 채팅 이름 앞에 달 수 있다.
// 판정은 모두 서버 기록으로 한다(클라이언트 값은 믿지 않는다). stat은 achievementRepository.statsOf가 계산한다.

export type AchievementStat =
  | 'enhance_best' // 강화로 도달한 가장 높은 단계
  | 'enhance_tries' // 강화 시도 횟수
  | 'enhance_gold' // 강화에 쓴 골드 합
  | 'enhance_destroyed' // 강화로 부서진 횟수
  | 'kills' // 처치 수
  | 'boss_kills' // 보스 처치 수
  | 'dungeon_clears' // 던전(레이드 포함) 클리어 수
  | 'party_clears' // 파티 판 클리어 수
  | 'raid_clears' // 레이드 클리어 수
  | 'level'
  | 'quests' // 보상을 받은 퀘스트 수
  | 'promoted' // 전직했으면 1
  | 'gold' // 지금 가진 골드
  | 'gacha_pulls' // 별조각 뽑기 횟수(계정)
  | 'gacha_top' // 뽑기에서 유니크·레전더리를 얻은 횟수(계정)
  | 'cosmetics' // 가진 외형 수(계정)
  | 'skins' // 가진 코스튬 스킨 수(계정)
  | 'stars_spent'; // 뽑기·교환에 쓴 별조각(계정)

/** 업적 창의 탭 */
export type AchievementCategory = 'growth' | 'combat' | 'dungeon' | 'enhance' | 'wealth' | 'cash';

export interface AchievementDef {
  id: string;
  /** 업적 이름이자 칭호 */
  title: string;
  description: string;
  stat: AchievementStat;
  goal: number;
  category: AchievementCategory;
}

const a = (category: AchievementCategory, id: string, title: string, description: string, stat: AchievementStat, goal: number): AchievementDef =>
  ({ id, title, description, stat, goal, category });

export const ACHIEVEMENTS: AchievementDef[] = [
  // 성장
  a('growth', 'level_10', '첫걸음', '레벨 10을 달성한다', 'level', 10),
  a('growth', 'level_20', '숙련 모험가', '레벨 20을 달성한다', 'level', 20),
  a('growth', 'level_30', '베테랑', '레벨 30을 달성한다', 'level', 30),
  a('growth', 'level_40', '정점에 선 자', '최고 레벨 40을 달성한다', 'level', 40),
  a('growth', 'promoted', '새로운 길', '전직한다', 'promoted', 1),
  a('growth', 'quests_10', '해결사', '퀘스트 10개를 완료한다', 'quests', 10),
  a('growth', 'quests_20', '믿음직한 손', '퀘스트 20개를 완료한다', 'quests', 20),
  a('growth', 'quests_30', '마을의 영웅', '퀘스트 30개를 완료한다', 'quests', 30),
  // 전투
  a('combat', 'kills_100', '사냥 입문', '몬스터 100마리를 처치한다', 'kills', 100),
  a('combat', 'kills_1000', '해골 사냥꾼', '몬스터 1,000마리를 처치한다', 'kills', 1000),
  a('combat', 'kills_5000', '전장의 그림자', '몬스터 5,000마리를 처치한다', 'kills', 5000),
  a('combat', 'kills_10000', '해골의 재앙', '몬스터 10,000마리를 처치한다', 'kills', 10_000),
  a('combat', 'kills_50000', '끝없는 학살자', '몬스터 50,000마리를 처치한다', 'kills', 50_000),
  a('combat', 'boss_1', '보스 사냥꾼', '보스를 처음 쓰러뜨린다', 'boss_kills', 1),
  a('combat', 'boss_30', '보스의 천적', '보스를 30번 쓰러뜨린다', 'boss_kills', 30),
  a('combat', 'boss_100', '왕을 베는 자', '보스를 100번 쓰러뜨린다', 'boss_kills', 100),
  // 던전
  a('dungeon', 'dungeon_1', '첫 공략', '던전을 처음 클리어한다', 'dungeon_clears', 1),
  a('dungeon', 'dungeon_10', '던전 탐험가', '던전을 10번 클리어한다', 'dungeon_clears', 10),
  a('dungeon', 'dungeon_30', '던전 단골', '던전을 30번 클리어한다', 'dungeon_clears', 30),
  a('dungeon', 'dungeon_100', '던전의 주인', '던전을 100번 클리어한다', 'dungeon_clears', 100),
  a('dungeon', 'party_1', '첫 동료', '파티로 던전을 처음 클리어한다', 'party_clears', 1),
  a('dungeon', 'party_10', '함께라면', '파티로 던전을 10번 클리어한다', 'party_clears', 10),
  a('dungeon', 'party_50', '전우', '파티로 던전을 50번 클리어한다', 'party_clears', 50),
  a('dungeon', 'raid_1', '레이드 정복자', '레이드를 처음 클리어한다', 'raid_clears', 1),
  a('dungeon', 'raid_10', '레이드 상습범', '레이드를 10번 클리어한다', 'raid_clears', 10),
  a('dungeon', 'raid_30', '공략대장', '레이드를 30번 클리어한다', 'raid_clears', 30),
  // 강화
  a('enhance', 'enhance_5', '반짝이는 장비', '장비를 +5까지 강화한다', 'enhance_best', 5),
  a('enhance', 'enhance_10', '강화의 시작', '장비를 +10까지 강화한다', 'enhance_best', 10),
  a('enhance', 'enhance_12', '강화 장인', '장비를 +12까지 강화한다', 'enhance_best', 12),
  a('enhance', 'enhance_15', '전설의 대장장이', '장비를 +15까지 강화한다', 'enhance_best', 15),
  a('enhance', 'enhance_tries_50', '망치질 50번', '강화를 50번 시도한다', 'enhance_tries', 50),
  a('enhance', 'enhance_tries_300', '쉬지 않는 모루', '강화를 300번 시도한다', 'enhance_tries', 300),
  a('enhance', 'enhance_gold_100k', '아낌없는 투자자', '강화에 골드 100,000을 쓴다', 'enhance_gold', 100_000),
  a('enhance', 'enhance_gold_1m', '골드를 녹이는 자', '강화에 골드 1,000,000을 쓴다', 'enhance_gold', 1_000_000),
  a('enhance', 'enhance_broken', '부서진 꿈', '강화하다 장비를 부순다', 'enhance_destroyed', 1),
  a('enhance', 'enhance_broken_10', '불굴의 도전자', '강화하다 장비를 10번 부순다', 'enhance_destroyed', 10),
  // 재화
  a('wealth', 'gold_100k', '두둑한 주머니', '골드 100,000을 모은다', 'gold', 100_000),
  a('wealth', 'gold_1m', '부자', '골드 1,000,000을 모은다', 'gold', 1_000_000),
  a('wealth', 'gold_10m', '황금왕', '골드 10,000,000을 모은다', 'gold', 10_000_000),
  // 캐시샵
  a('cash', 'gacha_1', '첫 뽑기', '별조각 뽑기를 처음 한다', 'gacha_pulls', 1),
  a('cash', 'gacha_100', '뽑기 애호가', '별조각 뽑기를 100회 한다', 'gacha_pulls', 100),
  a('cash', 'gacha_500', '별을 쫓는 자', '별조각 뽑기를 500회 한다', 'gacha_pulls', 500),
  a('cash', 'gacha_top_1', '행운의 별', '뽑기에서 유니크 이상을 처음 얻는다', 'gacha_top', 1),
  a('cash', 'gacha_top_10', '별이 내린 자', '뽑기에서 유니크 이상을 10번 얻는다', 'gacha_top', 10),
  a('cash', 'cosmetics_5', '멋쟁이', '외형 5개를 모은다', 'cosmetics', 5),
  a('cash', 'cosmetics_12', '옷장 부자', '외형 12개를 모은다', 'cosmetics', 12),
  a('cash', 'skin_1', '새 옷', '코스튬 스킨을 처음 얻는다', 'skins', 1),
  a('cash', 'skin_4', '코스튬 컬렉터', '코스튬 스킨 4종을 모두 모은다', 'skins', 4),
  a('cash', 'stars_10k', '별조각 큰손', '뽑기와 교환에 별조각 10,000을 쓴다', 'stars_spent', 10_000),
];
export const ACHIEVEMENT_BY_ID = new Map(ACHIEVEMENTS.map((a) => [a.id, a] as const));
