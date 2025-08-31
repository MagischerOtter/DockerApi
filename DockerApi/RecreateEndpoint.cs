using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Serilog;

namespace DockerApi;

public static class RecreateEndpoint
{
    public static void MapRecreateEndpoint(this WebApplication app)
    {
        app.MapGet("/docker/recreate/{ContainerName}", async (string containerName) =>
        {
            if (string.IsNullOrEmpty(containerName))
            {
                Log.Warning("BadRequest: ContainerName is required");
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
                    Log.Error(new DockerContainerNotFoundException(HttpStatusCode.NotFound, $"Container {containerName} not found."), "DockerContainerNotFoundException");
                    throw new DockerContainerNotFoundException(HttpStatusCode.NotFound, $"Container {containerName} not found.");
                }

                var inspection = await client.Containers.InspectContainerAsync(existingContainer.ID);

                int lastIndex = existingContainer.Image.AsSpan().LastIndexOf(':');

                if (lastIndex == -1)
                {
                    lastIndex = existingContainer.Image.AsSpan().Length;
                }

                string imageName = existingContainer.Image.AsSpan()[..lastIndex].ToString();
                string imageTag = "latest";
                string imageNameTag = imageName + ":" + imageTag;

                AuthConfig? authConfig = null;

                if (imageNameTag.StartsWith("ghcr.io"))
                {
                    authConfig = new();
                    authConfig.ServerAddress = "ghcr.io";
                    authConfig.Username = Environment.GetEnvironmentVariable("GHCR_USERNAME");
                    authConfig.Password = Environment.GetEnvironmentVariable("GHCR_PASSWORD");
                }

                Log.Information("Pulling image: " + imageNameTag);

                await client.Images.CreateImageAsync(new ImagesCreateParameters
                {
                    FromImage = imageName,
                    Tag = imageTag
                }, authConfig, new Progress<JSONMessage>());

                var portBindings = existingContainer.GetPortBindings();
                var exposedPorts = existingContainer.GetExposedPorts();

                // 3. Create a new container with the latest image, including ports, volumes, and labels
                var containerCreateParams = new CreateContainerParameters
                {
                    Image = imageNameTag,
                    Name = containerName,
                    ExposedPorts = exposedPorts,
                    Env = inspection.Config.Env,
                    HostConfig = new HostConfig
                    {
                        Binds = existingContainer.Mounts?.Select(m => $"{m.Source}:{m.Destination}").ToList(),
                        PortBindings = portBindings
                    },
                    Labels = existingContainer.Labels // Copy labels
                };

                await client.Containers.StopContainerAsync(existingContainer.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 });
                await client.Containers.RemoveContainerAsync(existingContainer.ID, new ContainerRemoveParameters { Force = true });

                var createdContainer = await client.Containers.CreateContainerAsync(containerCreateParams);

                // 4. Start the new container
                await client.Containers.StartContainerAsync(createdContainer.ID, new ContainerStartParameters());

                return Results.Ok(new { message = $"Container {containerName} recreated successfully." });
            }
            catch (DockerApiException ex)
            {
                Log.Error(ex, "DockerApiException");
                return Results.Problem(ex.Message, statusCode: (int)HttpStatusCode.InternalServerError, title: "Docker API Error");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception");
                return Results.Problem(ex.Message, statusCode: (int)HttpStatusCode.InternalServerError, title: "Internal Server Error");
            }
        });
    }
}
