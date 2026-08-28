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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.7.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.7 - F7 = selected-player data pipeline probe");
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
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.7 DATA PIPELINE ===");

            var root = MainRoot();
            if (root != null)
            {
                var table = Find(root, "playertable") ?? Find(root, "client-object-viewer-table");
                var view = table == null ? null : Find(table, "View");
                if (view != null)
                {
                    int count = SafeChildCount(view);
                    Line(sb, "Visible rows: " + count);
                    VisualElement selectedRow = null;
                    int selectedIndex = -1;
                    for (int i = 0; i < count; i++)
                    {
                        VisualElement row = null;
                        try { row = view.ElementAt(i); } catch { }
                        if (row == null) continue;
                        bool selected = false;
                        try { selected = row.ClassListContains("virtualised-list__item--selected"); } catch { }
                        Line(sb, "ROW " + i + " selected=" + selected);
                        if (selected && selectedRow == null) { selectedRow = row; selectedIndex = i; }
                    }

                    if (selectedRow != null)
                    {
                        Line(sb, "SELECTED ROW INDEX: " + selectedIndex);
                        DumpElementContext(selectedRow, sb, "SelectedRow");
                        var show = Find(selectedRow, "ShowPerson");
                        Line(sb, "Selected ShowPerson=" + (show != null));
                        if (show != null)
                        {
                            DumpElementContext(show, sb, "ShowPerson");
                            DumpAncestors(show, sb);
                        }
                    }
                    else Line(sb, "ERROR: no single selected row marker found. Select exactly one player before F7.");
                }
                else Line(sb, "Player table/View not found; continuing with type metadata.");
            }
            else Line(sb, "Main UI root not found; continuing with type metadata.");

            Line(sb, "\n=== CLICK/DATA PIPELINE TYPES ===");
            string[] exact = {
                "SI.Bindable.EmbeddedDataClickedEvent",
                "SI.Core.Record",
                "SI.Core.TypedValue",
                "SI.Core.ReferenceTypedValue",
                "SI.Core.NumericTypedValue",
                "SI.Bindable.BindingSubsystem",
                "SI.Bindable.Bindings+Key",
                "SI.Bindable.Bindings+Data",
                "FM.UI.EmbeddedDataHandler",
                "FM.UI.EmbeddedDataHandler+PersonReferenceClickedHandler",
                "FM.UI.EmbeddedDataHandler+DataReferenceHandlerBase",
                "FM.UI.EmbeddedDataHandler+DataReferenceHandlerBase+DataRequest",
                "FM.UI.PersonReference",
                "FM.UI.IPlayerReference",
                "FM.UI.PlayerAttributeReference"
            };
            foreach (var n in exact) DumpExactType(sb, n);

            Line(sb, "\n=== RELATED TYPE MATCHES ===");
            DumpTypeMatches(sb, new[] {
                "EmbeddedDataClickedEvent", "ReferenceTypedValue", "NumericTypedValue",
                "Record", "TypedValue", "BindingSubsystem", "PersonReferenceClickedHandler"
            }, 180);

            Save(sb);
        }

        private static void DumpAncestors(VisualElement el, StringBuilder sb)
        {
            VisualElement p = el;
            for (int i = 0; i < 12 && p != null; i++)
            {
                DumpElementContext(p, sb, "ancestor[" + i + "]");
                try { p = p.parent; } catch { p = null; }
            }
        }

        private static void DumpExactType(StringBuilder sb, string fullName)
        {
            Type found = null; string asmName = "";
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = a.GetType(fullName, false); if (t != null) { found = t; asmName = a.GetName().Name ?? ""; break; } }
                catch { }
            }
            if (found == null) { Line(sb, "TYPE NOT FOUND " + fullName); return; }
            Line(sb, "TYPE " + asmName + ": " + found.FullName);
            DumpTypeMetadata(found, sb, "  ", true);
            try { foreach (var nt in found.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) Line(sb, "  NESTED " + nt.FullName); } catch { }
        }

        private static void DumpTypeMatches(StringBuilder sb, string[] needles, int max)
        {
            int count = 0;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = ""; try { an = a.GetName().Name ?? ""; } catch { continue; }
                if (!(an.StartsWith("FM") || an.StartsWith("SI"))) continue;
                Type[] types; try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch { continue; }
                if (types == null) continue;
                foreach (var t in types)
                {
                    if (t == null) continue;
                    string fn = t.FullName ?? ""; bool ok = false;
                    foreach (var n in needles) if (fn.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) { ok = true; break; }
                    if (!ok) continue;
                    Line(sb, "TYPE " + an + ": " + fn);
                    DumpTypeMetadata(t, sb, "  ", false);
                    if (++count >= max) return;
                }
            }
        }

        private static void DumpTypeMetadata(Type t, StringBuilder sb, string pad, bool allMethods)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try { foreach (var p in t.GetProperties(flags)) Line(sb, pad + "PROP " + p.Name + " : " + SafeTypeName(p.PropertyType)); } catch { }
            try { foreach (var f in t.GetFields(flags)) Line(sb, pad + "FIELD " + f.Name + " : " + SafeTypeName(f.FieldType)); } catch { }
            try { foreach (var c in t.GetConstructors(flags)) Line(sb, pad + "CTOR " + SafeMemberString(c)); } catch { }
            try
            {
                foreach (var m in t.GetMethods(flags))
                {
                    if (!allMethods && !InterestingMethod(m.Name)) continue;
                    Line(sb, pad + "METHOD " + SafeMemberString(m));
                }
            }
            catch { }
        }

        private static bool InterestingMethod(string s)
        {
            string n = (s ?? "").ToLowerInvariant();
            return n.Contains("bind") || n.Contains("data") || n.Contains("person") || n.Contains("player") ||
                   n.Contains("value") || n.Contains("reference") || n.Contains("context") || n.Contains("property") ||
                   n.Contains("request") || n.Contains("click") || n.Contains("resolve") || n.Contains("record") ||
                   n.Contains("type") || n.Contains("get") || n.Contains("set");
        }

        private static string SafeMemberString(MethodBase m)
        {
            try { return m == null ? "?" : m.ToString(); } catch { return m?.Name ?? "?"; }
        }

        private static void DumpElementContext(VisualElement el, StringBuilder sb, string label)
        {
            if (el == null) return;
            string dsType = "", dsPath = ""; object ds = null, ud = null;
            try { dsType = el.dataSourceTypeString ?? ""; } catch { }
            try { dsPath = el.dataSourcePathString ?? ""; } catch { }
            try { ds = el.dataSource; } catch { }
            try { ud = el.userData; } catch { }
            int bc = 0; try { if (el.bindings != null) bc = el.bindings.Count; } catch { }
            Line(sb, label + " name='" + SafeName(el) + "' type=" + SafeType(el) + " bindings=" + bc +
                     " dsType='" + dsType + "' dsPath='" + dsPath + "' ds=" + SafeType(ds) + " userData=" + SafeType(ud));
        }

        private VisualElement MainRoot()
        {
            try
            {
                var docs = FindObjectsOfType<UIDocument>();
                foreach (var doc in docs)
                {
                    if (doc == null) continue; VisualElement r = null; try { r = doc.rootVisualElement; } catch { }
                    if (r != null && SafeName(r) == "PanelManager-container") return r;
                }
            }
            catch { }
            return null;
        }

        private static VisualElement Find(VisualElement root, string name)
        {
            if (root == null) return null; if (SafeName(root) == name) return root;
            int n = SafeChildCount(root);
            for (int i = 0; i < n; i++) { VisualElement c = null; try { c = root.ElementAt(i); } catch { } var x = Find(c, name); if (x != null) return x; }
            return null;
        }

        private static string SafeName(VisualElement el) { try { return el?.name ?? ""; } catch { return "<unreadable>"; } }
        private static int SafeChildCount(VisualElement el) { try { return el?.childCount ?? 0; } catch { return 0; } }
        private static string SafeType(object o) { try { return o == null ? "<null>" : (o.GetType().FullName ?? o.GetType().Name); } catch { return "<unknown>"; } }
        private static string SafeTypeName(Type t) { try { return t?.FullName ?? t?.Name ?? "?"; } catch { return "?"; } }
        private static void Line(StringBuilder sb, string s) { sb.AppendLine(s); try { Plugin.Log.LogInfo("[FM26FullProbe] " + s); } catch { } }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
        }
    }
}
