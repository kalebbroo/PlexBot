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

# Install ICU libraries for globalization support
RUN apk add --no-cache icu-libs

# Set environment variable to enable globalization
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV LC_ALL=en_US.UTF-8
ENV LANG=en_US.UTF-8

WORKDIR /app
COPY --from=build /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "PlexBot.dll"]
