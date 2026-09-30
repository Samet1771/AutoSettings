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
}
