# Runtime image for Cloud Run (docs/designs/gcp-infrastructure.md). The deploy workflow publishes first
# (ReadyToRun, linux-x64, login-page libs installed) into ./publish; this image only carries the result.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenTms.HttpApi.Host.dll"]
