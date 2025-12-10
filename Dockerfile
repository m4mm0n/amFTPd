# amFTPd Dockerfile
#
# Multi-stage build: build the amFTPd server and run it on a slim .NET runtime.
# Assumes the solution layout has an `amFTPd` project (amFTPd/amFTPd.csproj).

# ---- Build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything and restore/publish
COPY . .
RUN dotnet restore ./amFTPd/amFTPd.csproj
RUN dotnet publish ./amFTPd/amFTPd.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

# Copy published files
COPY --from=build /app/publish .

# Create mount points for configuration and data
VOLUME ["/config", "/data"]

# Expose control port and passive range (match your config)
EXPOSE 2121/tcp
EXPOSE 50000-50100/tcp

# By default, amFTPd expects a config file path as the first argument.
# We default to /config/amftpd.json, which you can override at runtime.
ENTRYPOINT ["dotnet", "amFTPd.dll"]
CMD ["/config/amftpd.json"]
