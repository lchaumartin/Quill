// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// The few UnityEngine types the engine's parser and element registry touch, so they compile outside
// Unity. Logging goes to stderr: stdout carries the LSP stream.
using System;
using System.Globalization;
namespace UnityEngine {
  public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
    public static Color magenta => new Color(1,0,1,1);
    public static Color white => new Color(1,1,1,1);
    public static Color LerpUnclamped(Color x, Color y, float t)=>new Color(x.r+(y.r-x.r)*t,x.g+(y.g-x.g)*t,x.b+(y.b-x.b)*t,x.a+(y.a-x.a)*t);
    public static bool operator ==(Color x, Color y) => Math.Abs(x.r-y.r)<1e-4f && Math.Abs(x.g-y.g)<1e-4f && Math.Abs(x.b-y.b)<1e-4f && Math.Abs(x.a-y.a)<1e-4f;
    public static bool operator !=(Color x, Color y) => !(x==y);
    public override bool Equals(object o) => o is Color c && c.r==r && c.g==g && c.b==b && c.a==a;
    public override int GetHashCode() => r.GetHashCode()^g.GetHashCode()^b.GetHashCode()^a.GetHashCode();
    public override string ToString()=>$"RGBA({r:0.###},{g:0.###},{b:0.###},{a:0.###})"; }
  public static class ColorUtility { public static bool TryParseHtmlString(string s, out Color c){ c=default; if(s=="white"){c=new Color(1,1,1,1);return true;} if(s=="black"){c=new Color(0,0,0,1);return true;} if(s=="red"){c=new Color(1,0,0,1);return true;} if(s==null||s.Length<2||s[0]!='#')return false; var h=s.Substring(1); if(h.Length!=6&&h.Length!=8)return false; if(!uint.TryParse(h,NumberStyles.HexNumber,null,out var v))return false; if(h.Length==6)v=(v<<8)|0xFF; c=new Color(((v>>24)&255)/255f,((v>>16)&255)/255f,((v>>8)&255)/255f,(v&255)/255f); return true; } }
  public static class Debug { public static int Warnings; public static System.Collections.Generic.List<string> Log_ = new(); public static void Log(object o){Log_.Add(o.ToString());} public static void LogWarning(object o){Warnings++;Console.Error.WriteLine("WARN "+o);} public static void LogError(object o){Warnings++;Console.Error.WriteLine("ERR "+o);} }
  public static class Random { public static float value=>0.5f; }
  public static class Mathf { public static float Exp(float x)=>(float)Math.Exp(x); public static float Max(float a,float b)=>Math.Max(a,b); public static float Abs(float a)=>Math.Abs(a); public static float Clamp01(float a)=>Math.Clamp(a,0,1); public static int RoundToInt(float f)=>(int)Math.Round(f); public static int Max(int a,int b)=>Math.Max(a,b);}
}
