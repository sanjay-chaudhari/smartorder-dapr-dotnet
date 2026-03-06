FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 5005

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/WorkflowOrchestrator/WorkflowOrchestrator.csproj", "src/WorkflowOrchestrator/"]
RUN dotnet restore "src/WorkflowOrchestrator/WorkflowOrchestrator.csproj"
COPY . .
RUN dotnet publish "src/WorkflowOrchestrator/WorkflowOrchestrator.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WorkflowOrchestrator.dll"]
