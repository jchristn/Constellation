#
#
# Build from project root:
#   docker buildx build --platform linux/amd64,linux/arm64/v8 --tag jchristn77/constellation:v1.0.0 --push .
#
#

#
#
# Build stage
#
#
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy source
COPY src/ .

# Restore dependencies
RUN dotnet restore "Constellation.ControllerServer/Constellation.ControllerServer.csproj"

# Build the application
WORKDIR /src/Constellation.ControllerServer
RUN dotnet build "Constellation.ControllerServer.csproj" -c Release -f net10.0 -o /app/build /p:GeneratePackageOnBuild=false

#
#
# Publish stage
#
#
FROM build AS publish
RUN dotnet publish "Constellation.ControllerServer.csproj" -c Release -f net10.0 -o /app/publish /p:UseAppHost=false /p:GeneratePackageOnBuild=false /p:ErrorOnDuplicatePublishOutputFiles=false


#
#
# Runtime stage
#
#
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS final
RUN apt-get update && apt-get install -y iputils-ping traceroute net-tools curl wget dnsutils iproute2 file vim procps && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8000
ENTRYPOINT ["dotnet", "Constellation.ControllerServer.dll"]
