FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/FluxoPro.Api/FluxoPro.Api.csproj src/FluxoPro.Api/
RUN dotnet restore src/FluxoPro.Api/FluxoPro.Api.csproj

COPY src/FluxoPro.Api/ src/FluxoPro.Api/
RUN dotnet publish src/FluxoPro.Api/FluxoPro.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

ENTRYPOINT ["dotnet", "FluxoPro.Api.dll"]
