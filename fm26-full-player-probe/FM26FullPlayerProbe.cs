using System;
using System.Collections.Generic;
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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.1.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded - F7 = probe player row");
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
                if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame)
                    Probe();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] " + ex);
            }
        }

        private void Probe()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.1 ===");

            var root = MainRoot();
            if (root == null) { Line(sb, "ERROR: main UI root not found"); Save(sb); return; }

            var table = Find(root, "playertable") ?? Find(root, "client-object-viewer-table");
            if (table == null) { Line(sb, "ERROR: player table not found"); Save(sb); return; }

            var view = Find(table, "View");
            if (view == null) { Line(sb, "ERROR: View not found"); Save(sb); return; }

            Line(sb, $"Visible rows: {view.childCount}");
            VisualElement target = null;
            for (int i = 0; i < view.childCount; i++)
            {
                var row = view.ElementAt(i);
                bool selected = false;
                try { selected = row.ClassListContains("virtualised-list__item--selected"); } catch { }
                Line(sb, $"ROW {i}: name={row.name} selected={selected} children={row.childCount}");
                if (selected && target == null) target = row;
            }

            if (target == null && view.childCount > 0)
            {
                target = view.ElementAt(0);
                Line(sb, "No selected marker; probing first visible row.");
            }
            if (target == null) { Line(sb, "ERROR: no row to probe"); Save(sb); return; }

            Line(sb, "\n=== VISUAL TREE + BINDINGS ===");
            DumpElement(target, sb, 0, 5);

            Line(sb, "\n=== RELEVANT TYPES ===");
            DumpTypes(sb);

            Save(sb);
        }

        private VisualElement MainRoot()
        {
            try
            {
                var docs = FindObjectsOfType<UIDocument>();
                foreach (var doc in docs)
                    if (doc != null && doc.rootVisualElement != null && doc.rootVisualElement.name == "PanelManager-container")
                        return doc.rootVisualElement;
            }
            catch { }
            return null;
        }

        private static VisualElement Find(VisualElement root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var x = Find(root.ElementAt(i), name);
                if (x != null) return x;
            }
            return null;
        }

        private static void DumpElement(VisualElement el, StringBuilder sb, int depth, int maxDepth)
        {
            if (el == null || depth > maxDepth) return;
            string pad = new string(' ', depth * 2);
            Line(sb, $"{pad}{el.GetType().FullName} name='{el.name}' children={el.childCount}");

            try
            {
                if (el.userData != null)
                {
                    Line(sb, $"{pad} userData -> {el.userData.GetType().FullName}");
                    DumpObject(el.userData, sb, depth + 1, 2);
                }
            }
            catch { }

            object ds = Member(el, "dataSource");
            if (ds != null)
            {
                Line(sb, $"{pad} dataSource -> {ds.GetType().FullName}");
                DumpObject(ds, sb, depth + 1, 3);
            }

            DumpInteresting(el, sb, depth + 1, 1);

            for (int i = 0; i < el.childCount; i++)
                DumpElement(el.ElementAt(i), sb, depth + 1, maxDepth);
        }

        private static void DumpObject(object obj, StringBuilder sb, int depth, int left)
        {
            if (obj == null || left < 0 || depth > 8) return;
            string pad = new string(' ', depth * 2);
            Type t;
            try { t = obj.GetType(); } catch { return; }
            Line(sb, $"{pad}OBJECT {t.FullName}");
            DumpInteresting(obj, sb, depth + 1, left);
        }

        private static void DumpInteresting(object obj, StringBuilder sb, int depth, int left)
        {
            if (obj == null) return;
            string pad = new string(' ', depth * 2);
            Type t;
            try { t = obj.GetType(); } catch { return; }
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                foreach (var p in t.GetProperties(flags))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0 || !Interesting(p.Name)) continue;
                    try
                    {
                        var v = p.GetValue(obj, null);
                        Line(sb, $"{pad}{p.Name} = {Value(v)}");
                        if (v != null && left > 0 && !Simple(v.GetType())) DumpObject(v, sb, depth + 1, left - 1);
                    }
                    catch (Exception ex) { Line(sb, $"{pad}{p.Name} = <{ex.GetType().Name}>"); }
                }

                foreach (var f in t.GetFields(flags))
                {
                    if (!Interesting(f.Name)) continue;
                    try { Line(sb, $"{pad}{f.Name} = {Value(f.GetValue(obj))}"); } catch { }
                }
            }
            catch { }
        }

        private static object Member(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var t = obj.GetType();
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p.GetValue(obj, null);
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(obj);
            }
            catch { }
            return null;
        }

        private static void DumpTypes(StringBuilder sb)
        {
            string[] wanted = { "PlayerReference", "PersonReference", "PlayerDataReference", "PlayerAttributeReference", "AttributeNameAndValueReference", "PlayerReportScoutedAbilityReference" };
            int count = 0;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string an;
                    try { an = a.GetName().Name ?? ""; } catch { continue; }
                    if (!(an.StartsWith("FM") || an.StartsWith("SI"))) continue;
                    Type[] types;
                    try { types = a.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types; }
                    catch { continue; }
                    if (types == null) continue;
                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        foreach (var w in wanted)
                        {
                            if ((t.FullName ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Line(sb, $"TYPE {an}: {t.FullName}");
                                count++;
                                break;
                            }
                        }
                        if (count >= 120) return;
                    }
                }
            }
            catch (Exception ex) { Line(sb, "Type scan error: " + ex.Message); }
        }

        private static bool Interesting(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            string[] keys = { "player", "person", "reference", "data", "binding", "source", "id", "ability", "potential", "attribute", "current", "value", "name", "club", "team", "contract", "wage", "nation", "position", "foot", "reputation", "personality", "consistency", "important", "professional", "ambition", "injury", "temperament", "pressure" };
            foreach (var k in keys) if (n.Contains(k)) return true;
            return false;
        }

        private static bool Simple(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(Guid);

        private static string Value(object o)
        {
            if (o == null) return "<null>";
            try
            {
                string s = o.ToString() ?? "";
                s = s.Replace("\r", " ").Replace("\n", " ");
                if (s.Length > 300) s = s.Substring(0, 300) + "...";
                return s + " [" + o.GetType().FullName + "]";
            }
            catch { return "<unreadable>"; }
        }

        private static void Line(StringBuilder sb, string s)
        {
            sb.AppendLine(s);
            try { Plugin.Log.LogInfo("[FM26FullProbe] " + s); } catch { }
        }

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
