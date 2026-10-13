import { setTranslationsForTest, translate } from '../src/utils/i18n';

describe('[I18N] 서버 메시지 번역', () => {
  beforeAll(() =>
    setTranslationsForTest({
      '정지된 계정입니다.': 'This account is suspended.',
      '레벨 {0}부터 착용할 수 있습니다.': 'Requires Lv.{0} to equip.',
      '{0}골드가 부족합니다 ({1}).': 'Not enough Gold: {0} ({1}).',
    }),
  );
  afterAll(() => setTranslationsForTest(null));

  it('한국어 요청과 헤더 없는 요청은 그대로', () => {
    expect(translate(undefined, '정지된 계정입니다.')).toBe('정지된 계정입니다.');
    expect(translate('ko', '정지된 계정입니다.')).toBe('정지된 계정입니다.');
  });

  it('영어 요청은 표로 바꾼다', () => {
    expect(translate('en', '정지된 계정입니다.')).toBe('This account is suspended.');
  });

  it('숫자가 든 문장은 자리표시자 키로 찾고 숫자를 다시 넣는다', () => {
    expect(translate('en', '레벨 30부터 착용할 수 있습니다.')).toBe('Requires Lv.30 to equip.');
    expect(translate('en', '1,200골드가 부족합니다 (3).')).toBe('Not enough Gold: 1,200 (3).');
  });

  it('표에 없으면 한국어 그대로', () => {
    expect(translate('en', '처음 보는 문장입니다.')).toBe('처음 보는 문장입니다.');
  });
});
