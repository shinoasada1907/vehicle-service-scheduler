FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Scheduler.Domain/Scheduler.Domain.csproj src/Scheduler.Domain/
COPY src/Scheduler.Application/Scheduler.Application.csproj src/Scheduler.Application/
COPY src/Scheduler.Infrastructure/Scheduler.Infrastructure.csproj src/Scheduler.Infrastructure/
COPY src/Scheduler.Api/Scheduler.Api.csproj src/Scheduler.Api/
RUN dotnet restore src/Scheduler.Api/Scheduler.Api.csproj
COPY src/ src/
RUN dotnet publish src/Scheduler.Api/Scheduler.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Scheduler.Api.dll"]
