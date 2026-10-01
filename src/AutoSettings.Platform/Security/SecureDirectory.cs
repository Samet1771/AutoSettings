using System.Security.AccessControl;
using System.Security.Principal;

namespace AutoSettings.Platform.Security;

/// <summary>Folders that only SYSTEM and administrators may change.</summary>
public static class SecureDirectory
{
    /// <summary>
    /// Creates <paramref name="path"/>, or resets the permissions of an existing folder, so that SYSTEM and
    /// administrators have full control and users can only read. Inherited permissions are removed, so a
    /// parent folder that users can write to does not weaken it.
    /// </summary>
    public static void Create(string path)
    {
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        var directory = new DirectoryInfo(path);
        if (directory.Exists)
            directory.SetAccessControl(security);
        else
            directory.Create(security);
    }

    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>
    /// Whether only SYSTEM, administrators and TrustedInstaller can change <paramref name="path"/>: its owner is one of
    /// them and no allow rule gives anybody else a right to write, delete or change permissions. The service only runs
    /// plugins from such folders, because their code runs as SYSTEM.
    /// </summary>
    /// <param name="path">A folder.</param>
    /// <param name="problem">Why not, in plain language.</param>
    public static bool IsAdminOnly(string path, out string? problem)
    {
        problem = null;
        try
        {
            var security = new DirectoryInfo(path).GetAccessControl();
            if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !IsTrusted(owner))
            {
                problem = $"{path} is owned by {Name(owner)}, not by administrators.";
                return false;
            }
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & WriteRights) == 0)
                    continue;
                if (rule.IdentityReference is SecurityIdentifier sid && (IsTrusted(sid) || sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid)))
                    continue;
                problem = $"{Name(rule.IdentityReference)} can change {path}.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problem = $"Cannot check the permissions of {path}: {ex.Message}";
            return false;
        }
    }

    private static bool IsTrusted(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
        || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
        || sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"; // NT SERVICE\TrustedInstaller

    private static string Name(IdentityReference identity)
    {
        try
        {
            return identity.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return identity.Value;
        }
    }
}
