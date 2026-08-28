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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.25.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.25 BINDINGS PROPERTY SHAPE - press F8 after loading a save.");
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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.25 BINDINGS PROPERTY SHAPE ===");
            sb.AppendLine("0.24 found the live GameInteropSubsystem, but managed subscription failed because ReadOnlySpan<T> cannot be marshalled as a managed delegate.");
            sb.AppendLine("Next route: use the already-live InteropDataHandler so its native callback remains responsible for backend responses.");
            sb.AppendLine("Metadata reflection only; no generated FM/SI getters invoked.");
            sb.AppendLine();

            string[] exactTargets = new[]
            {
                "SI.Bindable.Bindings+Property",
                "SI.Bindable.Bindings+DataKey",
                "SI.Bindable.Bindings+HandlerAccess",
                "SI.Bindable.Bindings+OpenRequest",
                "SI.Bindable.Bindings+Data",
                "SI.Bindable.IDataHandler",
                "FM.UI.InteropDataHandler",
                "SI.Core.TypedValue",
                "SI.Core.ReferenceTypedValue",
                "SI.Bindable.Reference.Core.PropertyID",
                "SI.Interop.InteropReference",
                "FM.UI.PersonReference"
            };

            foreach (string name in exactTargets)
            {
                DumpNamedTypeMetadata(sb, name);
                sb.AppendLine();
            }

            sb.AppendLine("=== ALL NESTED TYPES OF SI.Bindable.Bindings ===");
            var bindingsType = FindType("SI.Bindable.Bindings");
            if (bindingsType == null)
            {
                sb.AppendLine("Bindings type not found.");
            }
            else
            {
                try
                {
                    foreach (var nt in bindingsType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        sb.AppendLine("NESTED " + (nt.FullName ?? nt.Name));
                        if ((nt.Name ?? "").Contains("Property") ||
                            (nt.Name ?? "").Contains("DataKey") ||
                            (nt.Name ?? "").Contains("HandlerAccess") ||
                            (nt.Name ?? "").Contains("OpenRequest"))
                        {
                            DumpTypeMetadata(sb, nt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Nested type dump failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== TYPES WHOSE METHOD SIGNATURE CONTAINS 'Property, Key' ===");
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
                    bool hit = false;
                    try
                    {
                        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                        foreach (var m in t.GetMethods(flags))
                        {
                            string s = SafeMemberString(m);
                            if (s.Contains("Property, Key") || s.Contains("Property, SI.Bindable.Bindings+Key"))
                            {
                                if (!hit)
                                {
                                    sb.AppendLine("TYPE " + (t.FullName ?? t.Name));
                                    hit = true;
                                    count++;
                                }
                                sb.AppendLine("  METHOD " + (m.IsStatic ? "static " : "") + s);
                            }
                        }
                    }
                    catch { }
                }
            }
            sb.AppendLine("matchingTypeCount=" + count);

            Save(sb);
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

            try
            {
                foreach (var p in t.GetProperties(flags))
                    sb.AppendLine("  PROP " + (IsStatic(p) ? "static " : "") + p.Name + " : " + SafeTypeName(p.PropertyType));
            }
            catch { }

            try
            {
                foreach (var f in t.GetFields(flags))
                    sb.AppendLine("  FIELD " + (f.IsStatic ? "static " : "") + f.Name + " : " + SafeTypeName(f.FieldType));
            }
            catch { }

            try
            {
                foreach (var c in t.GetConstructors(flags))
                    sb.AppendLine("  CTOR " + SafeMemberString(c));
            }
            catch { }

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
                string file = Path.Combine(dir, "bindingsproperty_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
