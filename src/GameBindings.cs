using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InFalsusAdvVN;

/// <summary>
/// All game-side objects the mod touches, resolved at load time through the IL2CPP reflection API.
/// Resolution is by class / member name and by declared field type, never by hard-coded addresses.
///
/// Layout facts recovered from the shipped binary (GameAssembly.dll + global-metadata.dat):
///   StoryScene._gS   (+0x1D8) = StoryState          StoryScene._xS (+0x2D0) = FastText[] in use
///   StoryScene._vS   (+0x2B0) = current StoryStringId
///   StoryScene.englishText / japaneseText / chineseText (+0x120/+0x128/+0x130)
///   StoryState.TextBoxState (+0x98) = StoryTextBoxState
///   StoryTextBoxState.CurrentText / NumberOfCharacterToDisplay
///   StoryTranslationDetails.Translations = List&lt;StringMapping&gt;
///   StringMapping { Id, English, Japanese, Korean, ChineseTC, ChineseSC }
/// </summary>
internal sealed class GameBindings
{
    // Str.Strings
    public IntPtr StringsClass;
    public IntPtr TargetLocalizationField;

    // Str.DynamicString
    public IntPtr DynamicStringClass;
    public int DynamicStringSize;
    public IntPtr DynamicStringCtor;          // .ctor(string)

    // StoryTranslationDetails + StringMapping
    public IntPtr TranslationDetailsClass;
    public int TranslationsOffset;            // List<StringMapping>
    public IntPtr StringMappingClass;
    public int MappingSize;
    public int MappingIdOffset;
    public int MappingEnglishOffset;
    public int MappingJapaneseOffset;
    public int MappingKoreanOffset;
    public int MappingChineseTCOffset;
    public int MappingChineseSCOffset;

    // StoryState / StoryTextBoxState
    public IntPtr StoryStateClass;
    public int StateTextBoxOffset;
    public IntPtr TextBoxStateClass;
    public int CurrentTextOffset;
    public int DisplayCountOffset;
    public int CharacterNameOffset;

    // StoryScene
    public IntPtr StorySceneClass;
    public int SceneStateOffset;
    public int SceneTextBoxesOffset;
    public int SceneTextIdOffset;
    public int EnglishTextBoxesOffset;
    public int JapaneseTextBoxesOffset;
    public int ChineseTextBoxesOffset;

    // StoryScene.storyNameContainer (speaker name plate)
    public int SceneNameContainerOffset;
    public int SceneChatLogOffset;             // StoryScene.chatLogContainer (LOG panel)

    // ChatLogEntryContainer
    public IntPtr ChatLogEntryClass;
    public int EntryNameOffset;                // characterName (FastText)
    public int EntryTextsOffset;               // chatTexts (FastText[])
    public int EntryIdOffset;                  // debug_uniqueId (StoryStringId)
    public IntPtr NameContainerClass;
    public IntPtr NameContainerSetMethod;   // StoryNameContainer._yO(Str.DynamicString)

    // FastText
    public IntPtr FastTextClass;
    public IntPtr SetTextMethod;              // SetText(Str.DynamicString, UpdateType)
    public IntPtr SetTextNonLocalizedMethod;  // SetTextNonLocalized(string, UpdateType)
    public IntPtr SetMaxVisibleCharactersMethod;

    // UnityEngine.Component / GameObject
    public IntPtr GetGameObjectMethod;
    public IntPtr SetActiveMethod;

    public bool Ready { get; private set; }

