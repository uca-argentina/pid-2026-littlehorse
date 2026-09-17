FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DrinkIt.slnx Directory.Packages.props Directory.Build.props ./
COPY src/DrinkIt.Domain/DrinkIt.Domain.csproj src/DrinkIt.Domain/
COPY src/DrinkIt.Application/DrinkIt.Application.csproj src/DrinkIt.Application/
COPY src/DrinkIt.Infrastructure/DrinkIt.Infrastructure.csproj src/DrinkIt.Infrastructure/
COPY src/DrinkIt.Api/DrinkIt.Api.csproj src/DrinkIt.Api/
RUN dotnet restore src/DrinkIt.Api/DrinkIt.Api.csproj

COPY src/ src/
RUN dotnet publish src/DrinkIt.Api/DrinkIt.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Non-root: same reasoning as any other production container.
USER app

COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "DrinkIt.Api.dll"]
