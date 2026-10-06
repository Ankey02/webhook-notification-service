FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/WebhookService.Api/WebhookService.Api.csproj src/WebhookService.Api/
RUN dotnet restore src/WebhookService.Api/WebhookService.Api.csproj
COPY src/ src/
RUN dotnet publish src/WebhookService.Api/WebhookService.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebhookService.Api.dll"]
