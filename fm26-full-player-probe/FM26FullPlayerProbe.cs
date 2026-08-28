using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.21.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.21 RESOLVER HANDLER CENSUS - press F8 after loading a save.");
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
                    RunProbe();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
            }
        }

        private void RunProbe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.21 RESOLVER HANDLER CENSUS ===");
            sb.AppendLine("0.20 proved PersonReference can be carried inside SI.Core.TypedValue.");
            sb.AppendLine("This probe identifies the concrete IDataHandler/LookupDataHandler classes that can resolve player/person properties.");
            sb.AppendLine("Metadata reflection only: no generated FM/SI property getters invoked.");
            sb.AppendLine();

            DumpNamedTypeMetadata(sb, "SI.Bindable.Property");
            DumpNamedTypeMetadata(sb, "SI.Bindable.PropertyHandler");
            DumpNamedTypeMetadata(sb, "SI.Bindable.IDataHandler");
            DumpNamedTypeMetadata(sb, "SI.Bindable.LookupDataHandler");
            DumpNamedTypeMetadata(sb, "SI.Bindable.Bindings+HandlerAccess");

            sb.AppendLine();
            sb.AppendLine("=== LOOKUPDATAHANDLER / IDATAHANDLER TYPE CENSUS ===");
            int matched = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { }
                if (types == null) continue;

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (!IsHandlerType(t)) continue;
                    matched++;
                    sb.AppendLine("HANDLER TYPE " + (t.FullName ?? t.Name) + " base=" + SafeTypeName(t.BaseType));
                }
            }
            sb.AppendLine("handlerTypeCount=" + matched);

            sb.AppendLine();
            sb.AppendLine("=== PLAYER/PERSON/ABILITY/ATTRIBUTE/REPORT HANDLER DETAILS ===");
            int detailed = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { }
                if (types == null) continue;

                foreach (var t in types)
                {
                    if (t == null || !IsHandlerType(t)) continue;
                    string n = t.FullName ?? t.Name;
                    if (!LooksRelevant(n)) continue;
                    detailed++;
                    DumpTypeMetadata(sb, t);
                }
            }
            sb.AppendLine("detailedHandlerCount=" + detailed);

            sb.AppendLine();
            sb.AppendLine("=== NON-HANDLER TYPES WITH STRONG RESOLVER NAMES ===");
            int resolverTypes = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { }
                if (types == null) continue;

                foreach (var t in types)
                {
                    if (t == null) continue;
                    string n = t.FullName ?? t.Name;
                    string low = n.ToLowerInvariant();
                    if (!(low.Contains("person") || low.Contains("player"))) continue;
                    if (!(low.Contains("resolver") || low.Contains("lookup") || low.Contains("property") || low.Contains("datahandler") || low.Contains("referencehandler"))) continue;
                    if (IsHandlerType(t)) continue;
                    resolverTypes++;
                    sb.AppendLine("RESOLVER-LIKE TYPE " + n + " base=" + SafeTypeName(t.BaseType));
                }
            }
            sb.AppendLine("resolverLikeTypeCount=" + resolverTypes);

            Save(sb);
        }

        private static bool IsHandlerType(Type t)
        {
            try
            {
                Type cur = t;
                while (cur != null)
                {
                    string n = cur.FullName ?? cur.Name;
                    if (n == "SI.Bindable.LookupDataHandler") return true;
                    cur = cur.BaseType;
                }

                foreach (var i in t.GetInterfaces())
                {
                    string n = i.FullName ?? i.Name;
                    if (n == "SI.Bindable.IDataHandler") return true;
                }
            }
            catch { }
            return false;
        }

        private static bool LooksRelevant(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string low = n.ToLowerInvariant();
            return low.Contains("player") || low.Contains("person") || low.Contains("ability") ||
                   low.Contains("attribute") || low.Contains("report") || low.Contains("database") ||
                   low.Contains("scout") || low.Contains("reference");
        }

        private static void DumpNamedTypeMetadata(StringBuilder sb, string fullName)
        {
            Type t = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = a.GetType(fullName, false);
                    if (t != null) break;
                }
                catch { }
            }
            if (t == null)
            {
                sb.AppendLine("TYPE NOT FOUND " + fullName);
                return;
            }
            DumpTypeMetadata(sb, t);
        }

        private static void DumpTypeMetadata(StringBuilder sb, Type t)
        {
            if (t == null) return;
            sb.AppendLine("TYPE " + (t.FullName ?? t.Name));
            try { sb.AppendLine("  BaseType=" + SafeTypeName(t.BaseType)); } catch { }

            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                foreach (var p in t.GetProperties(flags))
                    sb.AppendLine("  PROP " + (IsStatic(p) ? "static " : "") + p.Name + " : " + SafeTypeName(p.PropertyType));
            }
            catch (Exception ex) { sb.AppendLine("  properties failed: " + ex.GetType().Name); }

            try
            {
                foreach (var f in t.GetFields(flags))
                    sb.AppendLine("  FIELD " + (f.IsStatic ? "static " : "") + f.Name + " : " + SafeTypeName(f.FieldType));
            }
            catch (Exception ex) { sb.AppendLine("  fields failed: " + ex.GetType().Name); }

            try
            {
                foreach (var m in t.GetMethods(flags))
                    sb.AppendLine("  METHOD " + (m.IsStatic ? "static " : "") + SafeMemberString(m));
            }
            catch (Exception ex) { sb.AppendLine("  methods failed: " + ex.GetType().Name); }
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

        private static string SafeTypeName(Type t)
        {
            try { return t == null ? "<null>" : (t.FullName ?? t.Name); }
            catch { return "?"; }
        }

        private static string SafeMemberString(MethodBase m)
        {
            try { return m == null ? "?" : m.ToString(); }
            catch { return m == null ? "?" : m.Name; }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "handlercensus_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
