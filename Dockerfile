# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["BinanceTelegramBot.csproj", "./"]
RUN dotnet restore "BinanceTelegramBot.csproj"

COPY . .
RUN dotnet publish "BinanceTelegramBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "BinanceTelegramBot.dll"]
