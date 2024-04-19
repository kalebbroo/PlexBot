# Use the .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:latest AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY ["PlexBot.csproj", "./"]
RUN dotnet restore "PlexBot.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "PlexBot.csproj" -c Release -o /app/build

# Publish the project
FROM build AS publish
RUN dotnet publish "PlexBot.csproj" -c Release -o /app/publish

# Build the runtime image
FROM mcr.microsoft.com/dotnet/runtime:latest AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PlexBot.dll"]
