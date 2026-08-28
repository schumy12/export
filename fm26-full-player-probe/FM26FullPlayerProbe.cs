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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.23.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.23 INTEROP RESOLVER PATH - press F8 after loading a save.");
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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.23 INTEROP RESOLVER PATH ===");
            sb.AppendLine("0.22 exposed FM.UI.InteropDataHandler -> FM.GamePlugin.GameInteropSubsystem and Bindings.GetHandler().");
            sb.AppendLine("Metadata reflection only. No generated FM/SI getters invoked.");
            sb.AppendLine();

            string[] targets = new[]
            {
                "FM.UI.InteropDataHandler",
                "FM.GamePlugin.GameInteropSubsystem",
                "FM.GamePlugin.ValueChangedWithSizeCallback",
                "SI.Bindable.Bindings",
                "SI.Bindable.Bindings+OpenRequest",
                "SI.Bindable.Property",
                "SI.Bindable.IDataHandler",
                "SI.Bindable.Reference.Core.IDataReference",
                "SI.Interop.InteropReference",
                "FM.UI.PersonReference"
            };

            foreach (string target in targets)
            {
                DumpNamedTypeMetadata(sb, target);
                sb.AppendLine();
            }

            sb.AppendLine("=== TYPES WITH METHODS REFERENCING GameInteropSubsystem / InteropDataHandler / IDataReference ===");
            int count = 0;
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
                    if (!ReferencesInteropPath(t)) continue;
                    count++;
                    sb.AppendLine("INTEROP TYPE " + (t.FullName ?? t.Name) + " assembly=" + (t.Assembly == null ? "?" : t.Assembly.GetName().Name));
                    DumpMethodsOnly(sb, t);
                }
            }
            sb.AppendLine("interopRelatedTypeCount=" + count);

            Save(sb);
        }

        private static bool ReferencesInteropPath(Type t)
        {
            try
            {
                string tn = t.FullName ?? t.Name;
                if (tn == "FM.UI.InteropDataHandler" || tn == "FM.GamePlugin.GameInteropSubsystem") return true;

                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var m in t.GetMethods(flags))
                {
                    string s = SafeMemberString(m);
                    if (s.Contains("GameInteropSubsystem") ||
                        s.Contains("InteropDataHandler") ||
                        s.Contains("IDataReference") ||
                        s.Contains("PersonReference") ||
                        s.Contains("ValueChangedWithSizeCallback"))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void DumpNamedTypeMetadata(StringBuilder sb, string fullName)
        {
            Type t = FindType(fullName);
            if (t == null)
            {
                sb.AppendLine("TYPE NOT FOUND " + fullName);
                return;
            }
            DumpTypeMetadata(sb, t);
        }

        private static Type FindType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = a.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static void DumpTypeMetadata(StringBuilder sb, Type t)
        {
            sb.AppendLine("TYPE " + (t.FullName ?? t.Name));
            sb.AppendLine("  Assembly=" + (t.Assembly == null ? "?" : t.Assembly.GetName().Name));
            sb.AppendLine("  BaseType=" + SafeTypeName(t.BaseType));
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            try { foreach (var p in t.GetProperties(flags)) sb.AppendLine("  PROP " + (IsStatic(p) ? "static " : "") + p.Name + " : " + SafeTypeName(p.PropertyType)); } catch { }
            try { foreach (var f in t.GetFields(flags)) sb.AppendLine("  FIELD " + (f.IsStatic ? "static " : "") + f.Name + " : " + SafeTypeName(f.FieldType)); } catch { }
            DumpMethodsOnly(sb, t);
        }

        private static void DumpMethodsOnly(StringBuilder sb, Type t)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                foreach (var m in t.GetMethods(flags))
                    sb.AppendLine("  METHOD " + (m.IsStatic ? "static " : "") + SafeMemberString(m));
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
                string file = Path.Combine(dir, "interopresolver_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
