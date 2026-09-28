using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using AutoSettings.Core;
using AutoSettings.Core.Updates;
using Microsoft.Win32;

namespace AutoSettings.Platform.Updates;

/// <summary>Detects how AutoSettings was installed.</summary>
public static class InstallInfo
{
    /// <summary>Registry key the MSI writes (<c>HKLM\Software\AutoSettings</c>).</summary>
    public const string RegistryPath = @"Software\" + Product.Name;

    /// <summary>MSI installs write <c>InstallMethod = msi</c>; everything else is a portable install.</summary>
    public static InstallMethod Detect()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(RegistryPath);
            return string.Equals(key?.GetValue("InstallMethod") as string, "msi", StringComparison.OrdinalIgnoreCase)
                ? InstallMethod.Msi
                : InstallMethod.Portable;
        }
        catch
        {
            return InstallMethod.Portable;
        }
    }
}

/// <summary>Authenticode signature checks for downloaded installers.</summary>
public static class Authenticode
{
    /// <summary>
    /// The subject of the certificate that signed <paramref name="path"/>, if the file has a valid, trusted
    /// signature; otherwise null.
    /// </summary>
    public static string? TrustedSigner(string path)
    {
        if (!Verify(path))
            return null;
        try
        {
#pragma warning disable SYSLIB0057 // No loader replacement exists for reading the signer of a signed file.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The subject of the certificate that signed <paramref name="path"/>, without checking trust; null if unsigned.</summary>
    public static string? Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs WinVerifyTrust (Authenticode policy) on a file.</summary>
    public static bool Verify(string path)
    {
        var fileInfo = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = path,
        };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeWholeChain,
                dwUnionChoice = WtdChoiceFile,
                pFile = filePointer,
                dwStateAction = WtdStateActionVerify,
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
