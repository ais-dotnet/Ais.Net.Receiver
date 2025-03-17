# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Ais.Net.Receiver.Host.Console/Ais.Net.Receiver.Host.Console.csproj", "Ais.Net.Receiver.Host.Console/"]
COPY ["Ais.Net.Receiver.Storage.Azure.Blob/Ais.Net.Receiver.Storage.Azure.Blob.csproj", "Ais.Net.Receiver.Storage.Azure.Blob/"]
COPY ["Ais.Net.Receiver/Ais.Net.Receiver.csproj", "Ais.Net.Receiver/"]
RUN dotnet restore "./Ais.Net.Receiver.Host.Console/Ais.Net.Receiver.Host.Console.csproj"
COPY . .
WORKDIR "/src/Ais.Net.Receiver.Host.Console"
RUN dotnet build "./Ais.Net.Receiver.Host.Console.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ais.Net.Receiver.Host.Console.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ais.Net.Receiver.Host.Console.dll"]