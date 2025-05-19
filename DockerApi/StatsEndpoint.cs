using System.Data;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace DockerApi;

public static class StatsEndpoint
{
    public static void MapStatsEndpoint(this WebApplication app)
    {
        app.MapGet("/docker/stats/{ContainerName}", async (string containerName, CancellationToken cT) =>
        {
            if (string.IsNullOrEmpty(containerName))
            {
                Serilog.Log.Warning("BadRequest: ContainerName is required");
                return Results.BadRequest(new { error = "ContainerName is required." });
            }

            // Get the Docker API endpoint
            string dockerApiEndpoint = DockerExtensions.GetDockerApiEndpoint();

            // Create a Docker client
            using var client = new DockerClientConfiguration(new Uri(dockerApiEndpoint))
                .CreateClient();

            // 1. Stop and Remove the existing container
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters());
            var existingContainer = containers.FirstOrDefault(c => c.Names.Contains("/" + containerName));

            if (existingContainer is null)
            {
                Serilog.Log.Warning("BadRequest: Container with name {0} does not exist", containerName);
                return Results.BadRequest(new { error = $"Container with name {containerName} does not exist" });
            }

            try
            {
                ContainerStatsResponse? capturedStats = null;
                var progress = new Progress<ContainerStatsResponse>(statsReport =>
                {
                    capturedStats = statsReport;
                });

                await client.Containers.GetContainerStatsAsync(
                    existingContainer.ID,
                    new ContainerStatsParameters { Stream = false }, // Key for single stats response
                    progress,
                    cT);

                if (capturedStats is null)
                {
                    Serilog.Log.Warning("Container stats for {ContainerName} were not captured.", containerName);
                    return Results.Problem($"Failed to retrieve stats for container {containerName}.", statusCode: (int)System.Net.HttpStatusCode.InternalServerError);
                }

                object result = new
                {
                    cpu = CalculateCpuUsagePercentage(capturedStats) + "%",
                    memory = Math.Round(capturedStats.MemoryStats.Usage / 1000000.0, 3) + "mb",
                    uptime = existingContainer.Status
                };

                return Results.Ok(result);
            }
            catch (DockerApiException ex)
            {
                Serilog.Log.Error(ex, "DockerApiException on container {ContainerName}", containerName);
                return Results.Problem(ex.Message, statusCode: (int)System.Net.HttpStatusCode.InternalServerError, title: "Docker API Error");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Exception on container {ContainerName}", containerName);
                return Results.Problem(ex.Message, statusCode: (int)System.Net.HttpStatusCode.InternalServerError, title: "Internal Server Error");
            }
        });
    }
    private static double CalculateCpuUsagePercentage(ContainerStatsResponse stats)
    {
        if (stats?.CPUStats?.CPUUsage == null || stats.PreCPUStats?.CPUUsage == null)
        {
            return 0.0; // Not enough data
        }

        double cpuDelta = (double)stats.CPUStats.CPUUsage.TotalUsage - stats.PreCPUStats.CPUUsage.TotalUsage;
        double systemDelta = (double)stats.CPUStats.SystemUsage - stats.PreCPUStats.SystemUsage;

        if (systemDelta <= 0.0 || cpuDelta <= 0.0) return 0.0;

        double onlineCpus = stats.CPUStats.OnlineCPUs;
        if (onlineCpus == 0.0 && stats.CPUStats.CPUUsage.PercpuUsage != null) onlineCpus = stats.CPUStats.CPUUsage.PercpuUsage.Count;
        if (onlineCpus == 0.0) onlineCpus = 1; // Avoid division by zero if systemDelta is huge, or if no CPU info

        return Math.Round((cpuDelta / systemDelta) * onlineCpus * 100.0, 3);
    }
}
