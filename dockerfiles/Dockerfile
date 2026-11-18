FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["CadeMinhaConsulta.slnx", "./"]
COPY ["src/CadeMinhaConsulta.Api/CadeMinhaConsulta.Api.csproj", "src/CadeMinhaConsulta.Api/"]
COPY ["tests/CadeMinhaConsulta.InfrastructureTests/CadeMinhaConsulta.InfrastructureTests.csproj", "tests/CadeMinhaConsulta.InfrastructureTests/"]

RUN dotnet restore "CadeMinhaConsulta.slnx"

COPY . .
RUN dotnet publish "src/CadeMinhaConsulta.Api/CadeMinhaConsulta.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CadeMinhaConsulta.Api.dll"]