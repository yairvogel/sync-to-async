# Use the .NET 9.0 SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["echo.csproj", "./"]
RUN dotnet restore "./echo.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "echo.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "echo.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the .NET 9.0 runtime as the final image
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "echo.dll"]