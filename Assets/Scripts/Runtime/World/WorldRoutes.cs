using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    /// <summary>Four surface regions form a ring; the central town opens a separate descending world.</summary>
    public static class WorldRoutes
    {
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
            entrance.southMap = "hollow_fungal";
            foreach (var id in new[] { "hollow_roots", "hollow_fungal" })
            {
                var branch = Get(id);
                branch.previousMap = entrance.id;
                branch.nextMap = "hollow_depths";
            }
            var depths = Get("hollow_depths");
            depths.northMap = "hollow_roots";
            depths.southMap = "hollow_fungal";
        }
    }
}
