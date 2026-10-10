using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    /// <summary>Four surface regions form a ring; the central town opens a separate descending world.</summary>
    public static class WorldRoutes
    {
        // Authored three-wide roads reach the map boundary, with inward-safe arrivals.
        public static UnityEngine.Vector2 HubApproach(string id)
        {
            switch (id)
            {
                case "village": return new UnityEngine.Vector2(.5f, 22.5f);
                case "canyon": return new UnityEngine.Vector2(11.5f, 59.5f);
                case "winter": return new UnityEngine.Vector2(.5f, 25.5f);
                default: return new UnityEngine.Vector2(.5f, 53.5f);
            }
        }
        public static UnityEngine.Vector2 HubInward(string id) => id == "canyon" ? UnityEngine.Vector2.down : UnityEngine.Vector2.right;
        // Shared by gameplay and the unvisited-map preview. Only these short town
        // approach strips are carved; all other terrain keeps its authored layout.
        public static string WithHubRoad(string id, string text)
        {
            if (id != "village" && id != "canyon" && id != "winter" && id != MapRegistry.Sanctum) return text;
            var rows = text.Split('\n').Select(r => r.TrimEnd('\r')).Where(r => !r.StartsWith("//") && !string.IsNullOrWhiteSpace(r)).Select(r => r.ToCharArray()).ToArray();
            int h = rows.Length; if (h == 0) return text;
            void Floor(int x, int y)
            {
                if (y < 0 || y >= h || x < 0 || x >= rows[h - 1 - y].Length) return;
                char original = rows[h - 1 - y][x];
                if (char.IsDigit(original) || original == 'P') return; // Keep authored NPCs and spawn anchors.
                rows[h - 1 - y][x] = id == MapRegistry.Sanctum ? '.' : ',';
            }
            if (id == "canyon") { for (int y = 55; y < h; y++) for (int x = 10; x <= 12; x++) Floor(x, y); }
            else
            {
                int cy = id == "village" ? 22 : id == "winter" ? 25 : 53;
                int end = id == "village" ? 12 : id == "winter" ? 6 : 17;
                for (int x = 0; x <= end; x++) for (int y = cy - 1; y <= cy + 1; y++) Floor(x, y);
            }
            return string.Join("\n", rows.Select(r => new string(r)));
        }
        public static readonly string[][] Regions = {
            new[]{"village","forest","forest_ruins","forest_depths","forest_crossing","canyon"},
            new[]{"canyon","canyon_pass","canyon_mine","canyon_ridge","canyon_gate","winter"},
            new[]{"winter","winter_edge","winter_lake","winter_peak","winter_reach",MapRegistry.Sanctum},
            new[]{MapRegistry.Sanctum,"sanctum_hall","sanctum_archive","sanctum_roots","sanctum_court",MapRegistry.Village}
        };
        public static bool Portal(char c)=>c=='<'||c=='>'||c=='['||c==']'||c=='^'||c=='{'||c=='}';
        public static string Target(MapInfo map, char c)
        {
            if (map == null || map.instanced) return null;
            switch (c)
            {
                case '<': return map.previousMap;
                case '>': return map.nextMap;
                case '[': return map.northMap;
                case ']': return map.southMap;
                case '^': return map.hubMap;
                case '{': return map.downMap;
                case '}': return map.upMap;
                default: return null;
            }
        }
        public static IEnumerable<string> Neighbors(string id)
        {
            var m=MapRegistry.Get(id);if(m==null)yield break;
            var seen = new HashSet<string>();
            foreach(var s in new[]{m.previousMap,m.nextMap,m.northMap,m.southMap,m.hubMap,m.downMap,m.upMap})
                if(!string.IsNullOrEmpty(s) && seen.Add(s))yield return s;
        }
        public static string Directions(MapInfo m)
        {
            var parts=new List<string>();
            foreach(var p in new[]{('<',"서쪽"),('>',"동쪽"),('[',"북쪽"),(']',"남쪽"),('^',"뿌리샘 길"),('{',"아래층"),('}',"지상으로")}){
                var id=Target(m,p.Item1);if(id!=null)parts.Add(p.Item2+": "+(MapRegistry.Get(id)?.displayName??id));
            }
            return string.Join(" / ",parts);
        }
        public static void Configure(List<MapInfo> maps)
        {
            MapInfo Get(string id)=>maps.Find(m=>m.id==id);
            foreach(var r in Regions){
                var town=Get(r[0]);town.nextMap=r[1];town.hubMap=MapRegistry.Undergate;
                var one=Get(r[1]);one.previousMap=r[0];one.nextMap=null;one.northMap=r[2];one.southMap=r[3];
                for(int n=2;n<=3;n++){var branch=Get(r[n]);branch.previousMap=r[1];branch.nextMap=r[4];}
                var four=Get(r[4]);four.previousMap=null;four.northMap=r[2];four.southMap=r[3];four.nextMap=r[5];
                Get(r[5]).previousMap=r[4];
                for(int n=1;n<=4;n++)Get(r[n]).hint="사냥터 "+n+" · "+(n==1?"북쪽: 사냥터 2 / 남쪽: 사냥터 3":n==4?"북쪽·남쪽 두 길이 합류 / 동쪽: 다음 지역":"서쪽: 사냥터 1 / 동쪽: 사냥터 4");
            }
            var gate = Get(MapRegistry.Undergate);
            gate.previousMap = MapRegistry.Village;
            gate.northMap = MapRegistry.Canyon;
            gate.nextMap = MapRegistry.Winter;
            gate.southMap = MapRegistry.Sanctum;
            gate.downMap = "hollow_descent";

            var entrance = Get("hollow_descent");
            entrance.upMap = MapRegistry.Undergate;
            entrance.northMap = "hollow_roots";
            entrance.southMap = null;
            var roots = Get("hollow_roots");
            roots.previousMap = entrance.id;
            roots.nextMap = "hollow_fungal";
            var fungal = Get("hollow_fungal");
            fungal.previousMap = roots.id;
            fungal.nextMap = "hollow_depths";
            var depths = Get("hollow_depths");
            depths.northMap = null;
            depths.southMap = fungal.id;
        }
    }
}
