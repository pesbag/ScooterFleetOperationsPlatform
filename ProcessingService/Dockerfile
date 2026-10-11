FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY ProcessingService/ProcessingService.csproj ProcessingService/
RUN dotnet restore ProcessingService/ProcessingService.csproj
COPY ProcessingService/ ProcessingService/
RUN dotnet publish ProcessingService/ProcessingService.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProcessingService.dll"]
