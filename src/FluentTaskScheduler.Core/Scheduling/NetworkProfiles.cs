using System.Security;
using Microsoft.Win32;

namespace FluentTaskScheduler.Scheduling;

/// <summary>A network Windows remembers, as a task's "only if this network is available" condition refers to it.</summary>
public sealed record NetworkProfile(Guid Id, string Name);

public interface INetworkProfiles
{
    /// <summary>The known networks; empty when they cannot be read, which is the case without administrator rights.</summary>
    IReadOnlyList<NetworkProfile> GetAll();
}

/// <summary>Reads the network profiles from where Windows keeps them.</summary>
public sealed class RegistryNetworkProfiles : INetworkProfiles
{
    private const string ProfilesKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";

    public IReadOnlyList<NetworkProfile> GetAll()
    {
        try
        {
            using RegistryKey? profiles = Registry.LocalMachine.OpenSubKey(ProfilesKey);
            if (profiles is null) return [];

            var found = new List<NetworkProfile>();
            foreach (string id in profiles.GetSubKeyNames())
            {
                using RegistryKey? profile = profiles.OpenSubKey(id);
                if (Guid.TryParse(id, out Guid guid) && profile?.GetValue("ProfileName") is string { Length: > 0 } name)
                    found.Add(new NetworkProfile(guid, name));
            }
            return found.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Serilog.Log.Information(ex, "The network profiles could not be read");
            return [];
        }
    }
}
