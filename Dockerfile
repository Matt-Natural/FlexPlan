# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY FlexPlan.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

# ---- run ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

# TZ makes "now" match your local time (change it if you live elsewhere).
ENV TZ=Asia/Manila \
    FLEXPLAN_BEHIND_PROXY=true \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Hosts such as Render tell the app which port to use through $PORT.
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet FlexPlan.dll"]
