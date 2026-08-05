FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PymeCore/PymeCore.csproj PymeCore/
RUN dotnet restore PymeCore/PymeCore.csproj

COPY PymeCore/. PymeCore/
RUN dotnet publish PymeCore/PymeCore.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PymeCore.dll"]
