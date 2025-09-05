# Use the official .NET 8.0 runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Use the .NET 8.0 SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["sync-to-async.csproj", "./"]
RUN dotnet restore "./sync-to-async.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "sync-to-async.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "sync-to-async.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "sync-to-async.dll"]