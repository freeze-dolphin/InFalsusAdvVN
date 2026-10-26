using System;
using System.Reflection;
using HarmonyLib;
using ifapp.Game.NovelEngine;
using ifapp.Game.Scenes.Story;
using ifapp.Game.Scenes;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace InFalsusAdvVN;

/// <summary>
/// Makes the game itself resolve and render story text in the language chosen by the hotkey.
///
/// Why it has to happen here (from the disassembly of this build):
///   StoryCommandProcessor.ProcessSayTextCommand resolves a line with
///   Strings.GetDynamic(DynamicStringTypeFlags.StoryText, StoryStringId). <c>GetDynamic</c> reads
///   Str.Strings.targetLocalization, so the language of a line is fixed at that moment and stored in
///   the command. The visible text later comes from StoryTextDisplayCountChangedEvent, which carries
///   a slice of *that* stored text - never from StoryState.TextBoxState.CurrentText. Rewriting the
///   text box or CurrentText therefore only lasts until the next display tick.
///   StoryScene._jp then hands the slice to FastText.SetText(DynamicString, UpdateType), and
///   FastText derives the font family and line-breaking rules from
///   Str.Strings.GetCurrentLocalization().
///
/// So the mod brackets exactly those two calls with the requested localization:
///   * ProcessSayTextCommand - the line is resolved (and stored) in the requested language.
///   * StoryScene._jp       - the line is rendered with that language's font metrics, and the
///                            per-language FastText renderer set is selected the same way the game
///                            itself selects it in StoryScene._Gp.
/// Everything else (menus, settings, card text, ...) is untouched because targetLocalization is
/// restored before the call returns.
/// </summary>
internal static class StoryLocalizationPatch
{
    private static Action<string> _log;
    private static int _depth;

    public static bool Installed { get; private set; }

    public static void Install(StoryTextController controller, Action<string> log)
    {
        _log = log;
        _controller = controller;

        var harmony = new Harmony(Plugin.Guid + ".storyloc");
        int patched = 0;

        try
        {
            MethodInfo resolve = AccessTools.Method(typeof(StoryCommandProcessor), "ProcessSayTextCommand");
            if (resolve != null)
            {
                harmony.Patch(resolve,
                    prefix: new HarmonyMethod(typeof(StoryLocalizationPatch), nameof(BeginPrefix)),
                    postfix: new HarmonyMethod(typeof(StoryLocalizationPatch), nameof(EndPostfix)));
                patched++;
            }
            else
            {
                log("StoryLocalizationPatch: StoryCommandProcessor.ProcessSayTextCommand not found.");
            }
        }
        catch (Exception e)
        {
            log("StoryLocalizationPatch (resolve) failed: " + e.Message);
        }

        try
        {
            MethodInfo render = AccessTools.Method(typeof(StoryScene), "_jp");
            if (render != null)
            {
                harmony.Patch(render,
                    prefix: new HarmonyMethod(typeof(StoryLocalizationPatch), nameof(RenderPrefix)),
                    postfix: new HarmonyMethod(typeof(StoryLocalizationPatch), nameof(EndPostfix)));
                patched++;
            }
            else
            {
                log("StoryLocalizationPatch: StoryScene._jp not found.");
            }
        }
        catch (Exception e)
        {
            log("StoryLocalizationPatch (render) failed: " + e.Message);
        }

        // The LOG panel rebuilds history from the command list in ChatLogContainer._aS and resolves
        // every line again, so it needs the same language bracket or it always renders the game
        // language.
        try
        {
            MethodInfo logBuild = AccessTools.Method(typeof(ChatLogContainer), "_aS");
            if (logBuild != null)
            {
                harmony.Patch(logBuild,
                    prefix: new HarmonyMethod(typeof(StoryLocalizationPatch), nameof(BeginPrefix)),
                    postfix: new HarmonyMethod(typeof(StoryLocalizationPatch), nameof(EndPostfix)));
                patched++;
            }
            else
            {
                log("StoryLocalizationPatch: ChatLogContainer._aS not found (LOG panel stays in the game language).");
            }
        }
        catch (Exception e)
        {
            log("StoryLocalizationPatch (log) failed: " + e.Message);
        }

        Installed = patched >= 2;
        log("StoryLocalizationPatch installed on " + patched + " story-localization hook(s).");
    }

    private static StoryTextController _controller;

    private static IntPtr Field => _controller == null ? IntPtr.Zero : _controller.LocalizationField;

    private static void Enter()
    {
        if (Plugin.StoryLocalization == 0 || _controller == null)
            return;

        if (_depth++ != 0)
            return;

        _saved = GameBindings.CurrentLocalization(Field);
        GameBindings.SetLocalization(Field, Plugin.StoryLocalization);
    }

    private static int _saved;

    private static void Exit()
    {
        if (_controller == null || _depth == 0)
            return;

        if (--_depth != 0)
            return;

        GameBindings.SetLocalization(Field, _saved == 0 ? 1 : _saved);
    }

    // ReSharper disable once InconsistentNaming
    private static void BeginPrefix() => Enter();

    // ReSharper disable once InconsistentNaming
    private static void EndPostfix() => Exit();

    /// <summary>
    /// Before the scene renders an event: switch the language, then make sure the FastText set that
    /// matches it is the active one (StoryScene._Gp normally does this from GetCurrentLocalization()).
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private static void RenderPrefix(StoryScene __instance)
    {
        Enter();

        try
        {
            if (Plugin.StoryLocalization == 0 || __instance == null || _controller == null)
                return;

            IntPtr scenePtr = IL2CPP.Il2CppObjectBaseToPtr(__instance);
            _controller.PrepareRendererSet(scenePtr, Plugin.StoryLocalization, false);
        }
        catch (Exception e)
        {
            _log?.Invoke("StoryLocalizationPatch render hook: " + e.Message);
        }
    }
}
