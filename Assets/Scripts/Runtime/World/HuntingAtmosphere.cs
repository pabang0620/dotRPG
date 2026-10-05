using System.Collections.Generic;
using UnityEngine;
namespace DotRPG {
 /// <summary>Bounded local motes, never gameplay projectiles. Destroyed with their map root.</summary>
 public sealed class HuntingAtmosphere:MonoBehaviour {
  static Sprite dot;readonly List<SpriteRenderer> motes=new List<SpriteRenderer>();readonly List<Vector2> origins=new List<Vector2>();float age;MapTheme theme;
  public void Setup(char[,] a,int w,int h,MapTheme kind){theme=kind;if(dot==null){var t=new Texture2D(2,2,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};t.SetPixels(new[]{Color.white,Color.clear,Color.clear,Color.white});t.Apply();dot=Sprite.Create(t,new Rect(0,0,2,2),Vector2.one*.5f,32);}
   for(int y=5;y<h-5;y+=5)for(int x=5;x<w-5;x+=7){if(motes.Count>=24||a[x,y]=='W'||a[x,y]=='%')continue;var go=new GameObject("Ambient mote");go.transform.SetParent(transform,false);var sr=go.AddComponent<SpriteRenderer>();sr.sprite=dot;sr.sortingOrder=-100;origins.Add(new Vector2(x,y));motes.Add(sr);}
  }
  void Update(){if(!Game.IsWorldRunning)return;age+=Time.deltaTime;for(int i=0;i<motes.Count;i++){float phase=age*.4f+i*2.7f;var basePos=origins[i];float sway=Mathf.Sin(phase)*.65f;float rise=theme==MapTheme.Winter?-(age*.23f+i*.4f)%2:Mathf.Sin(phase*.7f)*.5f;motes[i].transform.position=basePos+new Vector2(sway,rise);float alpha=.16f+.18f*(.5f+.5f*Mathf.Sin(phase));motes[i].color=theme==MapTheme.Winter?new Color(.8f,.91f,1,alpha):theme==MapTheme.Canyon?new Color(.94f,.73f,.4f,alpha):new Color(.53f,.86f,.6f,alpha);}}
 }
}
