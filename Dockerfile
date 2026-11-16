ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb
ARG ASPNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet restore backend/src/RealEstate.Web/RealEstate.Web.csproj
RUN dotnet publish backend/src/RealEstate.Web/RealEstate.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /publish \
    /p:UseAppHost=false

FROM ${ASPNET_RUNTIME_IMAGE} AS runtime
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    HOME=/home/app
WORKDIR /app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys /tmp \
    && chown -R 1654:1654 /home/app /app /tmp
COPY --from=build --chown=1654:1654 /publish/ ./
USER 1654:1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "RealEstate.Web.dll"]
