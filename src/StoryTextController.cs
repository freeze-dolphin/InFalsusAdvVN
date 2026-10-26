using System;
using System.Runtime.InteropServices;
using System.Text;
using ifapp.Game.Data.Story;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace InFalsusAdvVN;

/// <summary>
/// Story-text language override for the visual novel.
///
/// Design rule learned the hard way on this build: never invoke a native method whose argument or
/// return value is a structure. The whole class therefore only
///   * reads StoryTranslationDetails.Translations (List&lt;StringMapping&gt; of plain strings) - pure memory,
///   * rewrites the string pointer inside the existing Str.DynamicString held by
///     StoryState.TextBoxState.CurrentText (reusing the engine's own value, only swapping the text),
///   * calls FastText.SetTextNonLocalized(string, UpdateType) for the visible renderers.
/// Str.Strings.targetLocalization is never written, so menus and settings keep their language.
/// </summary>
internal sealed class StoryTextController
{
    private const int UpdateTypeDefault = 0;      // FastText.UpdateType.Default
    private const int UpdateTypeDontUpdateMesh = 1;
    private const int EnglishLocalization = 1;

    private readonly GameBindings _bindings;
    private readonly bool _manageVisibility;
    private readonly Action<string> _log;
    private readonly Action<string> _debug;

    private IntPtr _provider;
    private uint _providerHandle;
    private bool _providerReported;

    public StoryTextController(GameBindings bindings, bool manageVisibility, Action<string> log, Action<string> debug)
    {
        _bindings = bindings;
        _manageVisibility = manageVisibility;
        _log = log;
        _debug = debug;
    }

    /// <summary>The game's own language setting (never modified by the mod).</summary>
    /// <summary>Str.Strings.targetLocalization (static field handle).</summary>
    public IntPtr LocalizationField => _bindings.TargetLocalizationField;

    public int GameLocalization => GameBindings.CurrentLocalization(_bindings.TargetLocalizationField);

    // ---------------------------------------------------------------- translation table

    private StoryTranslationDetails _details;
    private Il2CppSystem.Collections.Generic.List<StoryTranslationDetails.StringMapping> _translations;
    private System.Collections.Generic.Dictionary<string, int> _idToIndex;

    /// <summary>
    /// Builds an id -> row index from StoryTranslationDetails.Translations. The list is read through
    /// the Il2CppInterop type so the element layout is handled by generated code instead of guesswork.
    /// </summary>
    private bool EnsureIndex()
    {
        if (_idToIndex != null)
            return true;

        try
        {
            Il2CppReferenceArray<UnityEngine.Object> all =
                Resources.FindObjectsOfTypeAll(Il2CppType.Of<StoryTranslationDetails>());
            if (all == null)
                return false;

            for (int i = 0; i < all.Length; i++)
            {
                UnityEngine.Object candidate = all[i];
                if (candidate == null)
                    continue;

                StoryTranslationDetails details = candidate.TryCast<StoryTranslationDetails>();
                if (details == null)
                    continue;

                var rows = details.Translations;
                if (rows == null)
                    continue;

                var map = new System.Collections.Generic.Dictionary<string, int>(rows.Count, StringComparer.Ordinal);
                for (int r = 0; r < rows.Count; r++)
                {
                    string id = rows[r].Id;
                    if (!string.IsNullOrEmpty(id))
                        map[id] = r;
                }

                _details = details;
                _translations = rows;
                _idToIndex = map;
                _log("Translation table indexed: " + map.Count + " story strings.");
                return true;
            }
        }
        catch (Exception e)
        {
            _log("Translation table indexing failed: " + e.Message);
        }

        return false;
    }

    /// <summary>Returns the line in the requested language (English is used as the game's fallback).</summary>
    public string LookupTranslation(string storyStringId, int localization)
    {
        if (string.IsNullOrEmpty(storyStringId) || !EnsureIndex())
            return null;

        int index;
        if (!_idToIndex.TryGetValue(storyStringId, out index))
        {
            _debug($"'{storyStringId}' is not in the translation table");
            return null;
        }

        try
        {
            StoryTranslationDetails.StringMapping row = _translations[index];
            string english = row.English;
            switch (localization)
            {
                case 2: return FirstNonEmpty(row.Japanese, english);
                case 4: return FirstNonEmpty(row.ChineseTC, english);
                case 5: return FirstNonEmpty(row.ChineseSC, english);
                default: return english;
            }
        }
        catch (Exception e)
        {
            _debug("reading translation row failed: " + e.Message);
            return null;
        }
    }

