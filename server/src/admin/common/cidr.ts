import { BlockList, isIP } from 'node:net';

/** 출처 IP가 허용 CIDR 목록 안에 있는가. IPv4-mapped IPv6(::ffff:a.b.c.d)는 IPv4로 본다 */
export function ipAllowed(ip: string | undefined, cidrs: string[]): boolean {
  if (!ip) return false;
  const norm = ip.startsWith('::ffff:') ? ip.slice(7) : ip;
  const fam = isIP(norm);
  if (fam === 0) return false;
  const list = new BlockList();
  for (const c of cidrs) {
    const [addr, bits] = c.split('/');
    if (!addr) continue;
    const f = isIP(addr);
    if (f === 0) continue;
    const prefix = bits === undefined ? (f === 4 ? 32 : 128) : Number(bits);
    if (!Number.isInteger(prefix)) continue;
    list.addSubnet(addr, prefix, f === 4 ? 'ipv4' : 'ipv6');
  }
  return list.check(norm, fam === 4 ? 'ipv4' : 'ipv6');
}
