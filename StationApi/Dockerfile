FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY StationApi/StationApi.csproj StationApi/
RUN dotnet restore StationApi/StationApi.csproj
COPY StationApi/ StationApi/
RUN dotnet publish StationApi/StationApi.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StationApi.dll"]