    private static IntPtr _stringClass;

    private string _lastWrittenLine;

    private static bool IsManagedString(IntPtr candidate)
    {
        if (candidate == IntPtr.Zero)
            return false;
        if (_stringClass == IntPtr.Zero)
            _stringClass = Il2.FindClass("System", "String");
        return _stringClass != IntPtr.Zero && Il2.ObjectClass(candidate) == _stringClass;
    }

    private static bool StringEquals(IntPtr il2cppString, string expected)
    {
        if (il2cppString == IntPtr.Zero || !IsManagedString(il2cppString))
            return false;

        int length = Il2.StringLength(il2cppString);
        if (length < 0 || length > 8192)
            return false;
        if (length != (expected?.Length ?? 0))
            return false;

        IntPtr chars = Il2.StringChars(il2cppString);
        for (int i = 0; i < length; i++)
        {
            if (Marshal.ReadInt16(chars, i * 2) != expected[i])
                return false;
        }

        return true;
    }
    private static string FirstNonEmpty(string value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;
    // ---------------------------------------------------------------- renderer set

    private int RendererSetOffsetFor(int localization)
    {
        switch (localization)
        {
            case 2: return _bindings.JapaneseTextBoxesOffset;
            case 4: return _bindings.ChineseTextBoxesOffset;
            case 5: return _bindings.ChineseTextBoxesOffset;
            default: return _bindings.EnglishTextBoxesOffset;
        }
    }

    /// <summary>Points StoryScene._xS at the renderer set for the requested language and enables it.</summary>
    public bool PrepareRendererSet(IntPtr scene, int localization, bool hideOthers)
    {
        if (scene == IntPtr.Zero || localization == 0)
            return false;

        IntPtr wanted = GameBindings.ReadPtr(scene, RendererSetOffsetFor(localization));
        if (wanted == IntPtr.Zero)
            return false;

        bool changed = false;
        IntPtr current = GameBindings.ReadPtr(scene, _bindings.SceneTextBoxesOffset);
        if (current != IntPtr.Zero && current != wanted)
        {
            // The renderer sets are separate text components; leaving the old set visible makes the
            // previous line stay on screen behind the new one.
            DeactivateSet(current);
            GameBindings.WritePtr(scene, _bindings.SceneTextBoxesOffset, wanted);
            changed = true;
        }

        ActivateSet(wanted);

        if (hideOthers)
            SyncVisibility(scene, wanted);

        return changed;
    }

    private static FastText.FastText Wrap(IntPtr pointer) =>
        pointer == IntPtr.Zero ? null : new FastText.FastText(pointer);

    private void ActivateSet(IntPtr array)
    {
        int n = GameBindings.ArrayLength(array);
        for (int i = 0; i < n; i++)
        {
            FastText.FastText renderer = Wrap(GameBindings.ArrayElement(array, i));
            if (renderer == null)
                continue;

            UnityEngine.GameObject go = renderer.gameObject;
            if (go != null && !go.activeSelf)
                go.SetActive(true);
        }
    }

    private void DeactivateSet(IntPtr array)
    {
        int n = GameBindings.ArrayLength(array);
        for (int i = 0; i < n; i++)
        {
            FastText.FastText renderer = Wrap(GameBindings.ArrayElement(array, i));
            if (renderer == null)
                continue;

            UnityEngine.GameObject go = renderer.gameObject;
            if (go != null && go.activeSelf)
                go.SetActive(false);
        }
    }
    private static bool LooksLikeText(IntPtr il2cppString)
    {
        try
        {
            int length = Il2.StringLength(il2cppString);
            return length > 0 && length < 4096;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the line exactly the way StoryScene does it: build a Str.DynamicString and hand it to
    /// FastText.SetText(DynamicString, UpdateType). Strings.targetLocalization is switched to the
    /// requested language only for the duration of the call, because FastText resolves its font
    /// family and line-breaking rules from it (SetTextNonLocalized instead hardcodes Localization 2,
    /// which is why it rendered with the Japanese metrics).
    /// </summary>
    private bool SetRendererText(IntPtr array, string text, int localization)
    {
        int count = GameBindings.ArrayLength(array);
        if (count <= 0)
        {
            _debug("renderer set is empty");
            return false;
        }

        int previous = GameBindings.CurrentLocalization(_bindings.TargetLocalizationField);
        int updated = 0;
        try
        {
            GameBindings.SetLocalization(_bindings.TargetLocalizationField, localization);

            var line = new Str.DynamicString(text);

            for (int i = 0; i < count; i++)
            {
                FastText.FastText renderer = Wrap(GameBindings.ArrayElement(array, i));
                if (renderer == null)
                    continue;

                try
                {
                    _debug($"FastText[{i}]: SetText(DynamicString, {text.Length} chars, loc={localization})");
                    renderer.SetText(line, FastText.FastText.UpdateType.Default);

                    int visible = renderer.MaxVisibleCharacters;
                    if (visible < text.Length)
                    {
                        _debug($"FastText[{i}]: SetMaxVisibleCharacters({visible} -> {text.Length})");
                        renderer.SetMaxVisibleCharacters(text.Length, FastText.FastText.UpdateType.Default);
                    }

                    updated++;
                }
                catch (Exception e)
                {
                    _debug($"FastText[{i}] update threw: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            _debug("SetText(DynamicString) setup threw: " + e.Message);
        }
        finally
        {
            GameBindings.SetLocalization(_bindings.TargetLocalizationField, previous);
        }

        _debug($"renderer text updated: {updated}/{count}");
        return updated > 0;
    }
    /// <summary>Re-writes only the visible text (used to stay ahead of the typewriter).</summary>
    public bool ReapplyVisible(IntPtr scene, string text, int localization)
    {
        if (scene == IntPtr.Zero || string.IsNullOrEmpty(text))
            return false;

        IntPtr target = GameBindings.ReadPtr(scene, RendererSetOffsetFor(localization));
        if (target == IntPtr.Zero)
            return false;

        return SetRendererText(target, text, localization);
    }

    /// <summary>Hides the renderer sets that do not belong to the active language.</summary>
    private void SyncVisibility(IntPtr scene, IntPtr activeArray)
    {
        if (_bindings.SetActiveMethod == IntPtr.Zero || _bindings.GetGameObjectMethod == IntPtr.Zero)
            return;

        var active = new System.Collections.Generic.HashSet<IntPtr>();
        int count = GameBindings.ArrayLength(activeArray);
        for (int i = 0; i < count; i++)
        {
            IntPtr element = GameBindings.ArrayElement(activeArray, i);
            if (element != IntPtr.Zero)
                active.Add(element);
        }

        int activated = 0;
        int deactivated = 0;
        foreach (int offset in new[]
                 {
                     _bindings.EnglishTextBoxesOffset,
                     _bindings.JapaneseTextBoxesOffset,
                     _bindings.ChineseTextBoxesOffset,
                 })
        {
            IntPtr array = GameBindings.ReadPtr(scene, offset);
            int n = GameBindings.ArrayLength(array);
            for (int i = 0; i < n; i++)
            {
                IntPtr element = GameBindings.ArrayElement(array, i);
                if (element == IntPtr.Zero)
                    continue;

                bool shouldBeActive = active.Contains(element);
                IntPtr gameObject = Il2.Invoke(_bindings.GetGameObjectMethod, element, null, out _);
                if (gameObject == IntPtr.Zero)
                    continue;

                IntPtr value = Il2.Alloc(4);
                try
                {
                    Marshal.WriteInt32(value, shouldBeActive ? 1 : 0);
                    Il2.Invoke(_bindings.SetActiveMethod, gameObject, new[] { value }, out _);
                }
                finally
                {
                    Marshal.FreeHGlobal(value);
                }

                if (shouldBeActive) activated++;
                else deactivated++;
            }
        }

        _debug($"renderer visibility: {activated} active, {deactivated} hidden");
    }

    // ---------------------------------------------------------------- speaker name

    /// <summary>
    /// The speaker name is resolved once per line and delivered through
    /// StoryCharacterNameChangedEvent, so the line already on screen keeps the old language until the
    /// next one. The name itself comes from DynamicStringMapping (DynamicStringTypeFlags.StoryName,
    /// keyed by RawStoryName), so it can be re-resolved here and pushed back into the name plate.
    /// </summary>
    private ifapp.Game.Data.DynamicStringMapping _nameMapping;

    private ifapp.Game.Data.DynamicStringMapping FindNameMapping()
    {
        if (_nameMapping != null)
            return _nameMapping;

        try
        {
            Il2CppReferenceArray<UnityEngine.Object> all =
                Resources.FindObjectsOfTypeAll(Il2CppType.Of<ifapp.Game.Data.DynamicStringMapping>());
            if (all == null)
                return null;

            for (int i = 0; i < all.Length; i++)
            {
                UnityEngine.Object candidate = all[i];
                if (candidate == null)
                    continue;

                var mapping = candidate.TryCast<ifapp.Game.Data.DynamicStringMapping>();
                if (mapping == null)
                    continue;

                _nameMapping = mapping;
                _log("DynamicStringMapping found (" + all.Length + " candidate object(s)).");
                return _nameMapping;
            }
        }
        catch (Exception e)
        {
            _debug("DynamicStringMapping lookup threw: " + e.Message);
        }

        return null;
    }

    /// <summary>
    /// The speaker name is resolved once per line (delivered through StoryCharacterNameChangedEvent),
    /// so the name plate keeps the old language until the next line. Names live in
    /// DynamicStringMapping under DynamicStringTypeFlags.StoryName, keyed by RawStoryName; re-resolve
    /// the currently displayed name and push it back into the name plate.
    /// </summary>
    private string _rawSpeakerName;   // key that actually resolved, kept across language switches
    private string _lastSpeakerText;  // text we last wrote into the name plate

    private const string MissingMapping = "Missing String Mapping";

    public bool RefreshSpeakerName(IntPtr scene, int localization)
    {
        try
        {
            if (scene == IntPtr.Zero || localization == 0)
                return false;
            if (_bindings.SceneNameContainerOffset <= 0 || _bindings.CharacterNameOffset <= 0)
                return false;

            IntPtr state = GameBindings.ReadPtr(scene, _bindings.SceneStateOffset);
            IntPtr textBox = state == IntPtr.Zero ? IntPtr.Zero : GameBindings.ReadPtr(state, _bindings.StateTextBoxOffset);
            IntPtr containerPtr = GameBindings.ReadPtr(scene, _bindings.SceneNameContainerOffset);
            if (textBox == IntPtr.Zero || containerPtr == IntPtr.Zero)
                return false;

            IntPtr namePtr = GameBindings.ReadPtr(textBox, _bindings.CharacterNameOffset);
            string displayed = namePtr == IntPtr.Zero ? null : Il2.ToManaged(namePtr);
            if (string.IsNullOrEmpty(displayed))
                return false;

            // The engine writes the name in the current language on every new line. If the text is
            // still what this mod wrote, the speaker has not changed and the original key is reused;
            // otherwise the engine just set a fresh name and that text becomes the new key.
            string key = displayed;
            if (_rawSpeakerName != null && _lastSpeakerText != null && displayed == _lastSpeakerText)
                key = _rawSpeakerName;

            if (FindNameMapping() == null)
            {
                _debug("DynamicStringMapping is not loaded; speaker name left as is");
                return false;
            }

            int previous = GameLocalization;
            GameBindings.SetLocalization(_bindings.TargetLocalizationField, localization);
            try
            {
                string text = ResolveName(key);

                // If the stored text is not a usable key, retry with the text on screen.
                if (IsUnusable(text) && key != displayed)
                    text = ResolveName(displayed);

                if (IsUnusable(text))
                {
                    _debug($"speaker name key '{key}' did not resolve; name plate left unchanged");
                    return false;
                }

                Str.DynamicString resolved = _nameMapping.Get(
                    Str.DynamicStringTypeFlags.StoryName,
                    (Str.Strings.Localization)localization,
                    new ifapp.Game.Data.RawStoryName(key));

                var container = new ifapp.Game.Scenes.StoryNameContainer(containerPtr);
                container._yO(resolved);

                var boxState = new ifapp.Game.NovelEngine.StoryTextBoxState(textBox);
                boxState.CharacterName = text;

                _rawSpeakerName = key;
                _lastSpeakerText = text;
                _log($"Speaker name '{key}' -> '{text}'.");
                return true;
            }
            finally
            {
                GameBindings.SetLocalization(_bindings.TargetLocalizationField, previous);
            }
        }
        catch (Exception e)
        {
            _debug("speaker name refresh threw: " + e.Message);
            return false;
        }
    }

    private string ResolveName(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        Str.DynamicString resolved = _nameMapping.Get(
            Str.DynamicStringTypeFlags.StoryName,
            (Str.Strings.Localization)GameLocalization,
            new ifapp.Game.Data.RawStoryName(key));

        return resolved == null ? null : resolved.CreateString();
    }

    private static bool IsUnusable(string text) =>
        string.IsNullOrEmpty(text) || text == MissingMapping;
    // ---------------------------------------------------------------- mouse wheel actions

    /// <summary>
    /// Advances the story exactly like a left click: StoryScene._dp() is the routine the scene's own
    /// keyboard/click path calls (StoryScene._OvA -> _dp), and it raises the story input trigger that
    /// StoryCommandProcessor consumes to complete the waiting line.
    /// </summary>
    public bool AdvanceStory(IntPtr scene)
    {
        try
        {
            if (scene == IntPtr.Zero)
                return false;

            var storyScene = new ifapp.Game.Scenes.StoryScene(scene);
            storyScene._dp();
            return true;
        }
        catch (Exception e)
        {
            _debug("advance threw: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Opens the LOG panel the same way its button does: StoryScene._wp (the LOG button handler) just
    /// calls StoryScene._Ep, which fills the panel from StoryScene._GS through ChatLogContainer._aS.
    /// The container has to be active before it is filled, so it is enabled first when closed.
    /// </summary>
    public bool OpenLog(IntPtr scene)
    {
        try
        {
            if (scene == IntPtr.Zero || _bindings.SceneChatLogOffset <= 0)
                return false;

            IntPtr containerPtr = GameBindings.ReadPtr(scene, _bindings.SceneChatLogOffset);
            if (containerPtr == IntPtr.Zero)
                return false;

            var container = new ifapp.Game.Scenes.Story.ChatLogContainer(containerPtr);
            UnityEngine.GameObject host = container.gameObject;
            if (host == null)
                return false;

            if (host.activeSelf)
                return false;   // already open

            host.SetActive(true);

            var storyScene = new ifapp.Game.Scenes.StoryScene(scene);
            storyScene._Ep();

            _log("LOG panel opened from the mouse wheel.");
            return true;
        }
        catch (Exception e)
        {
            _debug("opening the LOG panel threw: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Closes the LOG panel the way its own quit button does: the container fades out and the tween
    /// completion callback (ChatLogContainer._cS) deactivates it, then StoryScene.OnQuitCallback runs
    /// the scene-side cleanup. Calling SetActive(false) directly instead leaves the panel unusable.
    /// </summary>
    public bool CloseLog(IntPtr scene)
    {
        try
        {
            if (scene == IntPtr.Zero || _bindings.SceneChatLogOffset <= 0)
                return false;

            IntPtr containerPtr = GameBindings.ReadPtr(scene, _bindings.SceneChatLogOffset);
            if (containerPtr == IntPtr.Zero)
                return false;

            var container = new ifapp.Game.Scenes.Story.ChatLogContainer(containerPtr);
            UnityEngine.GameObject host = container.gameObject;
            if (host == null || !host.activeInHierarchy)
                return false;

            // ChatLogContainer._bS starts the fade-out tween; its completion callback (_cS)
            // deactivates the panel and runs OnQuitCallback (the scene-side restore).
            container._bS();
            _log("LOG panel closing (fade out via ChatLogContainer._bS).");
            return true;
        }
        catch (Exception e)
        {
            _debug("closing the LOG panel threw: " + e.Message);
            return false;
        }
    }

    /// <summary>True when the LOG panel currently holds the screen.</summary>
    public bool IsLogOpen(IntPtr scene)
    {
        try
        {
            if (scene == IntPtr.Zero || _bindings.SceneChatLogOffset <= 0)
                return false;

            IntPtr containerPtr = GameBindings.ReadPtr(scene, _bindings.SceneChatLogOffset);
            if (containerPtr == IntPtr.Zero)
                return false;

            var container = new ifapp.Game.Scenes.Story.ChatLogContainer(containerPtr);
            UnityEngine.GameObject host = container.gameObject;
            return host != null && host.activeInHierarchy && container.enabled;
        }
        catch
        {
            return false;
        }
    }
    // ---------------------------------------------------------------- LOG panel

    /// <summary>
    /// The LOG panel is ChatLogContainer: when it is opened it rebuilds the history from the command
    /// list with ChatLogContainer._aS and resolves every line again (that call is bracketed by
    /// StoryLocalizationPatch, so it uses the selected language). If the panel is already open when
    /// the language changes, toggling the component rebuilds it through its own OnDisable/OnEnable.
    /// </summary>
    /// <summary>
    /// Re-translates the LOG panel in place. Every entry (ChatLogEntryContainer) keeps the story
    /// string id in debug_uniqueId, so the visible history can be re-resolved and rewritten without
    /// rebuilding the list (rebuilding through the container's lifecycle leaves the panel empty until
    /// the game reopens it).
    /// </summary>
    private System.Collections.Generic.Dictionary<string, string> _textToId;

    /// <summary>
    /// Reverse index (localised text -> story string id) built from the translation table. LOG entries
    /// do not keep their StoryStringId, so the id of an existing entry is recovered from its text.
    /// </summary>
    private void EnsureReverseIndex()
    {
        if (_textToId != null || !EnsureIndex())
            return;

        var map = new System.Collections.Generic.Dictionary<string, string>(_translations.Count * 2, StringComparer.Ordinal);
        for (int i = 0; i < _translations.Count; i++)
        {
            string id = _translations[i].Id;
            if (string.IsNullOrEmpty(id))
                continue;

            StoryTranslationDetails.StringMapping row = _translations[i];
            AddToReverse(map, row.English, id);
            AddToReverse(map, row.Japanese, id);
            AddToReverse(map, row.ChineseTC, id);
            AddToReverse(map, row.ChineseSC, id);
        }

        _textToId = map;
        _log("Reverse text index built: " + map.Count + " strings.");
    }

    private static void AddToReverse(System.Collections.Generic.Dictionary<string, string> map, string text, string id)
    {
        if (!string.IsNullOrEmpty(text) && !map.ContainsKey(text))
            map[text] = id;
    }

    /// <summary>
    /// Re-translates the LOG panel in place. The container itself is left alone (rebuilding it through
    /// SetActive breaks the panel), only the FastText components of the existing entries are rewritten.
    /// </summary>
    public bool RefreshOpenLog(IntPtr scene)
    {
        try
        {
            if (scene == IntPtr.Zero || _bindings.SceneChatLogOffset <= 0 || _bindings.ChatLogEntryClass == IntPtr.Zero)
                return false;

            int localization = Plugin.StoryLocalization;
            if (localization == 0)
                return false;

            IntPtr containerPtr = GameBindings.ReadPtr(scene, _bindings.SceneChatLogOffset);
            if (containerPtr == IntPtr.Zero)
                return false;

            _debug("log: container found");
            var container = new ifapp.Game.Scenes.Story.ChatLogContainer(containerPtr);
            UnityEngine.GameObject host = container.gameObject;
            if (host == null || !host.activeSelf)
                return false;   // closed: it will be built in the selected language when opened

            _debug("log: panel is open");
            EnsureReverseIndex();
            if (_textToId == null)
                return false;

            _debug("log: collecting entries");
            Il2CppReferenceArray<UnityEngine.Component> entries =
                container.GetComponentsInChildren(Il2CppType.Of<ifapp.Game.Scenes.Story.ChatLogEntryContainer>(), false);
            if (entries == null || entries.Length == 0)
                return false;

            _debug("log: rewriting " + entries.Length + " entries");

            int updated = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                UnityEngine.Component component = entries[i];
                if (component == null)
                    continue;

                IntPtr entryPtr = IL2CPP.Il2CppObjectBaseToPtr(component);
                if (entryPtr == IntPtr.Zero)
                    continue;

                if (RetranslateEntry(entryPtr, localization))
                    updated++;
            }

            _debug($"LOG entries refreshed: {updated}/{entries.Length}");
            return updated > 0;
        }
        catch (Exception e)
        {
            _debug("LOG panel refresh threw: " + e.Message);
            return false;
        }
    }

    private bool RetranslateEntry(IntPtr entryPtr, int localization)
    {
        bool changed = false;

        int offset = _bindings.EntryTextsOffset;
        if (offset > 0)
        {
            IntPtr textsPtr = GameBindings.ReadPtr(entryPtr, offset);
            int count = GameBindings.ArrayLength(textsPtr);
            for (int i = 0; i < count; i++)
            {
                IntPtr elementPtr = GameBindings.ArrayElement(textsPtr, i);
                if (elementPtr == IntPtr.Zero)
                    continue;

                var renderer = new FastText.FastText(elementPtr);
                string current = renderer == null ? null : renderer.GetText();
                if (string.IsNullOrEmpty(current) || current == MissingMapping)
                    continue;

                string id;
                if (!_textToId.TryGetValue(current, out id))
                    continue;

                string text = LookupTranslation(id, localization);
                if (string.IsNullOrEmpty(text) || text == current)
                    continue;

                SetFastText(renderer, text, localization);
                changed = true;
            }
        }

        if (_bindings.EntryNameOffset > 0 && FindNameMapping() != null)
        {
            IntPtr namePtr = GameBindings.ReadPtr(entryPtr, _bindings.EntryNameOffset);
            if (namePtr != IntPtr.Zero)
            {
                var nameRenderer = new FastText.FastText(namePtr);
                string displayed = nameRenderer == null ? null : nameRenderer.GetText();
                if (!string.IsNullOrEmpty(displayed) && displayed != MissingMapping)
                {
                    Str.DynamicString resolved = _nameMapping.Get(
                        Str.DynamicStringTypeFlags.StoryName,
                        (Str.Strings.Localization)localization,
                        new ifapp.Game.Data.RawStoryName(displayed));

                    string name = resolved == null ? null : resolved.CreateString();
                    if (!IsUnusable(name) && name != displayed)
                    {
                        SetFastText(nameRenderer, name, localization);
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Re-translates the phone chat overlay. Its bubbles are created from
    /// StoryPhoneChatMessageAddedEvent, and that event carries a message that
    /// StoryCommandProcessor.ProcessPhoneChatCommand already resolved (in the language that was
    /// active at that moment). Bubbles that are already on screen therefore have to be rewritten
    /// the same way the LOG entries are: look the current text up in the translation table, then
    /// resolve it again in the selected language.
    /// </summary>
    public bool RefreshOpenPhoneChat()
    {
        try
        {
            int localization = Plugin.StoryLocalization;
            if (localization == 0)
                return false;

            _debug("phone: searching container");
            var container = UnityEngine.Object.FindObjectOfType<ifapp.Game.Scenes.Story.PhoneChatContainer>();
            if (container == null)
                return false;

            UnityEngine.GameObject host = container.gameObject;
            if (host == null || !host.activeInHierarchy)
                return false;

            _debug("phone: container is visible");
            EnsureReverseIndex();
            if (_textToId == null)
                return false;

            _debug("phone: collecting texts");
            Il2CppReferenceArray<UnityEngine.Component> texts =
                container.GetComponentsInChildren(Il2CppType.Of<FastText.FastText>(), false);
            if (texts == null || texts.Length == 0)
                return false;

            _debug("phone: rewriting " + texts.Length + " texts");

            int changed = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                UnityEngine.Component component = texts[i];
                if (component == null)
                    continue;

                var renderer = new FastText.FastText(IL2CPP.Il2CppObjectBaseToPtr(component));
                string current = renderer == null ? null : renderer.GetText();
                if (IsUnusable(current))
                    continue;

                string id;
                if (_textToId.TryGetValue(current, out id))
                {
                    string text = LookupTranslation(id, localization);
                    if (!IsUnusable(text) && text != current)
                    {
                        SetFastText(renderer, text, localization);
                        changed++;
                    }
                    continue;
                }

                if (FindNameMapping() == null)
                    continue;

                Str.DynamicString resolved = _nameMapping.Get(
                    Str.DynamicStringTypeFlags.StoryName,
                    (Str.Strings.Localization)localization,
                    new ifapp.Game.Data.RawStoryName(current));

                string name = resolved == null ? null : resolved.CreateString();
                if (!IsUnusable(name) && name != current)
                {
                    SetFastText(renderer, name, localization);
                    changed++;
                }
            }

            _debug($"phone chat texts refreshed: {changed}/{texts.Length}");
            return changed > 0;
        }
        catch (Exception e)
        {
            _debug("phone chat refresh threw: " + e.Message);
            return false;
        }
    }

    private void SetFastText(FastText.FastText renderer, string text, int localization)
    {
        int previous = GameLocalization;
        GameBindings.SetLocalization(_bindings.TargetLocalizationField, localization);
        try
        {
            renderer.SetText(new Str.DynamicString(text), FastText.FastText.UpdateType.Default);
            if (renderer.MaxVisibleCharacters < text.Length)
                renderer.SetMaxVisibleCharacters(text.Length, FastText.FastText.UpdateType.Default);
        }
        finally
        {
            GameBindings.SetLocalization(_bindings.TargetLocalizationField, previous);
        }
    }
    // ---------------------------------------------------------------- diagnostics

    public IntPtr ReadCurrentStoryStringIdPointer(IntPtr scene)
    {
        if (_bindings.SceneTextIdOffset <= 0)
            return IntPtr.Zero;
        return GameBindings.ReadPtr(scene, _bindings.SceneTextIdOffset);
    }

    public string ReadCurrentStoryStringId(IntPtr scene)
    {
        IntPtr id = ReadCurrentStoryStringIdPointer(scene);
        return id == IntPtr.Zero ? null : Il2.ToManaged(id);
    }

    public string DescribeScene(IntPtr scene)
    {
        IntPtr english = GameBindings.ReadPtr(scene, _bindings.EnglishTextBoxesOffset);
        IntPtr japanese = GameBindings.ReadPtr(scene, _bindings.JapaneseTextBoxesOffset);
        IntPtr chinese = GameBindings.ReadPtr(scene, _bindings.ChineseTextBoxesOffset);
        IntPtr selected = GameBindings.ReadPtr(scene, _bindings.SceneTextBoxesOffset);
        IntPtr state = GameBindings.ReadPtr(scene, _bindings.SceneStateOffset);
        IntPtr textBox = state == IntPtr.Zero ? IntPtr.Zero : GameBindings.ReadPtr(state, _bindings.StateTextBoxOffset);

        string activeName = selected == english ? "english"
            : selected == japanese ? "japanese"
            : selected == chinese ? "chinese"
            : selected == IntPtr.Zero ? "none" : "other";

        return "scene: gameLocalization=" + GameLocalization +
               ", override=" + Plugin.StoryLocalization +
               ", textId='" + (ReadCurrentStoryStringId(scene) ?? "<none>") + "'" +
               ", sets english/japanese/chinese=" + GameBindings.ArrayLength(english) + "/" +
               GameBindings.ArrayLength(japanese) + "/" + GameBindings.ArrayLength(chinese) +
               ", activeSet=" + activeName +
               ", textBoxState=" + (textBox != IntPtr.Zero ? "ok" : "null");
    }
}