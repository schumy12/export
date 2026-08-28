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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.3.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.3 - F7 = datasource probe");
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
            try
            {
                if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) Probe();
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] " + ex); }
        }

        private void Probe()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.3 DATASOURCE ===");
            var root = MainRoot();
            if (root == null) { Line(sb, "ERROR: main UI root not found"); Save(sb); return; }
            var table = Find(root, "playertable") ?? Find(root, "client-object-viewer-table");
            if (table == null) { Line(sb, "ERROR: player table not found"); Save(sb); return; }
            var view = Find(table, "View");
            if (view == null) { Line(sb, "ERROR: View not found"); Save(sb); return; }

            Line(sb, "Visible rows: " + SafeChildCount(view));
            for (int i = 0; i < SafeChildCount(view); i++)
            {
                VisualElement row = null;
                try { row = view.ElementAt(i); } catch { }
                if (row == null) continue;
                Line(sb, "\n=== ROW " + i + " ===");
                DumpLive(row, sb, "row[" + i + "]", 0, 7);
            }

            Line(sb, "\n=== KEY TYPE METADATA ===");
            DumpNamedType(sb, "FM.UI.PersonReference");
            DumpNamedType(sb, "FM.UI.IPlayerReference");
            DumpNamedType(sb, "FM.UI.PlayerAttributeReference");
            DumpNamedType(sb, "FM.UI.AttributeNameAndValueReference");
            DumpNamedType(sb, "FM.UI.AttributeValueReference");
            DumpNamedType(sb, "FM.UI.PlayerReportScoutedAbilityReference");
            Save(sb);
        }

        private static void DumpLive(VisualElement el, StringBuilder sb, string path, int depth, int maxDepth)
        {
            if (el == null || depth > maxDepth) return;
            string pad = new string(' ', depth * 2);
            string name = SafeName(el);
            string type = SafeType(el);
            string dsType = "";
            string dsPath = "";
            object ds = null;
            object ud = null;

            try { dsType = el.dataSourceTypeString ?? ""; } catch (Exception ex) { dsType = "<" + ex.GetType().Name + ">"; }
            try { dsPath = el.dataSourcePathString ?? ""; } catch (Exception ex) { dsPath = "<" + ex.GetType().Name + ">"; }
            try { ds = el.dataSource; } catch (Exception ex) { Line(sb, pad + path + " dataSource=<" + ex.GetType().Name + ">"); }
            try { ud = el.userData; } catch (Exception ex) { Line(sb, pad + path + " userData=<" + ex.GetType().Name + ">"); }

            bool interesting = !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(dsType) || !string.IsNullOrEmpty(dsPath) || ds != null || ud != null;
            if (interesting)
            {
                Line(sb, pad + path + " type=" + type + " name='" + name + "' children=" + SafeChildCount(el));
                if (!string.IsNullOrEmpty(dsType)) Line(sb, pad + "  dataSourceTypeString='" + dsType + "'");
                if (!string.IsNullOrEmpty(dsPath)) Line(sb, pad + "  dataSourcePathString='" + dsPath + "'");
                if (ds != null) Line(sb, pad + "  dataSource managedType=" + SafeType(ds));
                if (ud != null) Line(sb, pad + "  userData managedType=" + SafeType(ud));
            }

            int count = SafeChildCount(el);
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = el.ElementAt(i); } catch { }
                DumpLive(child, sb, path + "/" + i, depth + 1, maxDepth);
            }
        }

        private VisualElement MainRoot()
        {
            try
            {
                var docs = FindObjectsOfType<UIDocument>();
                foreach (var doc in docs)
                {
                    if (doc == null) continue;
                    VisualElement r = null;
                    try { r = doc.rootVisualElement; } catch { }
                    if (r != null && SafeName(r) == "PanelManager-container") return r;
                }
            }
            catch { }
            return null;
        }

        private static VisualElement Find(VisualElement root, string name)
        {
            if (root == null) return null;
            if (SafeName(root) == name) return root;
            int n = SafeChildCount(root);
            for (int i = 0; i < n; i++)
            {
                VisualElement c = null;
                try { c = root.ElementAt(i); } catch { }
                var x = Find(c, name);
                if (x != null) return x;
            }
            return null;
        }

        private static void DumpNamedType(StringBuilder sb, string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType(fullName, false); } catch { }
                if (t == null) continue;
                Line(sb, "TYPE " + fullName);
                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                try
                {
                    foreach (var p in t.GetProperties(flags))
                        Line(sb, "  PROP " + p.Name + " : " + SafeTypeName(p.PropertyType));
                }
                catch { }
                try
                {
                    foreach (var f in t.GetFields(flags))
                        Line(sb, "  FIELD " + f.Name + " : " + SafeTypeName(f.FieldType));
                }
                catch { }
                try
                {
                    foreach (var c in t.GetConstructors(flags))
                        Line(sb, "  CTOR " + c.ToString());
                }
                catch { }
                return;
            }
            Line(sb, "TYPE NOT FOUND " + fullName);
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
