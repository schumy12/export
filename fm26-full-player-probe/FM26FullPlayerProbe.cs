using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.19.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.19 REAL PERSON PROPERTY PROBE - press F8 after loading a save.");
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
        private const uint UniqueId = 1970170212u;
        private const uint IsPlayer = 862938733u;
        private const uint PlayerCurrentAbility = 1346584898u;
        private const uint PlayerPotentialAbility = 1347436866u;
        private const uint AttributeProfessionalism = 1349546607u;
        private const uint AttributeAmbition = 1348562274u;
        private const uint AttributePressure = 1349546597u;
        private const uint AttributeSportsmanship = 1349742703u;
        private const uint AttributeTemperament = 1349805421u;
        private const uint Consistency = 1346588494u;
        private const uint ImportantMatches = 1349086576u;
        private const uint InjuryProneness = 1349087346u;
        private const uint Versatility = 1349936498u;
        private const uint AttributeAcceleration = 892805152u;
        private const uint AttributeFinishing = 858923040u;

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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.19 REAL PERSON PROPERTY PROBE ===");
            sb.AppendLine("Uses property IDs discovered from PersonReference.GetPropertiesInternal().");
            sb.AppendLine();

            sb.AppendLine("=== SCHEMA ACCEPTANCE ===");
            DumpAcceptance(sb, "UniqueId", UniqueId);
            DumpAcceptance(sb, "IsPlayer", IsPlayer);
            DumpAcceptance(sb, "PlayerCurrentAbility", PlayerCurrentAbility);
            DumpAcceptance(sb, "PlayerPotentialAbility", PlayerPotentialAbility);
            DumpAcceptance(sb, "AttributeProfessionalism", AttributeProfessionalism);
            DumpAcceptance(sb, "Consistency", Consistency);
            DumpAcceptance(sb, "ImportantMatches", ImportantMatches);
            DumpAcceptance(sb, "InjuryProneness", InjuryProneness);
            DumpAcceptance(sb, "Versatility", Versatility);
            DumpAcceptance(sb, "AttributeAcceleration", AttributeAcceleration);

            sb.AppendLine();
            sb.AppendLine("=== FIXED INDEX TESTS ===");
            int[] fixedIndices = new[] { 0, 1, 2, 10, 100, 1000, 5000, 10000, 25000, 50000, 75000, 100000 };
            foreach (int index in fixedIndices)
                DumpIndex(sb, index, true);

            sb.AppendLine();
            sb.AppendLine("=== DIRECT PERSON TABLE SCAN 0..99999 ===");
            int hits = 0;
            int uniqueIdReads = 0;
            int caReads = 0;
            int paReads = 0;
            int errors = 0;

            for (int index = 0; index < 100000 && hits < 100; index++)
            {
                try
                {
                    var pr = new PersonReference(index);
                    if (pr == null) continue;

                    int uniqueId;
                    if (!pr.TryGetValue(UniqueId, out uniqueId))
                        continue;

                    uniqueIdReads++;
                    if (uniqueId <= 0) continue;

                    int isPlayer;
                    bool hasIsPlayer = pr.TryGetValue(IsPlayer, out isPlayer);

                    int ca;
                    bool hasCa = pr.TryGetValue(PlayerCurrentAbility, out ca);
                    if (hasCa) caReads++;

                    int pa;
                    bool hasPa = pr.TryGetValue(PlayerPotentialAbility, out pa);
                    if (hasPa) paReads++;

                    hits++;
                    sb.Append("HIT index=" + index + " uniqueId=" + uniqueId);
                    sb.Append(" isPlayer=" + (hasIsPlayer ? isPlayer.ToString() : "<no>"));
                    sb.Append(" CA=" + (hasCa ? ca.ToString() : "<no>"));
                    sb.Append(" PA=" + (hasPa ? pa.ToString() : "<no>"));
                    AppendValue(sb, pr, " Prof", AttributeProfessionalism);
                    AppendValue(sb, pr, " Amb", AttributeAmbition);
                    AppendValue(sb, pr, " Pressure", AttributePressure);
                    AppendValue(sb, pr, " Sports", AttributeSportsmanship);
                    AppendValue(sb, pr, " Temp", AttributeTemperament);
                    AppendValue(sb, pr, " Cons", Consistency);
                    AppendValue(sb, pr, " ImpMatches", ImportantMatches);
                    AppendValue(sb, pr, " Injury", InjuryProneness);
                    AppendValue(sb, pr, " Vers", Versatility);
                    AppendValue(sb, pr, " Acc", AttributeAcceleration);
                    AppendValue(sb, pr, " Fin", AttributeFinishing);
                    sb.AppendLine();
                }
                catch (Exception ex)
                {
                    errors++;
                    if (errors <= 10)
                        sb.AppendLine("index=" + index + " ERROR " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            sb.AppendLine();
            sb.AppendLine("SUMMARY hits=" + hits + " uniqueIdReads=" + uniqueIdReads + " caReads=" + caReads + " paReads=" + paReads + " errors=" + errors);
            Save(sb);
        }

        private static void DumpAcceptance(StringBuilder sb, string name, uint id)
        {
            try
            {
                bool accepts = PersonReference.AcceptsPropertyInternal(id);
                string kind = "?";
                string desc = "";
                try { kind = PersonReference.GetPropertyTypeInternal(id).ToString(); } catch { }
                try { desc = PersonReference.GetPropertyDescriptionInternal(id) ?? ""; } catch { }
                sb.AppendLine(name + " id=" + id + " accepts=" + accepts + " kind=" + kind + " desc='" + desc + "'");
            }
            catch (Exception ex)
            {
                sb.AppendLine(name + " id=" + id + " acceptance failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static void DumpIndex(StringBuilder sb, int index, bool includeHidden)
        {
            try
            {
                var pr = new PersonReference(index);
                if (pr == null)
                {
                    sb.AppendLine("INDEX " + index + " -> <null>");
                    return;
                }

                int uid;
                bool hasUid = pr.TryGetValue(UniqueId, out uid);
                int isPlayer;
                bool hasPlayer = pr.TryGetValue(IsPlayer, out isPlayer);
                int ca;
                bool hasCa = pr.TryGetValue(PlayerCurrentAbility, out ca);
                int pa;
                bool hasPa = pr.TryGetValue(PlayerPotentialAbility, out pa);

                sb.Append("INDEX " + index + " uniqueId=" + (hasUid ? uid.ToString() : "<no>"));
                sb.Append(" isPlayer=" + (hasPlayer ? isPlayer.ToString() : "<no>"));
                sb.Append(" CA=" + (hasCa ? ca.ToString() : "<no>"));
                sb.Append(" PA=" + (hasPa ? pa.ToString() : "<no>"));
                if (includeHidden)
                {
                    AppendValue(sb, pr, " Prof", AttributeProfessionalism);
                    AppendValue(sb, pr, " Cons", Consistency);
                    AppendValue(sb, pr, " Acc", AttributeAcceleration);
                }
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine("INDEX " + index + " failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static void AppendValue(StringBuilder sb, PersonReference pr, string label, uint propertyId)
        {
            try
            {
                int value;
                bool ok = pr.TryGetValue(propertyId, out value);
                sb.Append(label + "=" + (ok ? value.ToString() : "<no>"));
            }
            catch (Exception ex)
            {
                sb.Append(label + "=<" + ex.GetType().Name + ">");
            }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "realpersonprobe_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
