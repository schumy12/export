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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.11.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.11 SAFE - open player profile, press F8. Unity-object census only; no Harmony/UI traversal/getter reflection.");
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
                    ProbeRuntime();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] " + ex);
            }
        }

        private void ProbeRuntime()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.11 LIVE UNITY OBJECTS ===");
            Line(sb, "No Harmony. No VisualElement traversal. No generated FM/SI property getters invoked.");

            Line(sb, "\n=== CONFIRMED SCHEMA KEYS ===");
            try { Line(sb, "PersonReference.UID=" + PersonReference.UID); } catch (Exception ex) { Line(sb, "PersonReference.UID failed: " + ex.GetType().Name); }
            try { Line(sb, "PersonReference.Identifier=" + PersonReference.Identifier.ToString()); } catch (Exception ex) { Line(sb, "PersonReference.Identifier failed: " + ex.GetType().Name); }
            try { Line(sb, "PlayerReportReference.Identifier=" + PlayerReportReference.Identifier.ToString()); } catch (Exception ex) { Line(sb, "PlayerReportReference.Identifier failed: " + ex.GetType().Name); }

            Line(sb, "\n=== LIVE UNITYENGINE.OBJECT CENSUS ===");
            DumpUnityObjects(sb, 800);

            Line(sb, "\n=== TARGET TYPE INHERITANCE/METADATA ===");
            DumpTargetType(sb, typeof(PersonReference));
            DumpTargetType(sb, typeof(PlayerReportReference));
            DumpNamedType(sb, "FM.UI.EmbeddedDataHandler");
            DumpNamedType(sb, "FM.UI.EmbeddedDataHandler+PersonReferenceClickedHandler");
            DumpNamedType(sb, "SI.Bindable.BindingSubsystem");
            DumpNamedType(sb, "SI.Bindable.Bindings");
            DumpNamedType(sb, "SI.Bindable.PanelID");
            DumpNamedType(sb, "SI.Core.Record");
            DumpNamedType(sb, "SI.Core.TypedValue");
            DumpNamedType(sb, "FM.UI.DatabaseRecordReference");

            Save(sb);
        }

        private static void DumpUnityObjects(StringBuilder sb, int max)
        {
            UnityEngine.Object[] all;
            try { all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>(); }
            catch (Exception ex)
            {
                Line(sb, "FindObjectsOfTypeAll<UnityEngine.Object> failed: " + ex.GetType().Name + " - " + ex.Message);
                return;
            }

            int total = 0;
            int matched = 0;
            if (all != null)
            {
                foreach (var obj in all)
                {
                    total++;
                    if (obj == null) continue;

                    string type = SafeManagedType(obj);
                    if (!ContainsAny(type,
                        "FM.UI", "SI.Bindable", "SI.UI", "Panel", "Profile", "Person", "Player",
                        "Binding", "Record", "Reference", "DataHandler", "ContextMenu"))
                        continue;

                    string objName = "";
                    try { objName = obj.name ?? ""; } catch { objName = "<error>"; }

                    string extra = "";
                    try
                    {
                        var c = obj as Component;
                        if (c != null)
                        {
                            string go = c.gameObject == null ? "<null>" : (c.gameObject.name ?? "");
                            extra = " gameObject='" + go + "'";
                        }
                    }
                    catch { }

                    Line(sb, "LIVEOBJ type=" + type + " name='" + objName + "'" + extra);
                    matched++;
                    if (matched >= max) break;
                }
            }
            Line(sb, "UnityEngine.Objects total=" + total + " matched/logged=" + matched);
        }

        private static void DumpNamedType(StringBuilder sb, string fullName)
        {
            Type found = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    found = a.GetType(fullName, false);
                    if (found != null) break;
                }
                catch { }
            }

            if (found == null)
            {
                Line(sb, "TYPE NOT FOUND: " + fullName);
                return;
            }
            DumpTargetType(sb, found);
        }

        private static void DumpTargetType(StringBuilder sb, Type t)
        {
            if (t == null) return;
            Line(sb, "TYPE " + (t.FullName ?? t.Name));
            try { Line(sb, "  BaseType=" + (t.BaseType == null ? "<null>" : (t.BaseType.FullName ?? t.BaseType.Name))); } catch { }
            try
            {
                var ifaces = t.GetInterfaces();
                foreach (var i in ifaces)
                    Line(sb, "  IFACE " + (i.FullName ?? i.Name));
            }
            catch { }

            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            int n = 0;
            try
            {
                foreach (var p in t.GetProperties(flags))
                {
                    string pn = p.Name ?? "";
                    string pt = SafeTypeName(p.PropertyType);
                    if (ContainsAny(pn, "uid", "id", "record", "data", "binding", "key", "param", "person", "player", "panel", "instance") ||
                        ContainsAny(pt, "Record", "TypedValue", "Reference", "Bindings", "Key", "Panel"))
                    {
                        Line(sb, "  PROP " + (IsStatic(p) ? "static " : "") + pn + " : " + pt);
                        if (++n >= 100) break;
                    }
                }
            }
            catch { }

            try
            {
                foreach (var f in t.GetFields(flags))
                {
                    string fn = f.Name ?? "";
                    string ft = SafeTypeName(f.FieldType);
                    if (ContainsAny(fn, "uid", "id", "record", "data", "binding", "key", "param", "person", "player", "panel", "instance") ||
                        ContainsAny(ft, "Record", "TypedValue", "Reference", "Bindings", "Key", "Panel"))
                    {
                        Line(sb, "  FIELD " + (f.IsStatic ? "static " : "") + fn + " : " + ft);
                        if (++n >= 180) break;
                    }
                }
            }
            catch { }

            try
            {
                foreach (var m in t.GetMethods(flags))
                {
                    string mn = m.Name ?? "";
                    if (ContainsAny(mn, "get_", "try", "value", "record", "data", "binding", "key", "param", "person", "player", "panel", "instance", "context"))
                    {
                        Line(sb, "  METHOD " + (m.IsStatic ? "static " : "") + SafeMemberString(m));
                        if (++n >= 260) break;
                    }
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