    public static GameBindings Resolve(Action<string> log, bool dump = false)
    {
        var b = new GameBindings();
        var problems = new List<string>();

        // ---- Str.Strings --------------------------------------------------
        b.StringsClass = Il2.FindClass("Str", "Strings");
        if (b.StringsClass == IntPtr.Zero)
            problems.Add("Str.Strings not found");
        else
            b.TargetLocalizationField = Il2.Field(b.StringsClass, "targetLocalization");

        // ---- Str.DynamicString --------------------------------------------
        b.DynamicStringClass = Il2.FindClass("Str", "DynamicString");
        if (b.DynamicStringClass == IntPtr.Zero)
        {
            problems.Add("Str.DynamicString not found");
        }
        else
        {
            b.DynamicStringSize = Il2.ValueSize(b.DynamicStringClass);
            b.DynamicStringCtor = FindCtor(b.DynamicStringClass, 1);
            if (b.DynamicStringCtor == IntPtr.Zero) problems.Add("Str.DynamicString..ctor(string) not found");
        }

        // ---- StoryTranslationDetails --------------------------------------
        b.TranslationDetailsClass = Il2.FindClass("ifapp.Game.Data.Story", "StoryTranslationDetails");
        if (b.TranslationDetailsClass == IntPtr.Zero)
        {
            problems.Add("StoryTranslationDetails not found");
        }
        else
        {
            IntPtr translations = Il2.Field(b.TranslationDetailsClass, "Translations");
            if (translations != IntPtr.Zero)
                b.TranslationsOffset = Il2.FieldOffset(translations);
            if (b.TranslationsOffset == 0) problems.Add("StoryTranslationDetails.Translations not found");

            b.StringMappingClass = Il2.FindClassBySimpleName("StringMapping");
            if (b.StringMappingClass == IntPtr.Zero)
            {
                problems.Add("StoryTranslationDetails.StringMapping not found");
            }
            else
            {
                b.MappingSize = Il2.ValueSize(b.StringMappingClass);
                b.MappingIdOffset = OffsetOf(b.StringMappingClass, "Id");
                b.MappingEnglishOffset = OffsetOf(b.StringMappingClass, "English");
                b.MappingJapaneseOffset = OffsetOf(b.StringMappingClass, "Japanese");
                b.MappingKoreanOffset = OffsetOf(b.StringMappingClass, "Korean");
                b.MappingChineseTCOffset = OffsetOf(b.StringMappingClass, "ChineseTC");
                b.MappingChineseSCOffset = OffsetOf(b.StringMappingClass, "ChineseSC");

                if (dump)
                    log($"  StringMapping size={b.MappingSize}, Id=+0x{b.MappingIdOffset:X}, " +
                        $"En=+0x{b.MappingEnglishOffset:X}, Jp=+0x{b.MappingJapaneseOffset:X}, " +
                        $"Ko=+0x{b.MappingKoreanOffset:X}, TC=+0x{b.MappingChineseTCOffset:X}, " +
                        $"SC=+0x{b.MappingChineseSCOffset:X}");

                if (b.MappingIdOffset < 0 || b.MappingEnglishOffset < 0 || b.MappingJapaneseOffset < 0 ||
                    b.MappingChineseTCOffset < 0 || b.MappingChineseSCOffset < 0)
                    problems.Add("StringMapping language fields could not be resolved");
            }
        }

        // ---- StoryState / StoryTextBoxState --------------------------------
        b.StoryStateClass = Il2.FindClass("ifapp.Game.NovelEngine", "StoryState");
        b.TextBoxStateClass = Il2.FindClass("ifapp.Game.NovelEngine", "StoryTextBoxState");

        if (b.StoryStateClass == IntPtr.Zero)
        {
            problems.Add("StoryState not found");
        }
        else
        {
            foreach ((string name, string type, int offset) in Il2.ListFields(b.StoryStateClass))
            {
                if (type.IndexOf("StoryTextBoxState", StringComparison.Ordinal) >= 0)
                {
                    b.StateTextBoxOffset = offset;
                    log($"  StoryState.{name} (StoryTextBoxState) @ +0x{offset:X}");
                    break;
                }
            }

            if (b.StateTextBoxOffset == 0) problems.Add("StoryState.TextBoxState field not found");
        }

        if (b.TextBoxStateClass == IntPtr.Zero)
        {
            problems.Add("StoryTextBoxState not found");
        }
        else
        {
            foreach ((string name, string type, int offset) in Il2.ListFields(b.TextBoxStateClass))
            {
                if (name == "CurrentText") b.CurrentTextOffset = offset;
                else if (name == "CharacterName") b.CharacterNameOffset = offset;
                else if (name == "NumberOfCharacterToDisplay") b.DisplayCountOffset = offset;
            }

            if (b.CurrentTextOffset == 0) problems.Add("StoryTextBoxState.CurrentText not found");
            if (b.DisplayCountOffset == 0) problems.Add("StoryTextBoxState.NumberOfCharacterToDisplay not found");
        }

        // ---- StoryScene ----------------------------------------------------
        b.StorySceneClass = Il2.FindClass("ifapp.Game.Scenes", "StoryScene");
        if (b.StorySceneClass == IntPtr.Zero)
        {
            problems.Add("StoryScene not found");
        }
        else
        {
            List<(string Name, string Type, int Offset)> scenes = Il2.ListFields(b.StorySceneClass);
            if (dump)
            {
                log("  StoryScene fields:");
                foreach ((string name, string type, int offset) in scenes)
                    log($"    +0x{offset:X4}  {type}  {name}");
            }

            foreach ((string name, string type, int offset) in scenes)
            {
                if (type.IndexOf("StoryState", StringComparison.Ordinal) >= 0 && b.SceneStateOffset == 0)
                    b.SceneStateOffset = offset;

                if (name == "storyNameContainer") b.SceneNameContainerOffset = offset;
                if (name == "chatLogContainer") b.SceneChatLogOffset = offset;
                if (name == "englishText") b.EnglishTextBoxesOffset = offset;
                else if (name == "japaneseText") b.JapaneseTextBoxesOffset = offset;
                else if (name == "chineseText") b.ChineseTextBoxesOffset = offset;

                if (type.IndexOf("FastText", StringComparison.Ordinal) >= 0 &&
                    type.EndsWith("[]", StringComparison.Ordinal) && offset == 0x2D0)
                    b.SceneTextBoxesOffset = offset;

                if (type.IndexOf("String", StringComparison.Ordinal) >= 0 && offset == 0x2B0)
                    b.SceneTextIdOffset = offset;
            }

            log($"  StoryScene renderer sets: english=+0x{b.EnglishTextBoxesOffset:X} " +
                $"japanese=+0x{b.JapaneseTextBoxesOffset:X} chinese=+0x{b.ChineseTextBoxesOffset:X} " +
                $"active=+0x{b.SceneTextBoxesOffset:X} textId=+0x{b.SceneTextIdOffset:X}");

            if (b.SceneStateOffset == 0) problems.Add("StoryScene story-state field not found");
            if (b.SceneTextBoxesOffset == 0) problems.Add("StoryScene active renderer set not found");
            if (b.EnglishTextBoxesOffset == 0) problems.Add("StoryScene.englishText not found");
            if (b.JapaneseTextBoxesOffset == 0) problems.Add("StoryScene.japaneseText not found");
            if (b.ChineseTextBoxesOffset == 0) problems.Add("StoryScene.chineseText not found");
            if (b.SceneTextIdOffset == 0) problems.Add("StoryScene last-text-id field not found");
        }

        // ---- StoryNameContainer --------------------------------------------
        b.NameContainerClass = Il2.FindClass("ifapp.Game.Scenes", "StoryNameContainer");
        if (b.NameContainerClass != IntPtr.Zero)
        {
            foreach ((string name, IntPtr method) in Il2.ListMethods(b.NameContainerClass))
            {
                if (Il2.ParamCount(method) != 1)
                    continue;
                if (Il2.ParamTypeName(method, 0).IndexOf("DynamicString", StringComparison.Ordinal) < 0)
                    continue;
                b.NameContainerSetMethod = method;
                break;
            }

            if (dump)
                log($"  StoryNameContainer setter: {(b.NameContainerSetMethod != IntPtr.Zero ? "found" : "MISSING")}, " +
                    $"scene field +0x{b.SceneNameContainerOffset:X}");
        }
        else
        {
            log("  StoryNameContainer not found (speaker name will only refresh on the next line)");
        }

        // ---- ChatLogEntryContainer ------------------------------------------
        b.ChatLogEntryClass = Il2.FindClass("ifapp.Game.Scenes.Story", "ChatLogEntryContainer");
        if (b.ChatLogEntryClass != IntPtr.Zero)
        {
            b.EntryNameOffset = OffsetOf(b.ChatLogEntryClass, "characterName");
            b.EntryTextsOffset = OffsetOf(b.ChatLogEntryClass, "chatTexts");
            b.EntryIdOffset = OffsetOf(b.ChatLogEntryClass, "debug_uniqueId");
            if (dump)
                log($"  ChatLogEntryContainer: name=+0x{b.EntryNameOffset:X}, texts=+0x{b.EntryTextsOffset:X}, id=+0x{b.EntryIdOffset:X}");
        }
        else
        {
            log("  ChatLogEntryContainer not found (LOG panel cannot be re-translated in place).");
        }

        // ---- FastText ------------------------------------------------------
        b.FastTextClass = Il2.FindClass("FastText", "FastText");
        if (b.FastTextClass == IntPtr.Zero)
        {
            problems.Add("FastText not found");
        }
        else
        {
            List<(string Name, IntPtr Method)> methods = Il2.ListMethods(b.FastTextClass);
            if (dump)
            {
                log("  FastText SetText overloads:");
                foreach ((string name, IntPtr method) in methods)
                {
                    if (name != "SetText") continue;
                    string args = "";
                    for (int i = 0; i < Il2.ParamCount(method); i++)
                        args += (i > 0 ? ", " : "") + Il2.ParamTypeName(method, i);
                    log($"    SetText({args})");
                }
            }

            foreach ((string name, IntPtr method) in methods)
            {
                int count = Il2.ParamCount(method);

                if (name == "SetText" && count == 2 &&
                    Il2.ParamTypeName(method, 0).IndexOf("DynamicString", StringComparison.Ordinal) >= 0)
                    b.SetTextMethod = method;

                if (name == "SetMaxVisibleCharacters" && count == 2)
                    b.SetMaxVisibleCharactersMethod = method;

                if (name == "SetTextNonLocalized" && count == 2 &&
                    Il2.ParamTypeName(method, 0).IndexOf("String", StringComparison.Ordinal) >= 0)
                    b.SetTextNonLocalizedMethod = method;
            }

            if (b.SetTextNonLocalizedMethod == IntPtr.Zero) problems.Add("FastText.SetTextNonLocalized(string, UpdateType) not found");
            if (b.SetMaxVisibleCharactersMethod == IntPtr.Zero) problems.Add("FastText.SetMaxVisibleCharacters not found");
        }

        // ---- UnityEngine Component / GameObject ----------------------------
        IntPtr componentClass = Il2.FindClass("UnityEngine", "Component");
        if (componentClass != IntPtr.Zero)
            b.GetGameObjectMethod = Il2.Method(componentClass, "get_gameObject", 0);

        IntPtr gameObjectClass = Il2.FindClass("UnityEngine", "GameObject");
        if (gameObjectClass != IntPtr.Zero)
            b.SetActiveMethod = Il2.Method(gameObjectClass, "SetActive", 1);

        b.Ready = problems.Count == 0;
        foreach (string problem in problems)
            log("  MISSING: " + problem);
        log($"  Str.DynamicString size = {b.DynamicStringSize}");

        return b;
    }

