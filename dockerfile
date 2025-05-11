# Stage 1: Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY *.sln .
COPY DockerApi/*.csproj ./DockerApi/
RUN dotnet restore

# Copy everything else and build
COPY DockerApi/. ./DockerApi/
WORKDIR /app/DockerApi
RUN dotnet publish -c Release -o /app/out

# Stage 2: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build-env /app/out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DockerApi.dll"]
