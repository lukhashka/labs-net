# Lab 10: multi-stage image of the YARP gateway. Build context: the root of the repository
#   docker build -t gateway:v1 -f Platform.ApiGateway/Dockerfile .

# Stage 1: build; the project files and restore first, so the packages layer is cached
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Platform.ApiGateway/Platform.ApiGateway.csproj", "Platform.ApiGateway/"]
COPY ["Platform.ServiceDefaults/Platform.ServiceDefaults.csproj", "Platform.ServiceDefaults/"]
RUN dotnet restore "Platform.ApiGateway/Platform.ApiGateway.csproj"

COPY . .
WORKDIR "/src/Platform.ApiGateway"
RUN dotnet build "Platform.ApiGateway.csproj" -c Release -o /app/build --no-restore

# Stage 2: publish
FROM build AS publish
RUN dotnet publish "Platform.ApiGateway.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Stage 3: runtime only; the image listens on port 8080
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
USER $APP_UID
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Platform.ApiGateway.dll"]
