using System.IO;
namespace VertcoinOcm;

// Receiving addresses remain bound to the wallet and local node that returned them.
public sealed record CoreAddressBinding(string Address, string Wallet, string DataDirectory, int RpcPort, string Network)
{
    public static CoreAddressBinding Capture(string address, string wallet, CoreNodeSettings settings) =>
        new(address, wallet, Path.GetFullPath(settings.DataDirectory), settings.RpcPort, settings.Network);
    public bool Matches(string wallet, CoreNodeSettings settings) =>
        Wallet == wallet && RpcPort == settings.RpcPort && Network == settings.Network &&
        string.Equals(DataDirectory, Path.GetFullPath(settings.DataDirectory), StringComparison.OrdinalIgnoreCase);
}
