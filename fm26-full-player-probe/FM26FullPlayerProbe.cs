using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FM.UI;
using FM.Game;
using FM.GamePlugin;
using SI.Core;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.52.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.52 LEAN+CACHE FAST MULTI-SELECT - select player rows and press F8 once.");
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
        private enum TargetMode { Normal, ResolveReferenceName, Footedness }

        private struct Target
        {
            public string OutputName;
            public string PropertyName;
            public uint Id;
            public TargetMode Mode;
            public Target(string outputName, string propertyName, uint id, TargetMode mode = TargetMode.Normal)
            {
                OutputName = outputName;
                PropertyName = propertyName;
                Id = id;
                Mode = mode;
            }
        }

        private sealed class PlayerRow
        {
            public TypedValue Source;
            public int PersonIndex;
            public long PersonData1;
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        }

        private static readonly Target[] Targets = new Target[]
        {
            new Target("UniqueId", "UniqueId", 1970170212u),
            new Target("Name", "Name", 1851878757u),
            new Target("Surname", "Surname", 843789105u),
            new Target("ShirtName", "ShirtName", 1482779476u),
            new Target("Age", "Age", 825565216u),
            new Target("DateOfBirth", "DateOfBirth", 1348759394u),
            new Target("Height", "Height", 825761824u),
            new Target("Nationality", "NationalityText", 1851880537u),
            new Target("Nationalities", "NationalitiesText", 1851880532u),
            new Target("CityOfBirth", "CityOfBirth", 1668245090u, TargetMode.ResolveReferenceName),
            new Target("NationOfBirth", "NationOfBirth", 1349414754u, TargetMode.ResolveReferenceName),
            new Target("Club", "Club", 825630752u, TargetMode.ResolveReferenceName),
            new Target("Team", "Team", 1415930221u, TargetMode.ResolveReferenceName),
            new Target("Footedness", "Footedness", 1244885353u, TargetMode.Footedness),
            new Target("CurrentReputation", "PlayerCurrentReputation", 1146252104u),
            new Target("HomeReputation", "PlayerHomeReputation", 1346916944u),
            new Target("WorldReputation", "PlayerWorldReputation", 1347899984u),
            new Target("Personality", "Personality", 1349742196u),
            new Target("BestPosition", "BestPositionShortString", 1349546835u),
            new Target("NaturalPosition", "NaturalPositionShortString", 1349546834u),
            new Target("Positions", "PositionCombinedStringLong", 2019119186u),

            new Target("PlayerCurrentAbility", "PlayerCurrentAbility", 1346584898u),
            new Target("PlayerPotentialAbility", "PlayerPotentialAbility", 1347436866u),

            new Target("Adaptability", "AttributeAdaptability", 1348559969u),
            new Target("Ambition", "AttributeAmbition", 1348562274u),
            new Target("Controversy", "AttributeControversy", 1348695673u),
            new Target("Loyalty", "AttributeLoyalty", 1349283705u),
            new Target("Pressure", "AttributePressure", 1349546597u),
            new Target("Professionalism", "AttributeProfessionalism", 1349546607u),
            new Target("Sportsmanship", "AttributeSportsmanship", 1349742703u),
            new Target("Temperament", "AttributeTemperament", 1349805421u),
            new Target("Consistency", "Consistency", 1346588494u),
            new Target("ImportantMatches", "ImportantMatches", 1349086576u),
            new Target("InjuryProneness", "InjuryProneness", 1349087346u),
            new Target("Versatility", "Versatility", 1349936498u),

            new Target("Acceleration", "Acceleration", 892805152u),
            new Target("AerialReach", "AerialReach", 926232624u),
            new Target("Aggression", "Aggression", 875765792u),
            new Target("Agility", "Agility", 892870688u),
            new Target("Anticipation", "Anticipation", 875831328u),
            new Target("Balance", "Balance", 892936224u),
            new Target("Bravery", "Bravery", 875896864u),
            new Target("CommandOfArea", "CommandOfArea", 909516833u),
            new Target("Communication", "Communication", 909582369u),
            new Target("Composure", "Composure", 875962400u),
            new Target("Concentration", "Concentration", 876027936u),
            new Target("Corners", "Corners", 842604576u),
            new Target("Crossing", "Crossing", 858791968u),
            new Target("Decisions", "Decisions", 876093472u),
            new Target("Determination", "Determination", 876159008u),
            new Target("Dribbling", "Dribbling", 858857504u),
            new Target("Eccentricity", "Eccentricity", 909647905u),
            new Target("Finishing", "Finishing", 858923040u),
            new Target("FirstTouch", "FirstTouch", 858988576u),
            new Target("Flair", "Flair", 892346400u),
            new Target("FreeKicks", "FreeKicks", 859054112u),
            new Target("Handling", "Handling", 909713441u),
            new Target("Heading", "Heading", 859119648u),
            new Target("JumpingReach", "JumpingReach", 909123616u),
            new Target("Kicking", "Kicking", 925900832u),
            new Target("Leadership", "Leadership", 892411936u),
            new Target("LongShots", "LongShots", 859185184u),
            new Target("LongThrows", "LongThrows", 859250720u),
            new Target("Marking", "Marking", 859316256u),
            new Target("Movement", "Movement", 892477472u),
            new Target("NaturalFitness", "NaturalFitness", 909189152u),
            new Target("OneOnOnes", "OneOnOnes", 926167088u),
            new Target("Pace", "Pace", 909254688u),
            new Target("Passing", "Passing", 859381792u),
            new Target("PenaltyTaking", "PenaltyTaking", 875569184u),
            new Target("Positioning", "Positioning", 892543008u),
            new Target("Reflexes", "Reflexes", 925966369u),
            new Target("RushingOut", "RushingOut", 925970480u),
            new Target("Stamina", "Stamina", 909320224u),
            new Target("Strength", "Strength", 909385760u),
            new Target("Tackling", "Tackling", 875634720u),
            new Target("Teamwork", "Teamwork", 892608544u),
            new Target("Technique", "Technique", 875700256u),
            new Target("TendencyToPunch", "TendencyToPunch", 926036016u),
            new Target("Throwing", "Throwing", 926101552u),
            new Target("Vision", "Vision", 892674080u),
            new Target("WorkRate", "WorkRate", 892739616u)
        };

        private const uint NamePropertyId = 1851878757u;
        private const float PollDelaySeconds = 0.02f;
        private const float QueryTimeoutSeconds = 2.00f;

        private readonly List<PlayerRow> _players = new List<PlayerRow>();
        private readonly Dictionary<string, string> _referenceNameCache = new Dictionary<string, string>();
        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private IDataHandler _handlerInterface;
        private GameInteropSubsystem _interop;
        private Bindings.Key _key;
        private Bindings.Node _node;
        private Bindings.Data _data;
        private TypedValue _activeSource;
        private StringBuilder _log;
        private int _playerIndex;
        private int _targetIndex;
        private bool _waiting;
        private bool _channelOpen;
        private bool _nativeNodeAdded;
        private bool _resolvingReferenceName;
        private string _pendingReferenceCacheKey;
        private float _pollAfter;
        private float _timeoutAt;
        private float _queryStartedAt;
        private int _timeoutCount;
        private int _cacheHitCount;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private PlayerRow CurrentPlayer { get { return _players[_playerIndex]; } }

        private void Update()
        {
            try
            {
                if (!_waiting && _log == null && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) StartExport();
                if (_waiting) PollCurrentQuery();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                try { _log?.AppendLine("UPDATE/FATAL: " + ex); } catch { }
                SaveAndReset(false);
            }
        }

        private void PollCurrentQuery()
        {
            float now = Time.unscaledTime;
            if (now < _pollAfter) return;

            bool ready = false;
            try { ready = _data != null && _data.IsSet; } catch { }

            if (ready)
            {
                FinishCurrentQuery(false);
                return;
            }

            if (now >= _timeoutAt)
            {
                _timeoutCount++;
                try
                {
                    var t = Targets[_targetIndex];
                    _log.AppendLine("  TIMEOUT after " + ((now - _queryStartedAt) * 1000f).ToString("0") + " ms on " + t.OutputName + (_resolvingReferenceName ? " nested Name" : ""));
                }
                catch { }
                FinishCurrentQuery(true);
            }
        }

        private void StartExport()
        {
            _players.Clear();
            _referenceNameCache.Clear();
            _timeoutCount = 0;
            _cacheHitCount = 0;
            _pendingReferenceCacheKey = "";
            _log = new StringBuilder();
            _log.AppendLine("=== FM26 FULL PLAYER PROBE 0.52 LEAN+CACHE FAST MULTI-SELECT ===");
            _log.AppendLine("Based on 0.51 fast polling. Removes optional/redundant Gender, IsPlayer, IsEuNational and CompetentPositions queries, and caches resolved reference names.");
            _log.AppendLine("TargetsPerPlayer=" + Targets.Length + " pollDelay=" + PollDelaySeconds.ToString("0.00") + "s timeout=" + QueryTimeoutSeconds.ToString("0.00") + "s");
            _log.AppendLine();

            var showPerson = FindSelectedShowPerson(_log);
            if (showPerson == null) { _log.AppendLine("RESULT: selected ShowPerson NOT FOUND"); SaveAndReset(false); return; }

            try
            {
                BindingPath path; ActionGroupings groupings; bool multiple;
                var objects = PluginContextMenuContributor.GetContextMenuObjects(showPerson, out path, out groupings, out multiple);
                _log.AppendLine("GetContextMenuObjects count=" + (objects == null ? -1 : objects.Count) + " multiple=" + multiple);
                if (objects == null || objects.Count == 0) { _log.AppendLine("RESULT: no context objects"); SaveAndReset(false); return; }

                var seen = new HashSet<long>();
                for (int i = 0; i < objects.Count; i++)
                {
                    var tv = objects[i];
                    if (SafeType(tv) != "FM.UI.PersonReference") continue;
                    try
                    {
                        var raw = tv.Get();
                        var pr = new PersonReference(raw.Pointer);
                        long d1 = pr.Data1;
                        if (seen.Contains(d1)) continue;
                        seen.Add(d1);
                        _players.Add(new PlayerRow { Source = tv, PersonIndex = pr.m_index, PersonData1 = d1 });
                        _log.AppendLine("SELECTED[" + (_players.Count - 1) + "] Data1=" + d1 + " m_index=" + pr.m_index + " combined=" + pr.CombinedIndexAndType);
                    }
                    catch (Exception ex) { _log.AppendLine("object[" + i + "] PersonReference parse FAIL: " + ex.GetType().Name + " - " + ex.Message); }
                }
                if (_players.Count == 0) { _log.AppendLine("RESULT: no PersonReference sources"); SaveAndReset(false); return; }
                _log.AppendLine("SELECTED PLAYERS=" + _players.Count);
            }
            catch (Exception ex)
            {
                _log.AppendLine("context/source FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(false); return;
            }

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null) { _log.AppendLine("RESULT: BindingSubsystem NOT FOUND"); SaveAndReset(false); return; }
            _handler = FindLiveInteropHandler(_log, _bindings);
            if (_handler == null || _handlerInterface == null || _interop == null) { _log.AppendLine("RESULT: Interop handler/subsystem NOT FOUND"); SaveAndReset(false); return; }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_lean_cache_fast_multi_select");
                _log.AppendLine("NODE key=" + _key.m_key + " valid=" + _key.IsValid() + " exists=" + _bindings.Exists(ref _key));
                if (_bindings.m_nodes == null || !_bindings.m_nodes.ContainsKey(_key.m_key)) { _log.AppendLine("RESULT: created key not present in m_nodes"); SaveAndReset(false); return; }
                _node = _bindings.m_nodes[_key.m_key];
                if (_node == null) { _log.AppendLine("RESULT: m_nodes entry is null"); SaveAndReset(false); return; }
                _playerIndex = 0;
                _targetIndex = 0;
                StartCurrentTarget();
            }
            catch (Exception ex)
            {
                _log.AppendLine("START FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(false);
            }
        }

        private void StartCurrentTarget()
        {
            if (_playerIndex >= _players.Count)
            {
                _log.AppendLine();
                _log.AppendLine("RESULT: all selected players completed; writing CSV; timeouts=" + _timeoutCount + " cacheHits=" + _cacheHitCount + " cacheEntries=" + _referenceNameCache.Count);
                SaveAndReset(true);
                return;
            }

            if (_targetIndex >= Targets.Length)
            {
                _log.AppendLine("PLAYER COMPLETE [" + (_playerIndex + 1) + "/" + _players.Count + "] index=" + CurrentPlayer.PersonIndex);
                _playerIndex++;
                _targetIndex = 0;
                StartCurrentTarget();
                return;
            }

            var t = Targets[_targetIndex];
            _resolvingReferenceName = false;
            _pendingReferenceCacheKey = "";
            _activeSource = CurrentPlayer.Source;
            _log.AppendLine("P" + (_playerIndex + 1) + "/" + _players.Count + " T" + (_targetIndex + 1) + "/" + Targets.Length + " " + t.OutputName + " <= " + t.PropertyName);
            StartQuery(_activeSource, t.PropertyName, t.Id);
        }

        private void StartQuery(TypedValue source, string propertyName, uint propertyId)
        {
            var propId = new PropertyID(propertyId);
            _node.m_propID = propId;
            _data = _bindings.GetNewData();
            if (_data == null) throw new Exception("GetNewData returned null");
            _bindings.SetTargetData(_data, _node);
            var dataKey = _node.m_dataKey;
            var parentKey = _node.m_parent == null ? default(Bindings.Key) : _node.m_parent.m_key;

            dynamic runtimeInterop = _interop;
            runtimeInterop.AddNode(_key, parentKey, propId);
            _nativeNodeAdded = true;
            _interop.AddData(dataKey);
            _interop.SetTarget(_key, dataKey);
            _data.handler = _handlerInterface;
            _data.opener = _key;

            var property = new Bindings.Property(propertyName, propId);
            var contexts = new Il2CppSystem.Collections.Generic.List<string>();
            bool canHandle = _handler.CanHandle(source, property, contexts);
            _log.AppendLine("  OPEN sourceType=" + SafeType(source) + " canHandle=" + canHandle);
            _handler.OpenChannel(source, property, _key);
            _channelOpen = true;
            _waiting = true;
            _queryStartedAt = Time.unscaledTime;
            _pollAfter = _queryStartedAt + PollDelaySeconds;
            _timeoutAt = _queryStartedAt + QueryTimeoutSeconds;
        }

        private void FinishCurrentQuery(bool timedOut)
        {
            _waiting = false;
            var t = Targets[_targetIndex];
            TypedValue tv = null;
            bool isSet = false;
            try
            {
                isSet = _data != null && _data.IsSet;
                tv = _data == null ? null : _data.Value;
            }
            catch (Exception ex) { _log.AppendLine("  READ FAIL: " + ex.GetType().Name + " - " + ex.Message); }

            if (_resolvingReferenceName)
            {
                string resolved = !timedOut && isSet && tv != null ? CleanUiString(SafeText(tv)) : "";
                CurrentPlayer.Values[t.OutputName] = resolved;
                if (!string.IsNullOrEmpty(_pendingReferenceCacheKey) && !string.IsNullOrEmpty(resolved))
                    _referenceNameCache[_pendingReferenceCacheKey] = resolved;
                _log.AppendLine("  RESOLVED='" + resolved + "'");
                CleanupCurrentGraph();
                _resolvingReferenceName = false;
                _pendingReferenceCacheKey = "";
                _targetIndex++;
                StartCurrentTarget();
                return;
            }

            if (!timedOut && t.Mode == TargetMode.ResolveReferenceName && isSet && tv != null && SafeType(tv).EndsWith("Reference"))
            {
                string cacheKey = GetReferenceCacheKey(tv);
                string cached;
                if (!string.IsNullOrEmpty(cacheKey) && _referenceNameCache.TryGetValue(cacheKey, out cached))
                {
                    CurrentPlayer.Values[t.OutputName] = cached;
                    _cacheHitCount++;
                    _log.AppendLine("  CACHE HIT '" + cached + "'");
                    CleanupCurrentGraph();
                    _targetIndex++;
                    StartCurrentTarget();
                    return;
                }

                var nestedSource = tv;
                _pendingReferenceCacheKey = cacheKey;
                CleanupCurrentGraph();
                _resolvingReferenceName = true;
                _activeSource = nestedSource;
                try { StartQuery(_activeSource, "Name", NamePropertyId); }
                catch (Exception ex)
                {
                    _log.AppendLine("  NESTED NAME FAIL: " + ex.GetType().Name + " - " + ex.Message);
                    CurrentPlayer.Values[t.OutputName] = "";
                    _resolvingReferenceName = false;
                    _pendingReferenceCacheKey = "";
                    _targetIndex++;
                    StartCurrentTarget();
                }
                return;
            }

            string finalValue = "";
            try
            {
                if (timedOut || !isSet || tv == null) finalValue = "";
                else if (t.Mode == TargetMode.Footedness) finalValue = ParsePreferredFoot(tv);
                else if (SafeType(tv) == "SI.Bindable.DynamicReference")
                {
                    var dyn = VisualFunctionLibrary.GetDynamicReference(tv);
                    var inner = VisualFunctionLibrary.GetPropertyValue(dyn);
                    finalValue = CleanUiString(SafeText(inner));
                }
                else finalValue = CleanUiString(SafeText(tv));
            }
            catch (Exception ex)
            {
                _log.AppendLine("  VALUE CONVERT FAIL: " + ex.GetType().Name + " - " + ex.Message);
                finalValue = "";
            }

            CurrentPlayer.Values[t.OutputName] = finalValue;
            _log.AppendLine("  VALUE='" + finalValue + "' responseMs=" + ((Time.unscaledTime - _queryStartedAt) * 1000f).ToString("0"));
            CleanupCurrentGraph();
            _targetIndex++;
            StartCurrentTarget();
        }

        private static string GetReferenceCacheKey(TypedValue tv)
        {
            try
            {
                if (tv == null) return "";
                var raw = tv.Get();
                if (raw == null) return "";
                return SafeType(tv) + ":" + raw.Pointer.ToString("X");
            }
            catch { return ""; }
        }

        private string ParsePreferredFoot(TypedValue tv)
        {
            if (SafeType(tv) != "SI.Bindable.DynamicReference") return CleanUiString(SafeText(tv));
            var dyn = VisualFunctionLibrary.GetDynamicReference(tv);
            string preferred = "";
            foreach (uint key in dyn.Keys)
            {
                TypedValue value = null;
                try { value = dyn[key]; } catch { continue; }
                if (SafeType(value) != "System.String") continue;
                string val = CleanUiString(SafeText(value));
                if (val == "Sinistro" || val == "Destro" || val == "Entrambi" || val == "Sinistra" || val == "Destra")
                {
                    preferred = val;
                    break;
                }
            }
            return preferred;
        }

        private void CleanupCurrentGraph()
        {
            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); }
                catch (Exception ex) { try { _log?.AppendLine("  native RemoveNode FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
                _nativeNodeAdded = false;
            }
            _data = null;
        }

        private InteropDataHandler FindLiveInteropHandler(StringBuilder sb, BindingSubsystem bindings)
        {
            try
            {
                var registry = bindings.m_handlers;
                if (registry == null) return null;
                foreach (var pair in registry)
                {
                    string n = ""; try { n = pair.Key == null ? "" : pair.Key.FullName; } catch { }
                    if (n == null || !n.Contains("InteropReference")) continue;
                    var list = pair.Value; if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var h = list[i]; if (h == null) continue;
                        try
                        {
                            var concrete = new InteropDataHandler(h.Pointer);
                            if (concrete.m_interop != null)
                            {
                                _handlerInterface = h;
                                _interop = new GameInteropSubsystem(concrete.m_interop.Pointer);
                                sb.AppendLine("InteropDataHandler ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + _interop.Pointer.ToString("X"));
                                return concrete;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("Find handler FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }

        private static string SafeType(TypedValue tv) { try { return tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { return "<failed>"; } }
        private static string SafeText(TypedValue tv) { try { return tv == null ? "" : (tv.AsString() ?? ""); } catch { return ""; } }

        private static string CleanUiString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int marker = s.LastIndexOf('\u0002');
            if (marker >= 0 && marker + 1 < s.Length) s = s.Substring(marker + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) if (!char.IsControl(s[i])) sb.Append(s[i]);
            return sb.ToString().Trim();
        }

        private static VisualElement FindSelectedShowPerson(StringBuilder sb)
        {
            try
            {
                var docs = Resources.FindObjectsOfTypeAll<UIDocument>();
                sb.AppendLine("UIDocument count=" + (docs == null ? 0 : docs.Length));
                if (docs == null) return null;
                for (int i = 0; i < docs.Length; i++)
                {
                    var doc = docs[i]; if (doc == null) continue;
                    VisualElement root = null; try { root = doc.rootVisualElement; } catch { }
                    var found = FindSelectedRecursive(root);
                    if (found != null) { sb.AppendLine("selected element found in UIDocument[" + i + "]"); return found; }
                }
            }
            catch (Exception ex) { sb.AppendLine("FindSelected FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }

        private static VisualElement FindSelectedRecursive(VisualElement ve)
        {
            if (ve == null) return null;
            try { if (ve.ClassListContains("virtualised-list__item--selected")) { var show = FindNamedRecursive(ve, "ShowPerson"); if (show != null) return show; } } catch { }
            int count = 0; try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++) { VisualElement child = null; try { child = ve[i]; } catch { } var found = FindSelectedRecursive(child); if (found != null) return found; }
            return null;
        }

        private static VisualElement FindNamedRecursive(VisualElement ve, string name)
        {
            if (ve == null) return null;
            try { if (ve.name == name) return ve; } catch { }
            int count = 0; try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++) { VisualElement child = null; try { child = ve[i]; } catch { } var found = FindNamedRecursive(child, name); if (found != null) return found; }
            return null;
        }

        private static unsafe Bindings.Key CreateTemporaryNode(BindingSubsystem bindings, string name)
        {
            fixed (char* p = name) { var span = new Il2CppSystem.ReadOnlySpan<char>((void*)p, name.Length); return bindings.Create(ref span, Bindings.NodeFlags.Temporary); }
        }

        private void CloseChannel()
        {
            if (!_channelOpen || _handler == null) return;
            try { _handler.CloseChannel(_key); } catch (Exception ex) { try { _log?.AppendLine("CLOSE FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
            _channelOpen = false;
        }

        private static string Csv(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private void SaveAndReset(bool writeCsv)
        {
            CleanupCurrentGraph();
            _waiting = false;
            if (_log != null)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                    Directory.CreateDirectory(dir);
                    string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string logFile = Path.Combine(dir, "multiselect_" + stamp + ".txt");
                    File.WriteAllText(logFile, _log.ToString(), Encoding.UTF8);
                    Plugin.Log.LogInfo("[FM26FullProbe] Saved log: " + logFile);

                    if (writeCsv)
                    {
                        var csv = new StringBuilder();
                        csv.Append("PersonIndex,PersonData1");
                        for (int i = 0; i < Targets.Length; i++) csv.Append("," + Csv(Targets[i].OutputName));
                        csv.AppendLine();
                        for (int p = 0; p < _players.Count; p++)
                        {
                            var player = _players[p];
                            csv.Append(player.PersonIndex + "," + player.PersonData1);
                            for (int i = 0; i < Targets.Length; i++)
                            {
                                string v; player.Values.TryGetValue(Targets[i].OutputName, out v);
                                csv.Append("," + Csv(v));
                            }
                            csv.AppendLine();
                        }
                        string csvFile = Path.Combine(dir, "multiselect_" + stamp + ".csv");
                        File.WriteAllText(csvFile, csv.ToString(), new UTF8Encoding(true));
                        Plugin.Log.LogInfo("[FM26FullProbe] Saved CSV: " + csvFile);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
            }

            _data = null; _node = null; _activeSource = null;
            _handlerInterface = null; _handler = null; _interop = null; _bindings = null; _log = null;
            _players.Clear(); _referenceNameCache.Clear(); _resolvingReferenceName = false; _pendingReferenceCacheKey = "";
        }
    }
}
