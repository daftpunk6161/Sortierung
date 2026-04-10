FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Romulus.Api/Romulus.Api.csproj", "src/Romulus.Api/"]
COPY ["src/Romulus.Contracts/Romulus.Contracts.csproj", "src/Romulus.Contracts/"]
COPY ["src/Romulus.Core/Romulus.Core.csproj", "src/Romulus.Core/"]
COPY ["src/Romulus.Infrastructure/Romulus.Infrastructure.csproj", "src/Romulus.Infrastructure/"]
RUN dotnet restore "src/Romulus.Api/Romulus.Api.csproj"

COPY . .
RUN dotnet publish "src/Romulus.Api/Romulus.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish/ ./
COPY data/ /app/data/

RUN mkdir -p /app/.romulus \
    && touch /app/.portable

ENV ROMULUS_DATA_DIR=/app/data
EXPOSE 7878

ENTRYPOINT ["dotnet", "Romulus.Api.dll"]
