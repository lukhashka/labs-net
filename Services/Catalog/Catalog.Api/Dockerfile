# Lab 10: multi-stage image of catalog-api. Build context: the root of the repository
#   docker build -t catalog-api:v1 -f Services/Catalog/Catalog.Api/Dockerfile .

# Stage 1: build in the SDK image (~800 MB). First only the project files and restore: this layer is cached,
# so a change of the code does not download the packages again.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Services/Catalog/Catalog.Api/Catalog.Api.csproj", "Services/Catalog/Catalog.Api/"]
COPY ["Services/Catalog/Catalog.Bll/Catalog.Bll.csproj", "Services/Catalog/Catalog.Bll/"]
COPY ["Services/Catalog/Catalog.Dal/Catalog.Dal.csproj", "Services/Catalog/Catalog.Dal/"]
COPY ["Services/Catalog/Catalog.Domain/Catalog.Domain.csproj", "Services/Catalog/Catalog.Domain/"]
COPY ["Platform.ServiceDefaults/Platform.ServiceDefaults.csproj", "Platform.ServiceDefaults/"]
COPY ["Platform.EventBus/Platform.EventBus.csproj", "Platform.EventBus/"]
RUN dotnet restore "Services/Catalog/Catalog.Api/Catalog.Api.csproj"

COPY . .
WORKDIR "/src/Services/Catalog/Catalog.Api"
RUN dotnet build "Catalog.Api.csproj" -c Release -o /app/build --no-restore

# Stage 2: publish
FROM build AS publish
RUN dotnet publish "Catalog.Api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Stage 3: the final image contains only the ASP.NET Core runtime (~200 MB) and the published files
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
USER $APP_UID
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Catalog.Api.dll"]
