namespace UnityEngine {
  public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
  public static class Debug { public static void LogWarning(object o)=>System.Console.WriteLine("WARN "+o); }
  public class TooltipAttribute : System.Attribute { public TooltipAttribute(string s){} }
}
