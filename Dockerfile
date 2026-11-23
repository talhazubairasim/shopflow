# Builds any ShopFlow .NET service:  docker build --build-arg PROJECT=src/Services/Catalog/Catalog.Api -t shopflow-catalog .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish "$PROJECT" -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
RUN printf '#!/bin/sh\nexec dotnet /app/%s.dll\n' "$(basename "$PROJECT")" > /app/run.sh && chmod +x /app/run.sh
USER app
EXPOSE 8080
ENTRYPOINT ["/app/run.sh"]
