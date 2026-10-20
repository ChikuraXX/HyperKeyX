using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HyperKeyX.Application.Appearance;
using HyperKeyX.Application.Abstractions.Appearance;
using HyperKeyX.Domain.Appearance;

namespace HyperKeyX.Infrastructure.Appearance;

/// <summary>使用 DirectWrite 系统集合枚举当前账户可用的已安装字体，不启用在线字体下载。</summary>
public sealed class WindowsFontCatalog : IInstalledFontCatalog
{
    /// <summary>在后台线程调用；每次返回快照，所有原生对象在返回前释放。</summary>
    public IReadOnlyList<InstalledFontFamily> ReadInstalledFamilies()
    {
        var iid = typeof(IFactory).GUID;
        Marshal.ThrowExceptionForHR(DWriteCreateFactory(0, ref iid, out var factory));
        try
        {
            factory.GetSystemFontCollection(out var collection, true);
            try
            {
                var result = new List<InstalledFontFamily>();
                for (uint i = 0; i < collection.GetFontFamilyCount(); i++)
                {
                    collection.GetFontFamily(i, out var family);
                    try
                    {
                        family.GetFamilyNames(out var names);
                        try
                        {
                            var aliases = Enumerable.Range(0, checked((int)names.GetCount()))
                                .Select(index => ReadName(names, (uint)index)).ToArray();
                            var canonical = GetLocalizedName(names, "en-us");
                            var display = GetLocalizedName(names, CultureInfo.CurrentUICulture.Name);
                            family.GetFirstMatchingFont(400, 5, 0, out var font);
                            try
                            {
                                var languages = FontLanguageCoverage.Classify(code =>
                                {
                                    font.HasCharacter((uint)code, out var supported);
                                    return supported;
                                });
                                result.Add(new(canonical, display, aliases, languages));
                            }
                            finally { Marshal.ReleaseComObject(font); }
                        }
                        finally { Marshal.ReleaseComObject(names); }
                    }
                    finally { Marshal.ReleaseComObject(family); }
                }
                return result.OrderBy(font => font.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    private static string GetLocalizedName(ILocalizedStrings names, string locale)
    {
        names.FindLocaleName(locale, out var index, out var exists);
        if (!exists)
            names.FindLocaleName("en-us", out index, out exists);
        return ReadName(names, exists ? index : 0);
    }

    private static string ReadName(ILocalizedStrings names, uint index)
    {
        names.GetStringLength(index, out var length);
        var text = new StringBuilder(checked((int)length + 1));
        names.GetString(index, text, (uint)text.Capacity);
        return text.ToString();
    }

    // 这些最小 COM 声明保持 dwrite.h 的方法顺序；未使用方法只占原生虚表槽位。
    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(int type, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IFactory factory);

    [ComImport, Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFactory
    {
        void GetSystemFontCollection(out ICollection collection, [MarshalAs(UnmanagedType.Bool)] bool checkForUpdates);
    }

    [ComImport, Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICollection
    {
        [PreserveSig] uint GetFontFamilyCount();
        void GetFontFamily(uint index, out IFamily family);
    }

    [ComImport, Guid("da20d8ef-812a-4c43-9802-62ec4abd7add"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFamily
    {
        void GetFontCollection(out ICollection collection);
        [PreserveSig] uint GetFontCount();
        void GetFont(uint index, out IFont font);
        void GetFamilyNames(out ILocalizedStrings names);
        void GetFirstMatchingFont(int weight, int stretch, int style, out IFont font);
    }

    [ComImport, Guid("08256209-099a-4b34-b86d-c22b110e7771"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILocalizedStrings
    {
        [PreserveSig] uint GetCount();
        void FindLocaleName([MarshalAs(UnmanagedType.LPWStr)] string locale, out uint index, [MarshalAs(UnmanagedType.Bool)] out bool exists);
        void GetLocaleNameLength(uint index, out uint length);
        void GetLocaleName(uint index, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint size);
        void GetStringLength(uint index, out uint length);
        void GetString(uint index, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, uint size);
    }

    [ComImport, Guid("acd16696-8c14-4f5d-877e-fe3fc1d32737"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFont
    {
        void GetFontFamily(out IFamily family);
        [PreserveSig] int GetWeight();
        [PreserveSig] int GetStretch();
        [PreserveSig] int GetStyle();
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)] bool IsSymbolFont();
        void GetFaceNames(out ILocalizedStrings names);
        void GetInformationalStrings(int id, out ILocalizedStrings strings, [MarshalAs(UnmanagedType.Bool)] out bool exists);
        [PreserveSig] int GetSimulations();
        [PreserveSig] void GetMetrics(IntPtr metrics);
        void HasCharacter(uint character, [MarshalAs(UnmanagedType.Bool)] out bool exists);
    }
}
