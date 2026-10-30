# Runtime image for the Cloud Run job opentms-migrator (docs/designs/gcp-infrastructure.md). The deploy
# workflow publishes into ./publish first.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "OpenTms.DbMigrator.dll"]
