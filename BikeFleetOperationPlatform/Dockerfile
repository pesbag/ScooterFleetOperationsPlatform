FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY BikeFleetOperationPlatform/BikeFleetOperationPlatform.csproj BikeFleetOperationPlatform/
RUN dotnet restore BikeFleetOperationPlatform/BikeFleetOperationPlatform.csproj
COPY BikeFleetOperationPlatform/ BikeFleetOperationPlatform/
RUN dotnet publish BikeFleetOperationPlatform/BikeFleetOperationPlatform.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "BikeFleetOperationPlatform.dll"]
