using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ifapp.Game.Scenes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace InFalsusAdvVN;

[BepInPlugin(Guid, "InFalsusAdvVN", Version)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "aria.infalsus.advvn";
    public const string Version = "1.0.1";

    private const int RendererEnforceFrames = 120;

    private const string SectionLanguage = "LanguageSwitch";
    private const string SectionScroll = "Scroll";

    private static ManualLogSource _log;
    private static ConfigEntry<string> _hotkeyEntry;
    private static ConfigEntry<string> _orderEntry;
    private static ConfigEntry<bool> _verboseEntry;
    private static ConfigEntry<bool> _rememberEntry;
    private static ConfigEntry<bool> _visibilityEntry;
    private static ConfigEntry<bool> _engineTextEntry;
    private static ConfigEntry<bool> _wheelAdvanceEntry;
    private static ConfigEntry<bool> _wheelOpenLogEntry;

    private static GameBindings _bindings;
    private static StoryTextController _controller;
    private static readonly List<LanguageOption> Languages = new List<LanguageOption>();

    private static int _index = -1;
    private static int _pendingRestore;
    private static bool _faulted;
    private static bool _hotkeyHeld;
    private static Key _hotkey = Key.L;

    private static StoryScene _activeScene;
    private static int _enforceFrames;
    private static int _frame;
    private static bool _wheelLatched;
    private static int _wheelReportCount;
    private static int _lastGameLocalization = -1;
    private static IntPtr _lastStoryIdPointer;
    private static IntPtr _pendingScenePtr;
    private static int _reapplyFrames;
    private static bool _refreshing;

    /// <summary>Story language forced by the mod; 0 means "follow the game setting".</summary>
    internal static int StoryLocalization;

    public override void Load()
    {
        _log = Log;

        _hotkeyEntry = Config.Bind(SectionLanguage, "Hotkey", "L",
            "Key that cycles the story text language. It is only acted on while a story (visual novel) " +
            "scene is loaded. Unity Input System key name, e.g. L, D, F6, Tab.");
        _orderEntry = Config.Bind(SectionLanguage, "LanguageOrder", "English,ChineseSC,Japanese,ChineseTC",
            "Languages cycled by the hotkey. Accepted: English, Japanese, Korean, ChineseSC, ChineseTC " +
            "(or en / jp / ko / zh-hans / zh-hant). 'In Falsus' ships story translations for English, " +
            "Japanese, Simplified Chinese and Traditional Chinese; Korean falls back to English in the game data.");
        _rememberEntry = Config.Bind(SectionLanguage, "RememberSelection", true,
            "Remember the story language selected with the hotkey and re-apply it on the next launch.");
        _wheelAdvanceEntry = Config.Bind(SectionScroll, "WheelDownAdvances", true,
            "Mouse wheel down advances the story, like a left click (drives the scene's own input " +
            "trigger through StoryScene._dp).");
        _wheelOpenLogEntry = Config.Bind(SectionScroll, "WheelUpOpensLog", true,
            "Mouse wheel up opens the story history (LOG) panel, like clicking the LOG button.");
        _visibilityEntry = Config.Bind(SectionLanguage, "ManageRendererVisibility", false,
            "Also hide the renderer sets of the other languages after switching. The set for the selected " +
            "language is always activated; enable this only if leftover text from another language remains visible.");
        _engineTextEntry = Config.Bind("Advanced", "RewriteEngineText", false,
            "Also replace the text stored in StoryState.TextBoxState.CurrentText. This keeps the " +
            "typewriter in sync, but writing that structure is the most fragile interaction with the " +
            "game; leave it off unless the visible text reverts while a line is still being revealed.");
        _verboseEntry = Config.Bind("Debug", "VerboseLogging", false,
            "Log every resolved story string id and every applied text update.");

        ParseHotkey(_hotkeyEntry.Value);
        ParseLanguages(_orderEntry.Value);

        if (_rememberEntry.Value)
        {
            string saved = RememberedLanguage.Load();
            if (saved != null)
            {
                int savedIndex = Languages.FindIndex(l => string.Equals(l.Name, saved, StringComparison.OrdinalIgnoreCase));
                if (savedIndex >= 0)
                {
                    _index = savedIndex;
                    _pendingRestore = (int)Languages[savedIndex].Localization;
                }
            }
        }

        _log.LogInfo("InFalsusAdvVN " + Version + " loaded. Hotkey = " + _hotkey +
                     " (story mode only), languages = " + string.Join(", ", Languages.ConvertAll(l => l.Name)));

        ResolveBindings();
        StoryLocalizationPatch.Install(_controller, msg => _log.LogInfo(msg));
        WheelListener.SetLog(msg => _log.LogInfo(msg));
        InjectTickBehaviour();

        if (_pendingRestore != 0)
        {
            StoryLocalization = _pendingRestore;
            _log.LogInfo("Restored remembered story language: " + DescribeLocalization(_pendingRestore));
            _pendingRestore = 0;
        }

        _log.LogInfo("Game localization currently = " + DescribeLocalization(ProbeLocalization()) +
                     " (the mod never changes it; only story text is overridden).");
    }

    private static void InjectTickBehaviour()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<HotkeyBehaviour>();

            GameObject host = new GameObject("InFalsusAdvVN.Hotkey");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<HotkeyBehaviour>();

            _log.LogInfo("Hotkey behaviour injected.");
        }
        catch (Exception e)
        {
            _log.LogError("Could not inject the hotkey behaviour: " + e);
            _faulted = true;
        }
    }

    private static void ResolveBindings()
    {
        try
        {
            _bindings = GameBindings.Resolve(msg => _log.LogInfo(msg), _verboseEntry.Value);
            _controller = new StoryTextController(_bindings, _visibilityEntry.Value,
                msg => _log.LogInfo(msg),
                msg => { if (_verboseEntry.Value) _log.LogInfo("[debug] " + msg); });

            if (!_bindings.Ready)
            {
                _faulted = true;
                _log.LogError("InFalsusAdvVN could not bind to the game's localization API; hotkey disabled.");
                return;
            }

            _log.LogInfo("InFalsusAdvVN bindings resolved.");
        }
        catch (Exception e)
        {
            _faulted = true;
            _log.LogError("Binding resolution threw: " + e);
        }
    }


    internal static void ReportPatchError(Exception e)
    {
        _faulted = true;
        _log?.LogError("Story patch failed and is now disabled: " + e);
    }

    // ---------------------------------------------------------------- per-frame tick

    internal static void OnFrame()
    {
        if (_faulted || _controller == null)
            return;

        try
        {
            _frame++;

            if (_frame % 30 == 0)
                TrackGameLanguage();



            if (_enforceFrames > 0)
            {
                _enforceFrames--;
                if (_activeScene != null && StoryLocalization != 0)
                    _controller.PrepareRendererSet(IL2CPP.Il2CppObjectBaseToPtr(_activeScene), StoryLocalization, false);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            KeyControl control = keyboard[_hotkey];
            bool pressed = control != null && control.isPressed;

            if (pressed && !_hotkeyHeld)
            {
                _hotkeyHeld = true;
                HandleHotkey();
                return;
            }

            if (!pressed)
                _hotkeyHeld = false;


        }
        catch (Exception e)
        {
            _faulted = true;
            _log.LogError("Hotkey handling failed and is now disabled: " + e);
        }
    }

    /// <summary>
    /// Called by the StoryScene._jp postfix. This only records that a new line is on screen; the
    /// actual rewrite runs on the next main-loop tick so no native call happens inside the game's
    /// own event dispatch.
    /// </summary>
    /// <summary>
    /// Runs after every Update() in the frame and before rendering. The engine writes its own text
    /// during Update (StoryScene._jp), so rewriting here both wins the ordering and never shows the
    /// intermediate text.
    /// </summary>
    internal static void OnLateFrame()
    {
        if (_faulted || _controller == null)
            return;

        try
        {
            PollMouseWheel();
        }
        catch (Exception e)
        {
            _log.LogError("Late wheel poll failed: " + e);
        }
    }
    internal static void OnStoryEvent(StoryScene scene)
    {
        if (_faulted || _controller == null || scene == null)
            return;

        IntPtr scenePtr = IL2CPP.Il2CppObjectBaseToPtr(scene);
        _activeScene = scene;

        // New lines are resolved by the game itself in the overridden language (StoryLocalizationPatch),
        // so nothing has to be rewritten here.
        IntPtr id = _controller.ReadCurrentStoryStringIdPointer(scenePtr);
        if (id != IntPtr.Zero)
            _lastStoryIdPointer = id;
    }

    private static void HandleHotkey()
    {
        // Only listened for inside a story scene.
        StoryScene scene = FindStoryScene();
        if (scene == null)
            return;

        _activeScene = scene;
        IntPtr scenePtr = IL2CPP.Il2CppObjectBaseToPtr(scene);

        if (_index < 0)
            SyncIndexWithGame();

        _index = (_index + 1) % Languages.Count;
        LanguageOption target = Languages[_index];
        int selected = (int)target.Localization;

        StoryLocalization = selected;
        _enforceFrames = RendererEnforceFrames;
        _log.LogInfo("Story language -> " + target.Name);

        if (_rememberEntry.Value)
            RememberedLanguage.Save(target.Name);

        if (_verboseEntry.Value)
            _log.LogInfo("[debug] " + _controller.DescribeScene(scenePtr));

        RefreshCurrentLine(scenePtr, selected, "hotkey");

        if (_controller.RefreshSpeakerName(scenePtr, selected))
            _log.LogInfo("Speaker name refreshed.");

        _controller.RefreshOpenLog(scenePtr);
    }


    private static void RefreshCurrentLine(IntPtr scenePtr, int localization, string reason)
    {
        _refreshing = true;
        try
        {
            string storyStringId = _controller.ReadCurrentStoryStringId(scenePtr);
            if (string.IsNullOrEmpty(storyStringId))
            {
                if (reason == "hotkey")
                    _log.LogInfo("No story line is registered on screen yet; the change applies from the next line.");
                return;
            }

            _log.LogInfo("Refreshing line '" + storyStringId + "' (" + reason + ") -> " + DescribeLocalization(localization) + "...");

            string text = _controller.LookupTranslation(storyStringId, localization);
            if (string.IsNullOrEmpty(text))
            {
                _log.LogWarning("No " + DescribeLocalization(localization) + " text for this line in the game data.");
                return;
            }

            if (_verboseEntry.Value)
                _log.LogInfo("[debug] resolved " + text.Length + " chars: " + text);

            bool applied = _engineTextEntry.Value
                ? _controller.Apply(scenePtr, storyStringId, text, localization, _controller.GameLocalization)
                : _controller.ReapplyVisible(scenePtr, text, localization);
            _log.LogInfo(applied ? "On-screen text refreshed." : "Text resolved but nothing was written to the screen (see log).");
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>If the player changes the language in the game settings, drop the mod's override.</summary>
    private static void TrackGameLanguage()
    {
        int current = _controller.GameLocalization;
        if (current == 0)
            return;

        if (_lastGameLocalization < 0)
        {
            _lastGameLocalization = current;
            return;
        }

        if (current == _lastGameLocalization)
            return;

        _lastGameLocalization = current;
        if (StoryLocalization != 0)
        {
            StoryLocalization = 0;
            _log.LogInfo("Game language changed to " + DescribeLocalization(current) +
                         "; story text follows the game setting again.");
        }
    }

    /// <summary>
    /// Wheel down advances the line, wheel up opens the LOG history. Both reuse the game's own input
    /// routines: StoryScene._dp (the input trigger the scene calls from its keyboard/click path) and
    /// StoryScene._Ep + ChatLogContainer._aS (what the LOG button handler does).
    /// </summary>
    /// <summary>
    /// Wheel input. The legacy backend is tried first because this build processes the Input System
    /// manually (device scroll reads are already reset by the time a MonoBehaviour Update runs); if the
    /// legacy backend is inactive the Input System devices are sampled instead.
    /// </summary>
    private static void PollMouseWheel()
    {
        if (_faulted || _controller == null)
            return;

        // The game installs its own Input System listener during startup, so ours is attached as soon
        // as the story UI is up (it then survives for the rest of the session).
        if (!WheelListener.Installed && _frame % 60 == 0)
        {
            if (FindStoryScene() != null)
                WheelListener.InstallLate();
        }
        if (!_wheelAdvanceEntry.Value && !_wheelOpenLogEntry.Value)
            return;

        float delta = ReadWheelDelta(out int devices, out string report);
        if (delta == 0f)
        {
            _wheelLatched = false;
            return;
        }

        if (_wheelLatched)
            return;

        _wheelLatched = true;

        if (_wheelReportCount < 25)
        {
            _wheelReportCount++;
            _log.LogInfo("[debug] wheel delta=" + delta + " devices=" + devices + " " + report);
        }

        StoryScene scene = FindStoryScene();
        if (scene == null)
            return;

        IntPtr scenePtr = IL2CPP.Il2CppObjectBaseToPtr(scene);
        if (_controller.IsLogOpen(scenePtr))
            return;

        if (delta < 0f && _wheelAdvanceEntry.Value)
        {
            if (_controller.AdvanceStory(scenePtr))
                _log.LogInfo("Wheel down -> next line.");
        }
        else if (delta > 0f && _wheelOpenLogEntry.Value)
        {
            if (_controller.OpenLog(scenePtr))
                _log.LogInfo("Wheel up -> LOG panel.");
        }
    }

    private static float ReadWheelDelta(out int devices, out string report)
    {
        devices = 0;
        report = "";

        float captured = WheelListener.Consume();
        if (captured != 0f)
        {
            report = "[event=" + captured + "]";
            return captured;
        }

        try
        {
            float legacy = UnityEngine.Input.mouseScrollDelta.y;
            if (legacy != 0f)
            {
                report = "[legacy=" + legacy + "]";
                return legacy;
            }
        }
        catch
        {
            // legacy backend not active
        }

        float result = 0f;
        try
        {
            var all = Mouse.all;
            devices = all.Count;
            for (int i = 0; i < all.Count; i++)
            {
                var device = all[i];
                if (device == null)
                    continue;

                var mouse = device.TryCast<Mouse>();
                if (mouse == null || mouse.scroll == null)
                    continue;

                float value = mouse.scroll.ReadValue().y;
                if (report.Length < 200)
                    report += "[" + i + ":" + mouse.name + "=" + value + "]";
                if (value != 0f && result == 0f)
                    result = value;
            }
        }
        catch
        {
            // Input System unavailable
        }

        return result;
    }
    private static StoryScene FindStoryScene()
    {
        try
        {
            UnityEngine.Object found = UnityEngine.Object.FindObjectOfType(Il2CppType.Of<StoryScene>());
            if (found == null)
                return null;
            return found.TryCast<StoryScene>();
        }
        catch
        {
            return null;
        }
    }

    private static void SyncIndexWithGame()
    {
        int current = _controller.GameLocalization;
        int match = Languages.FindIndex(l => (int)l.Localization == current);
        _index = match >= 0 ? match : -1;
    }

    private static int ProbeLocalization()
    {
        try
        {
            IntPtr klass = Il2.FindClass("Str", "Strings");
            if (klass == IntPtr.Zero)
                return 0;
            IntPtr field = Il2.Field(klass, "targetLocalization");
            return field == IntPtr.Zero ? 0 : GameBindings.CurrentLocalization(field);
        }
        catch
        {
            return 0;
        }
    }

    private static string DescribeLocalization(int value)
    {
        switch (value)
        {
            case 0: return "Unset";
            case 1: return "English";
            case 2: return "Japanese";
            case 3: return "Korean";
            case 4: return "Traditional Chinese";
            case 5: return "Simplified Chinese";
            default: return "Unknown(" + value + ")";
        }
    }

    // ---------------------------------------------------------------- config parsing

    private static void ParseHotkey(string value)
    {
        Key parsed;
        if (Enum.TryParse(value, true, out parsed))
        {
            _hotkey = parsed;
        }
        else
        {
            _hotkey = Key.L;
            _log.LogWarning("Unknown hotkey '" + value + "'; falling back to L.");
        }
    }

    private static void ParseLanguages(string value)
    {
        Languages.Clear();
        foreach (string raw in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            LanguageOption option = LanguageOption.Parse(raw);
            if (option == null)
            {
                _log.LogWarning("Ignoring unknown language '" + raw + "'.");
                continue;
            }

            if (!Languages.Exists(l => l.Localization == option.Localization))
                Languages.Add(option);
        }

        if (Languages.Count == 0)
        {
            Languages.Add(LanguageOption.English);
            Languages.Add(LanguageOption.ChineseSC);
            _log.LogWarning("LanguageOrder was empty; defaulting to English, ChineseSC.");
        }
    }

    internal sealed class LanguageOption
    {
        public static readonly LanguageOption English = new LanguageOption("English", Str.Strings.Localization.English);
        public static readonly LanguageOption Japanese = new LanguageOption("Japanese", Str.Strings.Localization.Japanese);
        public static readonly LanguageOption Korean = new LanguageOption("Korean", Str.Strings.Localization.Korean);
        public static readonly LanguageOption ChineseSC = new LanguageOption("ChineseSC", Str.Strings.Localization.ChineseSC);
        public static readonly LanguageOption ChineseTC = new LanguageOption("ChineseTC", Str.Strings.Localization.ChineseTC);

        public LanguageOption(string name, Str.Strings.Localization localization)
        {
            Name = name;
            Localization = localization;
        }

        public string Name { get; private set; }

        public Str.Strings.Localization Localization { get; private set; }

        public static LanguageOption Parse(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "en":
                case "eng":
                case "english":
                    return English;
                case "jp":
                case "ja":
                case "jpn":
                case "japanese":
                    return Japanese;
                case "ko":
                case "kr":
                case "korean":
                    return Korean;
                case "zh-hans":
                case "zh_hans":
                case "zhsc":
                case "zh-cn":
                case "sc":
                case "simplified":
                case "chinesesc":
                case "chinesesimplified":
                    return ChineseSC;
                case "zh-hant":
                case "zh_hant":
                case "zhtc":
                case "zh-tw":
                case "tc":
                case "traditional":
                case "chinesetc":
                case "chinesetraditional":
                    return ChineseTC;
                default:
                    return null;
            }
        }
    }
}

/// <summary>Injected MonoBehaviour that provides a per-frame tick for the hotkey and renderer sync.</summary>
public sealed class HotkeyBehaviour : MonoBehaviour
{
    public HotkeyBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Update() => Plugin.OnFrame();

    private void LateUpdate() => Plugin.OnLateFrame();

}

/// <summary>Remembers the last language chosen with the hotkey between game sessions.</summary>
internal static class RememberedLanguage
{
    private static string Path
    {
        get
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            return System.IO.Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "InFalsusAdvVN.last");
        }
    }

    public static void Save(string name)
    {
        try
        {
            System.IO.File.WriteAllText(Path, name);
        }
        catch
        {
            // non critical
        }
    }

    public static string Load()
    {
        try
        {
            return System.IO.File.Exists(Path) ? System.IO.File.ReadAllText(Path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}