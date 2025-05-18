using Docker.DotNet;
using Docker.DotNet.Models;

namespace DockerApi;

public static class RestartEndpoint
{
    public static void MapRestartEndpoint(this WebApplication app)
    {
        app.MapGet("/docker/restart/{ContainerName}", async (string containerName) =>
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

            try
            {
                // 1. Stop and Remove the existing container
                var containers = await client.Containers.ListContainersAsync(new ContainersListParameters());
                var existingContainer = containers.FirstOrDefault(c => c.Names.Contains("/" + containerName));

                //Check if the image exists
                if (existingContainer is null)
                {
                    Serilog.Log.Error(new DockerContainerNotFoundException(System.Net.HttpStatusCode.NotFound, $"Container {containerName} not found."), "DockerContainerNotFoundException");
                    throw new DockerContainerNotFoundException(System.Net.HttpStatusCode.NotFound, $"Container {containerName} not found.");
                }

                await client.Containers.RestartContainerAsync(existingContainer.ID, new ContainerRestartParameters());

                return Results.Ok(new { message = $"Container {containerName} restarted successfully." });
            }
            catch (DockerApiException ex)
            {
                Serilog.Log.Error(ex, "DockerApiException");
                return Results.Problem(ex.Message, statusCode: (int)System.Net.HttpStatusCode.InternalServerError, title: "Docker API Error");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Exception");
                return Results.Problem(ex.Message, statusCode: (int)System.Net.HttpStatusCode.InternalServerError, title: "Internal Server Error");
            }
        });
    }
    
}
