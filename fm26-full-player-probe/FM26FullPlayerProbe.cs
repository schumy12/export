using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.4.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.4 - F7 = binding probe");
            _behaviour = AddComponent<ProbeBehaviour>();
        }
        public override bool Unload()
        {
            if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour);
            return base.Unload();
        }
    }

    public sealed class ProbeBehaviour : MonoBehaviour
    {
        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try { if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) Probe(); }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] " + ex); }
        }

        private void Probe()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.4 BINDINGS ===");
            var root = MainRoot();
            if (root == null) { Line(sb, "ERROR: main UI root not found"); Save(sb); return; }
            var table = Find(root, "playertable") ?? Find(root, "client-object-viewer-table");
            if (table == null) { Line(sb, "ERROR: player table not found"); Save(sb); return; }
            var view = Find(table, "View");
            if (view == null) { Line(sb, "ERROR: View not found"); Save(sb); return; }

            Line(sb, "Visible rows: " + SafeChildCount(view));
            DumpElementContext(table, sb, "TABLE");
            DumpElementContext(view, sb, "VIEW");

            int rows = Math.Min(3, SafeChildCount(view));
            for (int i = 0; i < rows; i++)
            {
                VisualElement row = null;
                try { row = view.ElementAt(i); } catch { }
                if (row == null) continue;
                Line(sb, "\n=== ROW " + i + " BINDING NODES ===");
                DumpBindingNodes(row, sb, "row[" + i + "]", 0, 9);

                var show = Find(row, "ShowPerson");
                if (show != null)
                {
                    Line(sb, "\n--- ShowPerson context row " + i + " ---");
                    DumpElementContext(show, sb, "ShowPerson");
                    DumpAncestors(show, sb);
                }
                else Line(sb, "ShowPerson not found in row " + i);
            }

            Line(sb, "\n=== BINDING-RELATED TYPES ===");
            DumpTypeMatches(sb, new[]{"BindingExpect","EmbeddedDataHandler","PersonReferenceClickedHandler","PersonReference","DataReferenceHandler","Bindings"}, 100);
            Save(sb);
        }

        private static void DumpBindingNodes(VisualElement el, StringBuilder sb, string path, int depth, int maxDepth)
        {
            if (el == null || depth > maxDepth) return;
            string name = SafeName(el);
            int bindCount = 0;
            try { if (el.bindings != null) bindCount = el.bindings.Count; } catch { }
            bool hit = bindCount > 0 || name == "ShowPerson" || name == "BindingExpect";
            if (hit)
            {
                Line(sb, new string(' ', depth*2) + path + " name='" + name + "' type=" + SafeType(el) + " bindings=" + bindCount);
                DumpElementContext(el, sb, new string(' ', depth*2) + "  context");
                if (bindCount > 0)
                {
                    for (int b = 0; b < bindCount; b++)
                    {
                        object binding = null;
                        try { binding = el.bindings[b]; } catch (Exception ex) { Line(sb, "    binding["+b+"] <"+ex.GetType().Name+">"); }
                        if (binding == null) continue;
                        Line(sb, "    binding[" + b + "] type=" + SafeType(binding));
                        DumpTypeMetadata(binding.GetType(), sb, "      ");
                    }
                }
            }
            int n = SafeChildCount(el);
            for (int i=0;i<n;i++)
            {
                VisualElement c=null; try { c=el.ElementAt(i); } catch { }
                DumpBindingNodes(c,sb,path+"/"+i,depth+1,maxDepth);
            }
        }

        private static void DumpElementContext(VisualElement el, StringBuilder sb, string label)
        {
            if (el == null) return;
            string dsType="", dsPath=""; object ds=null, ud=null;
            try { dsType = el.dataSourceTypeString ?? ""; } catch(Exception ex){ dsType="<"+ex.GetType().Name+">"; }
            try { dsPath = el.dataSourcePathString ?? ""; } catch(Exception ex){ dsPath="<"+ex.GetType().Name+">"; }
            try { ds = el.dataSource; } catch(Exception ex){ Line(sb,label+" dataSource=<"+ex.GetType().Name+">"); }
            try { ud = el.userData; } catch(Exception ex){ Line(sb,label+" userData=<"+ex.GetType().Name+">"); }
            int bc=0; try { if(el.bindings!=null) bc=el.bindings.Count; } catch { }
            Line(sb, label + " name='"+SafeName(el)+"' type="+SafeType(el)+" bindings="+bc+" dsType='"+dsType+"' dsPath='"+dsPath+"' ds="+SafeType(ds)+" userData="+SafeType(ud));
        }

        private static void DumpAncestors(VisualElement el, StringBuilder sb)
        {
            VisualElement p = el;
            for (int i=0;i<10 && p!=null;i++)
            {
                DumpElementContext(p,sb,"ancestor["+i+"]");
                try { p = p.parent; } catch { p=null; }
            }
        }

        private static void DumpTypeMatches(StringBuilder sb, string[] needles, int max)
        {
            int count=0;
            foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an=""; try { an=a.GetName().Name??""; } catch { continue; }
                if(!(an.StartsWith("FM")||an.StartsWith("SI")||an.StartsWith("Unity"))) continue;
                Type[] types; try { types=a.GetTypes(); } catch(ReflectionTypeLoadException e){types=e.Types;} catch{continue;}
                if(types==null) continue;
                foreach(var t in types)
                {
                    if(t==null) continue;
                    string fn=t.FullName??""; bool ok=false;
                    foreach(var n in needles) if(fn.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0){ok=true;break;}
                    if(!ok) continue;
                    Line(sb,"TYPE "+an+": "+fn);
                    DumpTypeMetadata(t,sb,"  ");
                    if(++count>=max) return;
                }
            }
        }

        private static void DumpTypeMetadata(Type t, StringBuilder sb, string pad)
        {
            var flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            try { foreach(var p in t.GetProperties(flags)) Line(sb,pad+"PROP "+p.Name+" : "+SafeTypeName(p.PropertyType)); } catch { }
            try { foreach(var f in t.GetFields(flags)) Line(sb,pad+"FIELD "+f.Name+" : "+SafeTypeName(f.FieldType)); } catch { }
            try { foreach(var m in t.GetMethods(flags)) if(InterestingMethod(m.Name)) Line(sb,pad+"METHOD "+m.Name); } catch { }
        }

        private static bool InterestingMethod(string s)
        {
            string n=(s??"").ToLowerInvariant();
            return n.Contains("bind")||n.Contains("data")||n.Contains("person")||n.Contains("player")||n.Contains("value")||n.Contains("reference")||n.Contains("context")||n.Contains("property");
        }

        private VisualElement MainRoot()
        {
            try
            {
                var docs=FindObjectsOfType<UIDocument>();
                foreach(var doc in docs)
                {
                    if(doc==null) continue; VisualElement r=null; try{r=doc.rootVisualElement;}catch{}
                    if(r!=null && SafeName(r)=="PanelManager-container") return r;
                }
            }
            catch { }
            return null;
        }

        private static VisualElement Find(VisualElement root,string name)
        {
            if(root==null) return null; if(SafeName(root)==name) return root;
            int n=SafeChildCount(root); for(int i=0;i<n;i++){VisualElement c=null;try{c=root.ElementAt(i);}catch{} var x=Find(c,name);if(x!=null)return x;}
            return null;
        }

        private static string SafeName(VisualElement el){try{return el?.name??"";}catch{return "<unreadable>";}}
        private static int SafeChildCount(VisualElement el){try{return el?.childCount??0;}catch{return 0;}}
        private static string SafeType(object o){try{return o==null?"<null>":(o.GetType().FullName??o.GetType().Name);}catch{return "<unknown>";}}
        private static string SafeTypeName(Type t){try{return t?.FullName??t?.Name??"?";}catch{return "?";}}
        private static void Line(StringBuilder sb,string s){sb.AppendLine(s);try{Plugin.Log.LogInfo("[FM26FullProbe] "+s);}catch{}}
        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Sports Interactive","Football Manager 26","FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file=Path.Combine(dir,"probe_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");
                File.WriteAllText(file,sb.ToString(),Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: "+file);
            }
            catch(Exception ex){Plugin.Log.LogError("[FM26FullProbe] Save failed: "+ex);}
        }
    }
}
