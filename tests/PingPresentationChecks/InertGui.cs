using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine
{
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} public static Color white => new(1,1,1,1); }
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
        public bool Contains(Vector2 point) => point.x>=x && point.y>=y && point.x<x+width && point.y<y+height;
    }
    public class Texture2D {
        public static int Created, Destroyed; public int Width, Height; public byte[] Pixels; public FilterMode filterMode;
        public Texture2D(){} public Texture2D(int w,int h,TextureFormat f,bool mip){Width=w;Height=h;Created++;}
        public void SetPixels32(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> pixels) {
            Pixels=pixels.Values.SelectMany(c=>new[]{c.r,c.g,c.b,c.a}).ToArray();
        }
        public void Apply(bool mip,bool unreadable){} public static Texture2D whiteTexture = new();
    }
    public enum FilterMode { Bilinear }
    public enum TextureFormat { RGBA32 }
    public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
    public class GameObject { public bool activeInHierarchy=true; }
    public class Camera {
        public bool enabled=true; public GameObject gameObject=new(); public int targetDisplay;
        public static bool InvalidProjection;
        public Vector3 Offset; public float Zoom=40;
        public readonly List<Vector3> Inputs=new();
        public Vector3 WorldToScreenPoint(Vector3 p) { Inputs.Add(p); return InvalidProjection?new(float.NaN,0,1):
            new(Screen.width/2+(p.x-Offset.x)*Zoom,Screen.height/2+(p.z-Offset.z)*Zoom*.45f+(p.y-Offset.y)*Zoom*.75f,1); }
    }
    public static class Object { public static void Destroy(Texture2D t){Texture2D.Destroyed++;} }

    public class Font { }
    public class RectOffset { public RectOffset(int a,int b,int c,int d){} }
    public enum ScaleMode { StretchToFill }
    public enum FontStyle { Normal, Bold }
    public enum TextClipping { Clip }
    public enum TextAnchor { MiddleLeft, MiddleCenter, MiddleRight, UpperLeft }
    public class GUIStyleState { public Texture2D background; public Color textColor = Color.white; }
    public class GUIStyle
    {
        public Vector2 CalcSize(GUIContent content) => new(content.text.Sum(c => (c == 'W' ? 1f : c == ' ' ? .3f : .62f) * fontSize), fontSize * 1.16f);
        public Font font; public int fontSize=14; public FontStyle fontStyle; public TextAnchor alignment;
        public bool wordWrap,richText; public RectOffset padding,margin,border; public TextClipping clipping;
        public GUIStyleState normal=new(),hover=new(),active=new(),focused=new(),onNormal=new(),onHover=new(),onActive=new(),onFocused=new();
    }
    public class GUISkin { public GUIStyle label=new(),window=new(); public Font font=new(); }
    public class GUIContent { public string text; }
    public class Event { public static Event current=new(); public Vector2 mousePosition=new(-100,-100); }
    public static class Screen { public static int width,height; }
    public static class Time { public static double realtimeSinceStartupAsDouble => 100; }
    public static class GUI
    {
        public static Color color=Color.white,contentColor=Color.white;
        public static bool enabled=true; public static int depth; public static GUISkin skin=new();
        public static List<object> Commands=new();
        public static void DrawTexture(Rect r,Texture2D texture,ScaleMode mode,bool alpha) => Commands.Add(new { kind="rect",r.x,r.y,r.width,r.height,textureWidth=texture.Width,textureHeight=texture.Height,pixels=texture.Pixels==null?null:Convert.ToBase64String(texture.Pixels),color=new[]{color.r,color.g,color.b,color.a} });
        public static void Label(Rect r,string text,GUIStyle style) => Commands.Add(new { kind="text",r.x,r.y,r.width,r.height,text,size=style.fontSize,bold=style.fontStyle==FontStyle.Bold,align=style.alignment.ToString(),wrap=style.wordWrap,color=new[]{style.normal.textColor.r,style.normal.textColor.g,style.normal.textColor.b,style.normal.textColor.a} });
        public static bool Button(Rect r,GUIContent content,GUIStyle style){ if(!string.IsNullOrEmpty(content.text)) Label(r,content.text,style); return false; }
    }
    public static class GUILayout
    {
        public static void BeginArea(Rect r,string text="",GUIStyle style=null){} public static void EndArea(){}
        public static bool Button(string text)=>false; public static void Label(string text){} public static void Space(float size){}
        public static bool Toggle(bool value,string text)=>value; public static void BeginHorizontal(){} public static void EndHorizontal(){}
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
    public class Il2CppStructArray<T> { public T[] Values; public Il2CppStructArray(int n){Values=new T[n];} public static explicit operator Il2CppStructArray<T>(T[] source)=>new(source.Length){Values=(T[])source.Clone()}; public T this[int i]{get=>Values[i];set=>Values[i]=value;} }
}
