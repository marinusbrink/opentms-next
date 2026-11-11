# Image for the Cloud Run job opentms-performance (docs/designs/76-ci-split-and-performance-gate.md): the volume tests of
# OpenTms.EntityFrameworkCore.Tests with their budgets enforced, against the database of the environment the job runs in.
# The deploy workflow builds the test project into ./perf first. The SDK image, because the tests run through dotnet test.
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /tests
COPY --chown=$APP_UID perf/ ./
ENV DOTNET_CLI_HOME=/tmp DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
USER $APP_UID
ENTRYPOINT ["dotnet", "test", "OpenTms.EntityFrameworkCore.Tests.dll", "--filter", "Category=Volume", "--logger", "console;verbosity=detailed", "--results-directory", "/tmp/results"]
