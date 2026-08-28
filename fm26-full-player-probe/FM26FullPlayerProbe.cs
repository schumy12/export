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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.2.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.2 - F7 = safe probe player row");
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
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.2 SAFE ===");

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
                Line(sb, $"ROW {i}: type={SafeType(row)} name={SafeName(row)} selected={selected} children={SafeChildCount(row)}");
                if (selected && target == null) target = row;
            }

            if (target == null && view.childCount > 0)
            {
                target = view.ElementAt(0);
                Line(sb, "No selected marker; probing first visible row.");
            }
            if (target == null) { Line(sb, "ERROR: no row to probe"); Save(sb); return; }

            Line(sb, "\n=== VISUAL TREE + SAFE METADATA ===");
            DumpElement(target, sb, 0, 6);

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
                {
                    if (doc == null) continue;
                    VisualElement root = null;
                    try { root = doc.rootVisualElement; } catch { }
                    if (root != null && SafeName(root) == "PanelManager-container") return root;
                }
            }
            catch { }
            return null;
        }

        private static VisualElement Find(VisualElement root, string name)
        {
            if (root == null) return null;
            if (SafeName(root) == name) return root;
            int count = SafeChildCount(root);
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = root.ElementAt(i); } catch { }
                var x = Find(child, name);
                if (x != null) return x;
            }
            return null;
        }

        private static void DumpElement(VisualElement el, StringBuilder sb, int depth, int maxDepth)
        {
            if (el == null || depth > maxDepth) return;
            string pad = new string(' ', depth * 2);
            Line(sb, $"{pad}{SafeType(el)} name='{SafeName(el)}' children={SafeChildCount(el)}");

            // Direct IL2CPP wrapper access is safe. Reflection invocation of generated
            // getters is intentionally NOT used: some are UnmanagedCallersOnly and
            // calling them via PropertyInfo.GetValue terminates the whole process.
            try
            {
                var ud = el.userData;
                if (ud != null)
                    Line(sb, $"{pad} userData -> {SafeObjectSummary(ud)}");
            }
            catch (Exception ex)
            {
                Line(sb, $"{pad} userData -> <{ex.GetType().Name}>");
            }

            DumpMetadataOnly(el, sb, depth + 1);

            int count = SafeChildCount(el);
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = el.ElementAt(i); } catch { }
                DumpElement(child, sb, depth + 1, maxDepth);
            }
        }

        private static void DumpMetadataOnly(object obj, StringBuilder sb, int depth)
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
                    if (!Interesting(p.Name)) continue;
                    string pt = "?";
                    try { pt = p.PropertyType.FullName ?? p.PropertyType.Name; } catch { }
                    // Metadata only. Never invoke the getter through reflection.
                    Line(sb, $"{pad}PROPERTY {p.Name} : {pt} readable={p.CanRead}");
                }
            }
            catch (Exception ex)
            {
                Line(sb, $"{pad}<property-enumeration:{ex.GetType().Name}>");
            }

            try
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (!Interesting(f.Name)) continue;
                    string ft = "?";
                    try { ft = f.FieldType.FullName ?? f.FieldType.Name; } catch { }
                    Line(sb, $"{pad}FIELD {f.Name} : {ft}");
                }
            }
            catch (Exception ex)
            {
                Line(sb, $"{pad}<field-enumeration:{ex.GetType().Name}>");
            }
        }

        private static void DumpTypes(StringBuilder sb)
        {
            string[] wanted = {
                "PlayerReference", "PersonReference", "PlayerDataReference",
                "PlayerAttributeReference", "AttributeNameAndValueReference",
                "AttributeValueReference", "PlayerReportScoutedAbilityReference",
                "IGEMovePersonReference", "IPlayerReference"
            };

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
                                DumpTypeMetadata(t, sb);
                                count++;
                                break;
                            }
                        }
                        if (count >= 120) return;
                    }
                }
            }
            catch (Exception ex)
            {
                Line(sb, "Type scan error: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void DumpTypeMetadata(Type t, StringBuilder sb)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                foreach (var p in t.GetProperties(flags))
                {
                    if (!Interesting(p.Name)) continue;
                    string pt = "?";
                    try { pt = p.PropertyType.FullName ?? p.PropertyType.Name; } catch { }
                    Line(sb, $"  PROP {p.Name} : {pt}");
                }
            }
            catch { }

            try
            {
                foreach (var m in t.GetMethods(flags))
                {
                    if (!Interesting(m.Name)) continue;
                    Line(sb, $"  METHOD {m.Name}");
                }
            }
            catch { }

            try
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (!Interesting(f.Name)) continue;
                    string ft = "?";
                    try { ft = f.FieldType.FullName ?? f.FieldType.Name; } catch { }
                    Line(sb, $"  FIELD {f.Name} : {ft}");
                }
            }
            catch { }
        }

        private static bool Interesting(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            string[] keys = {
                "player", "person", "reference", "data", "binding", "source", "id",
                "ability", "potential", "attribute", "current", "value", "name", "club",
                "team", "contract", "wage", "nation", "position", "foot", "reputation",
                "personality", "consistency", "important", "professional", "ambition",
                "injury", "temperament", "pressure", "sportsmanship"
            };
            foreach (var k in keys) if (n.Contains(k)) return true;
            return false;
        }

        private static string SafeName(VisualElement el)
        {
            try { return el?.name ?? ""; } catch { return "<unreadable>"; }
        }

        private static int SafeChildCount(VisualElement el)
        {
            try { return el?.childCount ?? 0; } catch { return 0; }
        }

        private static string SafeType(object o)
        {
            if (o == null) return "<null>";
            try { return o.GetType().FullName ?? o.GetType().Name; } catch { return "<unknown-type>"; }
        }

        private static string SafeObjectSummary(object o)
        {
            if (o == null) return "<null>";
            return SafeType(o);
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
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex);
            }
        }
    }
}
