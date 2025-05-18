using System.Runtime.InteropServices;
using Docker.DotNet.Models;
using Serilog;

namespace DockerApi;

public static class DockerExtensions
{
    public static string GetDockerApiEndpoint()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "npipe://./pipe/docker_engine";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "unix:/var/run/docker.sock";
        }
        else
        {
            Log.Error(new PlatformNotSupportedException("Unsupported operating system."), "PlatformNotSupportedException");
            throw new PlatformNotSupportedException("Unsupported operating system.");
        }
    }

    public static IDictionary<string, IList<PortBinding>> GetPortBindings(this ContainerListResponse container)
    {

        var dic = new Dictionary<string, IList<PortBinding>>();

        var groups = container.Ports?.GroupBy(p => p.PrivatePort.ToString() + "/" + p.Type) ?? [];

        foreach (var g in groups)
        {
            var portBindings = new List<PortBinding>();
            foreach (var port in g)
            {
                portBindings.Add(new PortBinding { HostPort = port.PublicPort.ToString() });
            }

            portBindings = portBindings.DistinctBy(x => x.HostPort).ToList();

            dic.TryAdd(g.Key, portBindings);
        }

        return dic;
    }

    public static IDictionary<string, EmptyStruct> GetExposedPorts(this ContainerListResponse container)
    {
        var dic = new Dictionary<string, EmptyStruct>();
        var groups = container.Ports?.GroupBy(p => p.PrivatePort.ToString() + "/" + p.Type) ?? [];

        foreach (var g in groups)
        {
            dic.TryAdd(g.Key, new EmptyStruct());
        }

        return dic;
    }
}
