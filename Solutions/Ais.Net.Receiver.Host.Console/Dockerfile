# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
# aspnet (not runtime): the host references Ais.Net.Receiver.ServiceDefaults, which brings in the
# Microsoft.AspNetCore.App shared framework - so the app requires the ASP.NET Core runtime.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central Package Management: the version catalog must be restored alongside the project files.
COPY ["Directory.Packages.props", "./"]
COPY ["Ais.Net.Receiver.Host.Console/Ais.Net.Receiver.Host.Console.csproj", "Ais.Net.Receiver.Host.Console/"]
COPY ["Ais.Net.Receiver.ServiceDefaults/Ais.Net.Receiver.ServiceDefaults.csproj", "Ais.Net.Receiver.ServiceDefaults/"]
COPY ["Ais.Net.Receiver.Storage.Azure.Blob/Ais.Net.Receiver.Storage.Azure.Blob.csproj", "Ais.Net.Receiver.Storage.Azure.Blob/"]
COPY ["Ais.Net.Receiver/Ais.Net.Receiver.csproj", "Ais.Net.Receiver/"]
RUN dotnet restore "./Ais.Net.Receiver.Host.Console/Ais.Net.Receiver.Host.Console.csproj"
COPY . .
WORKDIR "/src/Ais.Net.Receiver.Host.Console"
RUN dotnet build "./Ais.Net.Receiver.Host.Console.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Stage an empty dead-letter directory to copy into the final (chiseled) image with app-user
# ownership - chiseled images have no shell/mkdir/chown to create it at runtime.
RUN mkdir -p /deadletter

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ais.Net.Receiver.Host.Console.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Dead-letter spill directory, owned by the non-root app user so a volume mounted here inherits that
# ownership and the receiver can persist/replay batches. Enable with Storage__deadLetterPath=/data/deadletter.
COPY --chown=$APP_UID:$APP_UID --from=build /deadletter /data/deadletter
ENTRYPOINT ["dotnet", "Ais.Net.Receiver.Host.Console.dll"]