    private static int OffsetOf(IntPtr klass, string fieldName)
    {
        IntPtr field = Il2.Field(klass, fieldName);
        return field == IntPtr.Zero ? -1 : Il2.FieldOffset(field);
    }

    private static IntPtr FindCtor(IntPtr klass, int paramCount)
    {
        IntPtr ctor = Il2.Method(klass, ".ctor", paramCount);
        if (ctor != IntPtr.Zero)
            return ctor;

        foreach ((string name, IntPtr method) in Il2.ListMethods(klass))
        {
            if (name == ".ctor" && Il2.ParamCount(method) == paramCount)
                return method;
        }

        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- helpers

    public static IntPtr ReadPtr(IntPtr obj, int offset)
    {
        if (obj == IntPtr.Zero || offset <= 0)
            return IntPtr.Zero;
        return Marshal.ReadIntPtr(obj, offset);
    }

    public static int ReadInt(IntPtr obj, int offset) =>
        obj == IntPtr.Zero ? 0 : Marshal.ReadInt32(obj, offset);

    public static void WriteInt(IntPtr obj, int offset, int value)
    {
        if (obj != IntPtr.Zero)
            Marshal.WriteInt32(obj, offset, value);
    }

    public static void WritePtr(IntPtr obj, int offset, IntPtr value)
    {
        if (obj != IntPtr.Zero)
            Marshal.WriteIntPtr(obj, offset, value);
    }

    /// <summary>Managed array: length at +0x18, element data at +0x20.</summary>
    public static int ArrayLength(IntPtr array) => array == IntPtr.Zero ? 0 : Marshal.ReadInt32(array, 0x18);

    public static IntPtr ArrayElement(IntPtr array, int index) =>
        array == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(array, 0x20 + index * IntPtr.Size);

    /// <summary>List&lt;T&gt;: backing array at +0x10, count at +0x18.</summary>
    public static IntPtr ListItems(IntPtr list) => ReadPtr(list, 0x10);

    public static int ListCount(IntPtr list) => ReadInt(list, 0x18);

    public static void SetLocalization(IntPtr targetLocalizationField, int value)
    {
        if (targetLocalizationField == IntPtr.Zero)
            return;

        IntPtr buffer = Il2.Alloc(4);
        try
        {
            Marshal.WriteInt32(buffer, value);
            Il2.FieldStaticSetValue(targetLocalizationField, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static int CurrentLocalization(IntPtr targetLocalizationField)
    {
        IntPtr buffer = Il2.Alloc(4);
        try
        {
            Il2.FieldStaticGetValue(targetLocalizationField, buffer);
            return Marshal.ReadInt32(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
