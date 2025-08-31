using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Serilog;

namespace DockerApi;

public class EditContainerRequest
{
    // The desired CPU limit as a percentage of total host CPUs (e.g., 0.5 for 50%)
    public decimal? CpuLimitPercent { get; set; }
}

public static class EditEndpoint
{
    public static void MapEditEndpoint(this WebApplication app)
    {
        app.MapPut("/docker/edit/{ContainerName}", async (string containerName, EditContainerRequest request, HttpContext httpContext) =>
        {
            string dockerApiEndpoint = DockerExtensions.GetDockerApiEndpoint();

            // Create a Docker client
            using var client = new DockerClientConfiguration(new Uri(dockerApiEndpoint))
                .CreateClient();

            // 1. Stop and Remove the existing container
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters());
            var existingContainer = containers.FirstOrDefault(c => c.Names.Contains("/" + containerName));

            //Check if the image exists
            if (existingContainer is null)
            {
                Log.Error(new DockerContainerNotFoundException(HttpStatusCode.NotFound, $"Container {containerName} not found."), "DockerContainerNotFoundException");
                throw new DockerContainerNotFoundException(HttpStatusCode.NotFound, $"Container {containerName} not found.");
            }

            // Get system info to find the total number of CPUs
            var systemInfo = await client.System.GetSystemInfoAsync();
            var totalCores = systemInfo.NCPU;

            var updateParams = new ContainerUpdateParameters();

            if (request.CpuLimitPercent.HasValue)
            {
                if (request.CpuLimitPercent.Value <= 0 || request.CpuLimitPercent.Value > 1)
                {
                    httpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await httpContext.Response.WriteAsync("CpuLimitPercent must be a decimal value greater than 0 and less than or equal to 1 (e.g., 0.5 for 50%).");
                    return;
                }

                // Calculate the number of cores to use based on the percentage
                var coresToUse = totalCores * request.CpuLimitPercent.Value;

                // Dynamically calculate the CPU quota based on the request.
                const long cpuPeriod = 100000; // Standard 100ms period
                updateParams.CPUPeriod = cpuPeriod;
                updateParams.CPUQuota = (long)(coresToUse * cpuPeriod);

                Log.Information("Calculating CPU limit: {TotalCores} (host cores) * {CpuPercent:P} = {CoresToUse:F2} cores. Setting CpuQuota to {CpuQuota}",
                    totalCores, request.CpuLimitPercent.Value, coresToUse, updateParams.CPUQuota);
            }

            await client.Containers.UpdateContainerAsync(existingContainer.ID, updateParams);
            Log.Information("Successfully updated container {ContainerId} with new resource limits.", existingContainer.ID);
        });
    }
}
