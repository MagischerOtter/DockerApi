using CliWrap;
using Docker.DotNet;
using Docker.DotNet.Models;
using Serilog;
using System.Net;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(context.Configuration).CreateLogger();
});

// Helper method to determine the correct Docker API endpoint
static string GetDockerApiEndpoint()
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}

// Map the endpoint
app.MapGet("/docker/recreate/{ContainerName}", async (string containerName) =>
{
    if (string.IsNullOrEmpty(containerName))
    {
        Log.Warning("BadRequest: ContainerName is required");
        return Results.BadRequest(new { error = "ContainerName is required." });
    }

    // Get the Docker API endpoint
    string dockerApiEndpoint = GetDockerApiEndpoint();

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
        int lastIndex = existingContainer.Image.AsSpan().LastIndexOf(':');
        string imageName = existingContainer.Image.AsSpan()[..lastIndex].ToString();
        string imageTag = ":latest";
        string imageNameTag = imageName + imageTag;

        Log.Information("Pulling image: " + imageNameTag);

        await Cli.Wrap("docker").WithArguments(["pull", imageNameTag]).ExecuteAsync();

        // await client.Images.CreateImageAsync(new ImagesCreateParameters
        // {
        //     FromImage = imageName,
        //     Tag = imageTag
        // }, null, new Progress<JSONMessage>());

        var portBindings = GetPortBindings(existingContainer);
        var exposedPorts = GetExposedPorts(existingContainer);
        
        // 3. Create a new container with the latest image, including ports, volumes, and labels
        var containerCreateParams = new CreateContainerParameters
        {
            Image = imageNameTag,
            Name = containerName,
            ExposedPorts = exposedPorts,
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

app.Run();


IDictionary<string, IList<PortBinding>> GetPortBindings(ContainerListResponse container)
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

IDictionary<string, EmptyStruct> GetExposedPorts(ContainerListResponse container)
{
    var dic = new Dictionary<string, EmptyStruct>();
    var groups = container.Ports?.GroupBy(p => p.PrivatePort.ToString() + "/" + p.Type) ?? [];

    foreach (var g in groups)
    {
        dic.TryAdd(g.Key, new EmptyStruct());
    }

    return dic;
}