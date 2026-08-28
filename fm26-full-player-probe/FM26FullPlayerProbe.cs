using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;
using SI.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.20.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.20 REFERENCE RESOLVER BRIDGE - press F8 after loading a save.");
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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.20 REFERENCE RESOLVER BRIDGE ===");
            sb.AppendLine("0.19 proved PersonReference.TryGetValue is not the API that evaluates PersonReference properties.");
            sb.AppendLine("This probe tests the TypedValue bridge used by SI.Bindable and dumps resolver metadata only.");
            sb.AppendLine();

            sb.AppendLine("=== DIRECT REFERENCE -> TYPEDVALUE TEST ===");
            int[] indices = new[] { 0, 1, 100, 1000 };
            foreach (int index in indices)
                TestTypedValueBridge(sb, index);

            sb.AppendLine();
            sb.AppendLine("=== RESOLVER-RELATED MANAGED METADATA (NO REFLECTION GETTERS INVOKED) ===");
            DumpNamedTypeMetadata(sb, "SI.Core.TypedValue");
            DumpNamedTypeMetadata(sb, "SI.Core.ReferenceTypedValue");
            DumpNamedTypeMetadata(sb, "SI.Interop.InteropReference");
            DumpNamedTypeMetadata(sb, "SI.Interop.InteropReference+Pair");
            DumpNamedTypeMetadata(sb, "SI.Bindable.Reference.Core.Property");
            DumpNamedTypeMetadata(sb, "SI.Bindable.Reference.Core.PropertyID");
            DumpNamedTypeMetadata(sb, "SI.Bindable.BindingSubsystem");
            DumpNamedTypeMetadata(sb, "SI.Bindable.LookupDataHandler");
            DumpNamedTypeMetadata(sb, "FM.UI.PlayerHistoryDataHandler");

            Save(sb);
        }

        private static void TestTypedValueBridge(StringBuilder sb, int index)
        {
            try
            {
                var pr = new PersonReference(index);
                if (pr == null)
                {
                    sb.AppendLine("INDEX " + index + " PersonReference=<null>");
                    return;
                }

                var tv = TypedValue.GetReferenceTypedValue();
                if (tv == null)
                {
                    sb.AppendLine("INDEX " + index + " GetReferenceTypedValue=<null>");
                    return;
                }

                string beforeType = SafeDataType(tv);
                string beforeText = SafeAsString(tv);
                sb.AppendLine("INDEX " + index + " before SetValue tvPtr=0x" + tv.Pointer.ToString("X") + " type=" + beforeType + " text='" + beforeText + "'");

                try
                {
                    tv.SetValue(pr);
                    sb.AppendLine("INDEX " + index + " SetValue(PersonReference)=OK");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("INDEX " + index + " SetValue(PersonReference) failed: " + ex.GetType().Name + " - " + ex.Message);
                    return;
                }

                string afterType = SafeDataType(tv);
                string afterText = SafeAsString(tv);
                sb.AppendLine("INDEX " + index + " after SetValue type=" + afterType + " text='" + afterText + "' prData1=" + Safe(() => pr.Data1.ToString()) + " prID='" + Safe(() => pr.ID.ToString()) + "'");
            }
            catch (Exception ex)
            {
                sb.AppendLine("INDEX " + index + " bridge failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static string SafeDataType(TypedValue tv)
        {
            try { return tv.DataType == null ? "<null>" : tv.DataType.FullName; }
            catch (Exception ex) { return "<" + ex.GetType().Name + ">"; }
        }

        private static string SafeAsString(TypedValue tv)
        {
            try { return tv.AsString() ?? ""; }
            catch (Exception ex) { return "<" + ex.GetType().Name + ": " + ex.Message + ">"; }
        }

        private static string Safe(Func<string> fn)
        {
            try { return fn(); }
            catch (Exception ex) { return "<" + ex.GetType().Name + ": " + ex.Message + ">"; }
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
            sb.AppendLine("TYPE " + (t.FullName ?? t.Name));
            try { sb.AppendLine("  BaseType=" + (t.BaseType == null ? "<null>" : (t.BaseType.FullName ?? t.BaseType.Name))); } catch { }

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
            try { return t == null ? "?" : (t.FullName ?? t.Name); }
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
                string file = Path.Combine(dir, "resolverbridge_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
