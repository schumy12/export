using System;
using System.IO;
using System.Text;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using SI.Bindable;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.33.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.33 BINDING DATA PLUMBING METADATA - press F8 after loading a save.");
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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.33 BINDING DATA PLUMBING METADATA ===");
            sb.AppendLine("0.32.1 proved InteropDataHandler.OpenChannel queues a backend request and the queue is consumed, but direct handler.OpenChannel does not attach returned data to our synthetic Bindings node.");
            sb.AppendLine("Goal: map the exact Bindings Node/Data/DataKey/OpenRequest plumbing needed to let BindingSubsystem own the channel lifecycle.");
            sb.AppendLine("Metadata reflection only; no generated FM/SI property getters are invoked through reflection.");
            sb.AppendLine();

            DumpType(sb, typeof(Bindings));
            DumpType(sb, typeof(Bindings.Node));
            DumpType(sb, typeof(Bindings.Data));
            DumpType(sb, typeof(Bindings.DataKey));
            DumpType(sb, typeof(Bindings.OpenRequest));
            DumpType(sb, typeof(IReadOnlyNode));
            DumpType(sb, typeof(IReadOnlyData));
            DumpType(sb, typeof(BindingSubsystem));

            sb.AppendLine();
            sb.AppendLine("=== METHODS CONTAINING OPEN / DATA / TARGET / NODE ===");
            DumpFilteredMethods(sb, typeof(Bindings));
            DumpFilteredMethods(sb, typeof(BindingSubsystem));
            DumpFilteredMethods(sb, typeof(Bindings.Node));
            DumpFilteredMethods(sb, typeof(Bindings.Data));

            Save(sb);
        }

        private static void DumpType(StringBuilder sb, Type t)
        {
            sb.AppendLine("TYPE " + t.FullName);
            sb.AppendLine("  Assembly=" + t.Assembly.GetName().Name);
            sb.AppendLine("  BaseType=" + (t.BaseType == null ? "<null>" : t.BaseType.FullName));

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var p in t.GetProperties(flags))
                sb.AppendLine("  PROP " + p.Name + " : " + TypeName(p.PropertyType));

            foreach (var f in t.GetFields(flags))
                sb.AppendLine("  FIELD " + f.Name + " : " + TypeName(f.FieldType));

            foreach (var c in t.GetConstructors(flags))
                sb.AppendLine("  CTOR " + c);

            foreach (var m in t.GetMethods(flags))
                sb.AppendLine("  METHOD " + FormatMethod(m));

            sb.AppendLine();
        }

        private static void DumpFilteredMethods(StringBuilder sb, Type t)
        {
            sb.AppendLine("FILTERED " + t.FullName);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMethods(flags))
            {
                string n = m.Name ?? "";
                if (n.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Node", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Get", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.AppendLine("  " + FormatMethod(m));
                }
            }
            sb.AppendLine();
        }

        private static string FormatMethod(MethodInfo m)
        {
            var s = new StringBuilder();
            s.Append(m.IsPublic ? "public " : (m.IsPrivate ? "private " : (m.IsFamily ? "protected " : "internal ")));
            if (m.IsStatic) s.Append("static ");
            s.Append(TypeName(m.ReturnType)).Append(" ").Append(m.Name).Append("(");
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) s.Append(", ");
                if (ps[i].IsOut) s.Append("out ");
                else if (ps[i].ParameterType.IsByRef) s.Append("ref ");
                var pt = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                s.Append(TypeName(pt)).Append(" ").Append(ps[i].Name);
            }
            s.Append(")");
            return s.ToString();
        }

        private static string TypeName(Type t)
        {
            if (t == null) return "<null>";
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var name = def.FullName;
                int tick = name == null ? -1 : name.IndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);
                var args = t.GetGenericArguments();
                var sb = new StringBuilder(name ?? def.Name);
                sb.Append("<");
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(TypeName(args[i]));
                }
                sb.Append(">");
                return sb.ToString();
            }
            return t.FullName ?? t.Name;
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "bindingplumbing_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
