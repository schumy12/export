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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.17.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.17 PERSON REFERENCE SEMANTICS - press F8 after loading a save.");
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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.17 PERSON REFERENCE SEMANTICS ===");
            sb.AppendLine("Previous result established that PersonReference(int index) already packs DatabaseTableType.Person automatically.");
            sb.AppendLine("PersonReference.UID schema key=" + PersonReference.UID);
            sb.AppendLine();

            sb.AppendLine("=== KNOWN STATIC / SINGLETON SHAPE ===");
            try { sb.AppendLine("PersonReference.Identifier=" + PersonReference.Identifier.ToString()); }
            catch (Exception ex) { sb.AppendLine("Identifier failed: " + ex.GetType().Name + " - " + ex.Message); }

            try
            {
                var inst = PersonReference.GetInstance();
                if (inst == null)
                    sb.AppendLine("PersonReference.GetInstance()=<null>");
                else
                    DumpReference(sb, inst, "GetInstance");
            }
            catch (Exception ex)
            {
                sb.AppendLine("PersonReference.GetInstance failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("=== DIRECT ACCESS TESTS ===");
            int[] probes = new[] { 0, 1, 2, 10, 100, 1000, 1999, 2000, 5000, 10000, 25000, 50000, 75000, 100000, 250000, 500000, 1000000 };
            foreach (int index in probes)
                ProbeIndex(sb, index);

            sb.AppendLine();
            sb.AppendLine("=== PERSONREFERENCE FULL MANAGED METADATA (NO GETTERS INVOKED) ===");
            DumpTypeMetadata(sb, typeof(PersonReference));
            DumpNamedTypeMetadata(sb, "FM.UI.DatabaseRecordReference");
            DumpNamedTypeMetadata(sb, "FM.UI.IPlayerReference");
            DumpNamedTypeMetadata(sb, "FM.UI.PlayerAttributeReference");
            DumpNamedTypeMetadata(sb, "FM.UI.AttributeNameAndValueReference");
            DumpNamedTypeMetadata(sb, "FM.UI.AttributeValueReference");

            Save(sb);
        }

        private static void ProbeIndex(StringBuilder sb, int index)
        {
            try
            {
                var pr = new PersonReference(index);
                if (pr == null)
                {
                    sb.AppendLine("INDEX " + index + " -> <null>");
                    return;
                }

                string type = "?";
                string realIndex = "?";
                string combined = "?";
                string data1 = "?";
                try { type = pr.Type.ToString(); } catch { }
                try { realIndex = pr.m_index.ToString(); } catch { }
                try { combined = pr.CombinedIndexAndType.ToString(); } catch { }
                try { data1 = pr.Data1.ToString(); } catch { }

                int propertySlot;
                bool hasProperty = false;
                string propError = "";
                try { hasProperty = pr.TryGetProperty(PersonReference.UID, out propertySlot); }
                catch (Exception ex) { propertySlot = 0; propError = ex.GetType().Name + ": " + ex.Message; }

                int uid;
                bool hasValue = false;
                string valueError = "";
                try { hasValue = pr.TryGetValue(PersonReference.UID, out uid); }
                catch (Exception ex) { uid = 0; valueError = ex.GetType().Name + ": " + ex.Message; }

                sb.AppendLine("INDEX " + index +
                    " type=" + type +
                    " m_index=" + realIndex +
                    " combined=" + combined +
                    " Data1=" + data1 +
                    " TryGetProperty(UID)=" + hasProperty +
                    " propertySlot=" + propertySlot +
                    (propError.Length == 0 ? "" : " propertyError='" + propError + "'") +
                    " TryGetValue(UID)=" + hasValue +
                    " uid=" + uid +
                    (valueError.Length == 0 ? "" : " valueError='" + valueError + "'"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("INDEX " + index + " ctor failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static void DumpReference(StringBuilder sb, PersonReference pr, string label)
        {
            string type = "?";
            string index = "?";
            string combined = "?";
            string data1 = "?";
            string id = "?";
            try { type = pr.Type.ToString(); } catch { }
            try { index = pr.m_index.ToString(); } catch { }
            try { combined = pr.CombinedIndexAndType.ToString(); } catch { }
            try { data1 = pr.Data1.ToString(); } catch { }
            try { id = pr.ID.ToString(); } catch (Exception ex) { id = "<" + ex.GetType().Name + ">"; }
            sb.AppendLine(label + " ptr=0x" + pr.Pointer.ToString("X") + " type=" + type + " m_index=" + index + " combined=" + combined + " Data1=" + data1 + " ID=" + id);
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
                string file = Path.Combine(dir, "personsemantics_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
