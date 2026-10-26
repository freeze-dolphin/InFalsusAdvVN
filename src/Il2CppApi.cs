using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace InFalsusAdvVN;

/// <summary>
/// Thin, version-agnostic binding to the IL2CPP runtime API exported by GameAssembly.dll.
/// Everything the mod needs about the game's managed types is resolved through this API so the
/// mod does not depend on obfuscated field names being present in the generated interop assembly.
/// </summary>
internal static class Il2
{
    private const string Dll = "GameAssembly";

    private const CallingConvention Cc = CallingConvention.Cdecl;

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_domain_get")]
    private static extern IntPtr DomainGet();

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_domain_get_assemblies")]
    private static extern IntPtr DomainGetAssemblies(IntPtr domain, ref uint size);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_assembly_get_image")]
    private static extern IntPtr AssemblyGetImage(IntPtr assembly);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_image_get_name")]
    private static extern IntPtr ImageGetName(IntPtr image);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_from_name")]
    private static extern IntPtr ClassFromName(IntPtr image, string namespaze, string name);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_image_get_class_count")]
    private static extern uint ImageGetClassCount(IntPtr image);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_image_get_class")]
    private static extern IntPtr ImageGetClass(IntPtr image, int index);
    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_get_name")]
    private static extern IntPtr ClassGetName(IntPtr klass);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_get_namespace")]
    private static extern IntPtr ClassGetNamespace(IntPtr klass);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_value_size")]
    private static extern uint ClassValueSize(IntPtr klass, out uint align);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_get_fields")]
    private static extern IntPtr ClassGetFields(IntPtr klass, out IntPtr iter);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_get_field_from_name")]
    private static extern IntPtr ClassGetFieldFromName(IntPtr klass, string name);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_field_get_name")]
    private static extern IntPtr FieldGetName(IntPtr field);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_field_get_offset")]
    private static extern int FieldGetOffset(IntPtr field);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_field_get_type")]
    private static extern IntPtr FieldGetType(IntPtr field);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_type_get_name")]
    private static extern IntPtr TypeGetName(IntPtr type);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_get_method_from_name")]
    private static extern IntPtr ClassGetMethodFromName(IntPtr klass, string name, int argCount);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_class_get_methods")]
    private static extern IntPtr ClassGetMethods(IntPtr klass, out IntPtr iter);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_method_get_name")]
    private static extern IntPtr MethodGetName(IntPtr method);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_method_get_param_count")]
    private static extern int MethodGetParamCount(IntPtr method);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_method_get_param")]
    private static extern IntPtr MethodGetParam(IntPtr method, int index);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_object_get_class")]
    internal static extern IntPtr ObjectGetClass(IntPtr obj);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_runtime_invoke")]
    private static extern IntPtr RuntimeInvoke(IntPtr method, IntPtr obj, IntPtr[] parameters, out IntPtr exception);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_object_new")]
    internal static extern IntPtr ObjectNew(IntPtr klass);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_object_unbox")]
    internal static extern IntPtr ObjectUnbox(IntPtr obj);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_string_new_utf16")]
    private static extern IntPtr StringNewUtf16(IntPtr text, int length);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_string_length")]
    internal static extern int StringLength(IntPtr value);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_string_chars")]
    internal static extern IntPtr StringChars(IntPtr value);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_gchandle_new")]
    private static extern uint GcHandleNew(IntPtr obj, bool pinned);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_gchandle_free")]
    private static extern void GcHandleFree(uint handle);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_field_get_value")]
    private static extern void FieldGetValue(IntPtr obj, IntPtr field, IntPtr value);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_field_static_get_value")]
    internal static extern void FieldStaticGetValue(IntPtr field, IntPtr value);

    [DllImport(Dll, CallingConvention = Cc, EntryPoint = "il2cpp_field_static_set_value")]
    internal static extern void FieldStaticSetValue(IntPtr field, IntPtr value);

    // ---------------------------------------------------------------- helpers

    private static IntPtr[] _images;

    public static IReadOnlyList<string> ImageNames { get; private set; } = Array.Empty<string>();

    public static string ReadAnsi(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);

    public static void CacheImages()
    {
        if (_images != null)
            return;

        IntPtr domain = DomainGet();
        uint count = 0;
        IntPtr array = DomainGetAssemblies(domain, ref count);
        var images = new IntPtr[count];
        var names = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            IntPtr assembly = Marshal.ReadIntPtr(array, (int)i * IntPtr.Size);
            IntPtr image = AssemblyGetImage(assembly);
            images[i] = image;
            names.Add(ReadAnsi(ImageGetName(image)));
        }

