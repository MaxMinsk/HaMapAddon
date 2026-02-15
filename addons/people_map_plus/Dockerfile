FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

WORKDIR /src
COPY src/PeopleMapPlus.Addon.csproj ./
RUN dotnet restore PeopleMapPlus.Addon.csproj

COPY src/. ./
RUN dotnet publish PeopleMapPlus.Addon.csproj \
  -c Release \
  -o /app/publish \
  --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine

WORKDIR /app
EXPOSE 8099
ENV ASPNETCORE_URLS=http://0.0.0.0:8099

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "PeopleMapPlus.Addon.dll"]
