using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.10.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.10 SAFE - open a player profile normally, then press F8. No UI-tree traversal, no Harmony.");
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
                if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    ProbeProfileRuntime();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] " + ex);
            }
        }

        private void ProbeProfileRuntime()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.10 SCHEMA + LIVE COMPONENTS ===");
            Line(sb, "No UI VisualElement traversal is performed in this version.");

            Line(sb, "\n=== DIRECT FM.UI REFERENCE SCHEMA CONSTANTS ===");
            TryDirectSchemaReads(sb);

            Line(sb, "\n=== LIVE MONOBEHAVIOUR CENSUS (PROFILE/PANEL/BINDING/DATA RELATED) ===");
            DumpLiveBehaviours(sb, 300);

            Line(sb, "\n=== MATCHING FM/SI TYPE METADATA ===");
            DumpTypeMatches(sb, new[] {
                "PanelManager", "Panel", "Profile", "Player", "Person", "Binding",
                "Record", "Parameter", "Param", "Navigation", "Context", "DataReference"
            }, 350);

            Save(sb);
        }

        private static void TryDirectSchemaReads(StringBuilder sb)
        {
            // These generated FM.UI properties are static schema/property identifiers,
            // not per-player values. Previous compiler diagnostics proved UID is static.
            try { Line(sb, "PersonReference.UID schema key = " + PersonReference.UID); }
            catch (Exception ex) { Line(sb, "PersonReference.UID read failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { Line(sb, "PersonReference.Identifier = " + PersonReference.Identifier.ToString()); }
            catch (Exception ex) { Line(sb, "PersonReference.Identifier read failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { Line(sb, "MatchPlayerReference.UID schema key = " + MatchPlayerReference.UID); }
            catch (Exception ex) { Line(sb, "MatchPlayerReference.UID read failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { Line(sb, "MatchPlayerReference.Identifier = " + MatchPlayerReference.Identifier.ToString()); }
            catch (Exception ex) { Line(sb, "MatchPlayerReference.Identifier read failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { Line(sb, "PlayerReportReference.Identifier = " + PlayerReportReference.Identifier.ToString()); }
            catch (Exception ex) { Line(sb, "PlayerReportReference.Identifier read failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { Line(sb, "PlayerReportBasicInfoReference.Identifier = " + PlayerReportBasicInfoReference.Identifier.ToString()); }
            catch (Exception ex) { Line(sb, "PlayerReportBasicInfoReference.Identifier read failed: " + ex.GetType().Name + " - " + ex.Message); }
        }

        private static void DumpLiveBehaviours(StringBuilder sb, int max)
        {
            MonoBehaviour[] all;
            try { all = Resources.FindObjectsOfTypeAll<MonoBehaviour>(); }
            catch (Exception ex)
            {
                Line(sb, "FindObjectsOfTypeAll<MonoBehaviour> failed: " + ex.GetType().Name + " - " + ex.Message);
                return;
            }

            int total = 0;
            int matched = 0;
            if (all != null)
            {
                foreach (var mb in all)
                {
                    total++;
                    if (mb == null) continue;

                    string type = SafeManagedType(mb);
                    if (!ContainsAny(type,
                        "FM.", "SI.", "panel", "profile", "player", "person", "binding", "record", "navigation", "data"))
                        continue;

                    string go = "";
                    try { go = mb.gameObject == null ? "<null>" : (mb.gameObject.name ?? ""); } catch { go = "<error>"; }
                    string name = "";
                    try { name = mb.name ?? ""; } catch { }

                    Line(sb, "LIVE type=" + type + " behaviourName='" + name + "' gameObject='" + go + "'");
                    matched++;

                    if (ContainsAny(type, "panel", "profile", "player", "person", "binding", "record", "navigation"))
                    {
                        Type mt = null;
                        try { mt = mb.GetType(); } catch { }
                        if (mt != null) DumpInterestingMembers(mt, sb, "  ", 80);
                    }

                    if (matched >= max) break;
                }
            }

            Line(sb, "MonoBehaviours total=" + total + " matched/logged=" + matched);
        }

        private static void DumpTypeMatches(StringBuilder sb, string[] needles, int max)
        {
            int count = 0;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = "";
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
                    string fn = t.FullName ?? "";
                    if (!ContainsAny(fn, needles)) continue;

                    bool memberHit = TypeHasInterestingMember(t);
                    if (!memberHit && !ContainsAny(fn, "PanelManager", "PlayerPanel", "PersonPanel", "Profile", "Record", "Binding"))
                        continue;

                    Line(sb, "TYPE " + an + ": " + fn);
                    DumpInterestingMembers(t, sb, "  ", 140);
                    if (++count >= max) return;
                }
            }
            Line(sb, "Matching types logged=" + count);
        }

        private static bool TypeHasInterestingMember(Type t)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                foreach (var p in t.GetProperties(flags))
                {
                    if (ContainsAny(p.Name, "record", "param", "player", "person", "profile", "panel", "data", "context", "reference", "uid")) return true;
                    if (ContainsAny(SafeTypeName(p.PropertyType), "Record", "PersonReference", "PlayerReference", "TypedValue", "Bindings")) return true;
                }
            }
            catch { }
            try
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (ContainsAny(f.Name, "record", "param", "player", "person", "profile", "panel", "data", "context", "reference", "uid")) return true;
                    if (ContainsAny(SafeTypeName(f.FieldType), "Record", "PersonReference", "PlayerReference", "TypedValue", "Bindings")) return true;
                }
            }
            catch { }
            return false;
        }

        private static void DumpInterestingMembers(Type t, StringBuilder sb, string pad, int max)
        {
            int n = 0;
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                foreach (var p in t.GetProperties(flags))
                {
                    string pn = p.Name ?? "";
                    string pt = SafeTypeName(p.PropertyType);
                    if (!ContainsAny(pn, "record", "param", "player", "person", "profile", "panel", "data", "context", "reference", "uid", "binding") &&
                        !ContainsAny(pt, "Record", "PersonReference", "PlayerReference", "TypedValue", "Bindings", "Panel"))
                        continue;
                    Line(sb, pad + "PROP " + (IsStatic(p) ? "static " : "") + pn + " : " + pt);
                    if (++n >= max) return;
                }
            }
            catch { }

            try
            {
                foreach (var f in t.GetFields(flags))
                {
                    string fn = f.Name ?? "";
                    string ft = SafeTypeName(f.FieldType);
                    if (!ContainsAny(fn, "record", "param", "player", "person", "profile", "panel", "data", "context", "reference", "uid", "binding") &&
                        !ContainsAny(ft, "Record", "PersonReference", "PlayerReference", "TypedValue", "Bindings", "Panel"))
                        continue;
                    Line(sb, pad + "FIELD " + (f.IsStatic ? "static " : "") + fn + " : " + ft);
                    if (++n >= max) return;
                }
            }
            catch { }

            try
            {
                foreach (var m in t.GetMethods(flags))
                {
                    string mn = m.Name ?? "";
                    if (!ContainsAny(mn, "record", "param", "player", "person", "profile", "panel", "data", "context", "reference", "uid", "binding", "open", "show"))
                        continue;
                    Line(sb, pad + "METHOD " + (m.IsStatic ? "static " : "") + SafeMemberString(m));
                    if (++n >= max) return;
                }
            }
            catch { }
        }

        private static bool IsStatic(PropertyInfo p)
        {
            try
            {
                var g = p.GetGetMethod(true);
                if (g != null) return g.IsStatic;
                var s = p.GetSetMethod(true);
                return s != null && s.IsStatic;
            }
            catch { return false; }
        }

        private static bool ContainsAny(string s, params string[] needles)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var n in needles)
                if (!string.IsNullOrEmpty(n) && s.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string SafeManagedType(object o)
        {
            try { return o == null ? "<null>" : (o.GetType().FullName ?? o.GetType().Name); }
            catch { return "<unknown>"; }
        }

        private static string SafeTypeName(Type t)
        {
            try { return t?.FullName ?? t?.Name ?? "?"; }
            catch { return "?"; }
        }

        private static string SafeMemberString(MethodBase m)
        {
            try { return m == null ? "?" : m.ToString(); }
            catch { return m?.Name ?? "?"; }
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
                string file = Path.Combine(dir, "runtimeprobe_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved runtime probe: " + file);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex);
            }
        }
    }
}
