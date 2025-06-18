# STAGE 1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
RUN apt-get update && apt-get install -y iputils-ping
WORKDIR /src

ARG SERVICE

COPY SharedLib/SharedLib.csproj SharedLib/
COPY ${SERVICE}/${SERVICE}.csproj ${SERVICE}/
RUN dotnet restore ${SERVICE}/${SERVICE}.csproj

COPY . .
RUN dotnet publish ${SERVICE}/${SERVICE}.csproj -c Release -o /app

# STAGE 2
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "REPLACE_ME"]