        _images = images;
        ImageNames = names;
    }

    /// <summary>Find a class by namespace/name, searching every loaded image (assembly names vary per build).</summary>
    public static IntPtr FindClass(string namespaze, string name)
    {
        CacheImages();
        foreach (IntPtr image in _images)
        {
            IntPtr klass = ClassFromName(image, namespaze, name);
            if (klass != IntPtr.Zero)
                return klass;
        }

        return IntPtr.Zero;
    }

    private static readonly System.Collections.Generic.Dictionary<string, IntPtr> SimpleNameCache =
        new System.Collections.Generic.Dictionary<string, IntPtr>(StringComparer.Ordinal);

    /// <summary>Find a class by its simple name across all images (works for nested types too).</summary>
    public static IntPtr FindClassBySimpleName(string simpleName)
    {
        if (SimpleNameCache.TryGetValue(simpleName, out IntPtr cached))
            return cached;

        CacheImages();
        foreach (IntPtr image in _images)
        {
            uint count = ImageGetClassCount(image);
            for (int i = 0; i < count; i++)
            {
                IntPtr klass = ImageGetClass(image, i);
                if (klass == IntPtr.Zero)
                    continue;
                string name = ReadAnsi(ClassGetName(klass));
                if (name != simpleName)
                    continue;
                SimpleNameCache[simpleName] = klass;
                return klass;
            }
        }

        SimpleNameCache[simpleName] = IntPtr.Zero;
        return IntPtr.Zero;
    }
    public static string ClassName(IntPtr klass)
    {
        if (klass == IntPtr.Zero)
            return "<null>";
        string ns = ReadAnsi(ClassGetNamespace(klass));
        string n = ReadAnsi(ClassGetName(klass));
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }

    public static string TypeName(IntPtr type) => ReadAnsi(TypeGetName(type));

    public static int ValueSize(IntPtr klass)
    {
        if (klass == IntPtr.Zero)
            return 0;
        return (int)ClassValueSize(klass, out _);
    }

    public static List<(string Name, IntPtr Method)> ListMethods(IntPtr klass)
    {
        var result = new List<(string, IntPtr)>();
        if (klass == IntPtr.Zero)
            return result;

        IntPtr iter = IntPtr.Zero;
        IntPtr method;
        while ((method = ClassGetMethods(klass, out iter)) != IntPtr.Zero)
            result.Add((ReadAnsi(MethodGetName(method)), method));

        return result;
    }

    public static int ParamCount(IntPtr method) => MethodGetParamCount(method);

    public static string ParamTypeName(IntPtr method, int index) =>
        TypeName(MethodGetParam(method, index));

    public static IntPtr ObjectClass(IntPtr obj) => obj == IntPtr.Zero ? IntPtr.Zero : ObjectGetClass(obj);

    public static int FieldOffset(IntPtr field) => FieldGetOffset(field);

    public static IntPtr Field(IntPtr klass, string name) => ClassGetFieldFromName(klass, name);

    public static List<(string Name, string Type, int Offset)> ListFields(IntPtr klass)
    {
        var result = new List<(string, string, int)>();
        if (klass == IntPtr.Zero)
            return result;

        IntPtr iter = IntPtr.Zero;
        IntPtr field;
        while ((field = ClassGetFields(klass, out iter)) != IntPtr.Zero)
        {
            result.Add((ReadAnsi(FieldGetName(field)),
                        TypeName(FieldGetType(field)),
                        FieldGetOffset(field)));
        }

        return result;
    }

    public static IntPtr Method(IntPtr klass, string name, int argCount) =>
        ClassGetMethodFromName(klass, name, argCount);

    /// <summary>Invoke a method; returns the raw returned object (value types are boxed by IL2CPP).</summary>
    public static IntPtr Invoke(IntPtr method, IntPtr instance, IntPtr[] args, out string error)
    {
        error = null;
        if (method == IntPtr.Zero)
        {
            error = "method not found";
            return IntPtr.Zero;
        }

        IntPtr result = RuntimeInvoke(method, instance, args, out IntPtr exception);
        if (exception != IntPtr.Zero)
        {
            error = DescribeException(exception);
            return IntPtr.Zero;
        }

        return result;
    }

    public static string DescribeException(IntPtr exception)
    {
        try
        {
            // exception.ToString() through il2cpp
            IntPtr objectKlass = FindClass("System", "Object");
            IntPtr toString = Method(objectKlass, "ToString", 0);
            IntPtr result = RuntimeInvoke(toString, exception, null, out _);
            if (result != IntPtr.Zero)
                return ToManaged(result);
        }
        catch
        {
            // ignored
        }

        return "il2cpp exception";
    }

    /// <summary>Creates a managed string through il2cpp using UTF-16 so CJK text is preserved.</summary>
    public static IntPtr NewString(string value)
    {
        if (value == null)
            return IntPtr.Zero;

        int length = value.Length;
        int bytes = length == 0 ? 1 : length * 2;
        IntPtr buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            for (int i = 0; i < length; i++)
                Marshal.WriteInt16(buffer, i * 2, (short)value[i]);

            return StringNewUtf16(buffer, length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
    public static string ToManaged(IntPtr il2cppString)
    {
        if (il2cppString == IntPtr.Zero)
            return null;
        int length = StringLength(il2cppString);
        if (length <= 0)
            return string.Empty;
        IntPtr chars = StringChars(il2cppString);
        return Marshal.PtrToStringUni(chars, length);
    }

    public static uint Pin(IntPtr obj) => GcHandleNew(obj, false);

    public static void Unpin(uint handle)
    {
        if (handle != 0)
            GcHandleFree(handle);
    }

    public static IntPtr Alloc(int bytes)
    {
        IntPtr p = Marshal.AllocHGlobal(bytes);
        unsafe
        {
            new Span<byte>((void*)p, bytes).Clear();
        }

        return p;
    }
}
