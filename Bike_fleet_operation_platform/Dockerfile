FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Bike_fleet_operation_platform/Bike_fleet_operation_platform.csproj Bike_fleet_operation_platform/
RUN dotnet restore Bike_fleet_operation_platform/Bike_fleet_operation_platform.csproj
COPY Bike_fleet_operation_platform/ Bike_fleet_operation_platform/
RUN dotnet publish Bike_fleet_operation_platform/Bike_fleet_operation_platform.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Bike_fleet_operation_platform.dll"]
