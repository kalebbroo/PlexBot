# Build using the .NET SDK on Alpine
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy and restore dependencies
COPY ["PlexBot.csproj", "./"]
RUN dotnet restore "PlexBot.csproj"

# Copy source files and build
COPY . .
RUN dotnet publish "PlexBot.csproj" -c Release -o /app/publish

# Create the runtime image using the .NET runtime on Alpine
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine3.18 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "PlexBot.dll"]
