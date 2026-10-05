using UnityEngine;

namespace DotRPG
{
    /// <summary>Confirmed-hit observer. A short displaced silhouette gives recoil without changing physics or AI.</summary>
    public sealed class CareerImpact : MonoBehaviour
    {
        static int count;static float cameraReady;
        public static int Count=>count;
        public static int ConfirmedHits {get;private set;}
        PlayerController source;string map;SpriteRenderer image;Vector2 start,dir;float age;Color tint;
        public static void Show(CareerSkill s,EnemyController enemy,Vector2 direction,PlayerController owner,int stage=0)
        {
            if(s==null||enemy==null)return;
            ConfirmedHits++;
            var art=s.effect=="eclipse"?CareerCatalog.Get(stage==0?"m_fire":stage==1?"m_ice":"m_storm"):s;
            CareerEffect.Hit(art,enemy.Center,direction,owner);
            // Camera response is deterministic and respects the existing accessibility preference.
            bool heavy=s.effect=="f_drop"||s.effect=="f_eruption"||s.effect=="f_convergence"||s.effect=="five"||s.effect=="execute"||s.effect=="fire"||s.effect=="bash"||s.effect=="counter"||s.effect=="eclipse";
            if(heavy&&owner!=null&&owner.IsLocal&&Time.unscaledTime>=cameraReady){
                cameraReady=Time.unscaledTime+.12f;
                Game.Camera?.CareerImpulse(direction,s.kind==CareerSkillKind.Awakening?.085f:.045f,.11f);
            }
            if(count>=32)return;
            var body=enemy.GetComponent<CharacterAnimator>()?.Renderer;if(body==null||body.sprite==null)return;
            var go=new GameObject("Career hit silhouette");if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);
            var v=go.AddComponent<CareerImpact>();v.source=owner;v.map=Game.Session.MapId;v.start=body.transform.position;v.dir=direction.normalized;
            v.image=go.AddComponent<SpriteRenderer>();v.image.sprite=body.sprite;v.image.flipX=body.flipX;v.image.flipY=body.flipY;v.image.sortingOrder=body.sortingOrder+1;
            go.transform.position=body.transform.position;go.transform.localScale=body.transform.lossyScale;
            v.tint=CareerCatalog.Color(s.career);v.tint.a=.32f;v.image.color=v.tint;count++;
        }
        void Update()
        {
            age+=Time.deltaTime;
            if(age>=.15f||Game.Session.MapId!=map||source==null||source.IsDead||!source.gameObject.activeInHierarchy){Destroy(gameObject);return;}
            float t=age/.15f,offset=Mathf.Sin(t*Mathf.PI)*.1f;
            var at=start+dir*offset;at.x=Mathf.Round(at.x*96)/96;at.y=Mathf.Round(at.y*96)/96;transform.position=at;
            var c=tint;c.a*=1-t;image.color=c;
        }
        void OnDestroy(){count=Mathf.Max(0,count-1);}
    }
}
