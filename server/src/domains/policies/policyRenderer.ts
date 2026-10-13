// 정책 문서 Markdown -> HTML. 외부 라이브러리 없이 정책 문서에 쓰는 문법만 다룬다:
// 제목(#~######), 문단, '- ' 목록, '1. ' 목록, 파이프 표, **굵게**, [글](주소) 링크. 모든 HTML은 먼저 이스케이프한다.
import type { PolicyLang } from './policyVersions';

const escapeHtml = (s: string): string =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');

/** {{key}}를 운영 정보로 바꾼다. 빈 값은 (미정)/(TBD) */
export function fillPlaceholders(md: string, values: Record<string, string>, lang: PolicyLang): string {
  const empty = lang === 'ko' ? '(미정)' : '(TBD)';
  return md.replace(/\{\{\s*([a-z0-9_]+)\s*\}\}/gi, (_m, key: string) => {
    const v = values[key];
    return typeof v === 'string' && v.trim() ? v.trim() : empty;
  });
}

/** 이미 이스케이프한 한 줄의 인라인 문법 */
function inline(escaped: string): string {
  return escaped
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_m, text: string, url: string) => {
      // 허용: http(s), mailto, 같은 사이트 경로, 문서 안 앵커. 그 밖(javascript: 등)은 글자만 남긴다
      if (!/^(https?:\/\/|mailto:|\/|#)/i.test(url)) return text;
      return `<a href="${url}">${text}</a>`;
    });
}

const splitRow = (line: string): string[] =>
  line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim());

const isTableSep = (line: string): boolean => /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(line);

export function renderMarkdown(md: string): string {
  const lines = escapeHtml(md.replace(/\r\n?/g, '\n')).split('\n');
  const out: string[] = [];
  let para: string[] = [];
  let list: { tag: 'ul' | 'ol'; items: string[] } | null = null;
  const flushPara = (): void => {
    if (para.length) out.push(`<p>${inline(para.join(' '))}</p>`);
    para = [];
  };
  const flushList = (): void => {
    if (list) out.push(`<${list.tag}>${list.items.map((i) => `<li>${inline(i)}</li>`).join('')}</${list.tag}>`);
    list = null;
  };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] as string;
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
    const ordered = /^\s*\d+\.\s+(.*)$/.exec(line);
    if (!line.trim()) {
      flushPara();
      flushList();
    } else if (heading) {
      flushPara();
      flushList();
      const level = (heading[1] as string).length;
      out.push(`<h${level}>${inline(heading[2] as string)}</h${level}>`);
    } else if (line.trim().startsWith('|') && i + 1 < lines.length && isTableSep(lines[i + 1] as string)) {
      flushPara();
      flushList();
      const head = splitRow(line);
      const rows: string[][] = [];
      i += 2;
      while (i < lines.length && (lines[i] as string).trim().startsWith('|')) rows.push(splitRow(lines[i++] as string));
      i--;
      out.push(
        '<div class="table"><table><thead><tr>' +
          head.map((c) => `<th>${inline(c)}</th>`).join('') +
          '</tr></thead><tbody>' +
          rows.map((r) => `<tr>${r.map((c) => `<td>${inline(c)}</td>`).join('')}</tr>`).join('') +
          '</tbody></table></div>',
      );
    } else if (bullet || ordered) {
      flushPara();
      const tag = bullet ? 'ul' : 'ol';
      if (!list || list.tag !== tag) {
        flushList();
        list = { tag, items: [] };
      }
      list.items.push(((bullet ?? ordered) as RegExpExecArray)[1] as string);
    } else if (list && /^\s{2,}\S/.test(line)) {
      // 목록 항목의 이어지는 줄
      list.items[list.items.length - 1] += ' ' + line.trim();
    } else {
      flushList();
      para.push(line.trim());
    }
  }
  flushPara();
  flushList();
  return out.join('\n');
}

const CSS = `
:root{--bg:#ffffff;--fg:#1d1f24;--muted:#5d6472;--line:#d9dde4;--head:#f3f5f8;--link:#1f5fbf}
@media (prefers-color-scheme: dark){:root{--bg:#15171c;--fg:#e6e8ec;--muted:#a3a9b5;--line:#30343d;--head:#1f2229;--link:#7fb2ff}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.7 system-ui,-apple-system,"Segoe UI","Malgun Gothic","Apple SD Gothic Neo",sans-serif}
main{max-width:860px;margin:0 auto;padding:24px 16px 64px}
h1{font-size:1.6rem;margin:0 0 16px}h2{font-size:1.25rem;margin:32px 0 8px}h3{font-size:1.05rem;margin:24px 0 6px}
p,li{color:var(--fg)}a{color:var(--link)}
.table{overflow-x:auto;margin:12px 0}
table{border-collapse:collapse;width:100%;font-size:.95rem}
th,td{border:1px solid var(--line);padding:6px 10px;text-align:left;vertical-align:top}
th{background:var(--head)}
footer{margin-top:40px;color:var(--muted);font-size:.85rem}
`;

/** 문서 한 장(외부 리소스 없음, 밝은·어두운 화면, 휴대폰 폭) */
export function renderPage(title: string, bodyHtml: string, lang: PolicyLang, footer: string): string {
  return (
    `<!doctype html><html lang="${lang}"><head><meta charset="utf-8">` +
    '<meta name="viewport" content="width=device-width,initial-scale=1">' +
    `<title>${escapeHtml(title)}</title><style>${CSS}</style></head>` +
    `<body><main>${bodyHtml}<footer>${escapeHtml(footer)}</footer></main></body></html>`
  );
}
