FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Only copy the projects the API actually depends on for restore — the test
# projects are excluded from the build context entirely (see .dockerignore),
# since they're irrelevant to building/running the service itself.
COPY NovaWallet.sln ./
COPY src/NovaWallet.Domain/NovaWallet.Domain.csproj src/NovaWallet.Domain/
COPY src/NovaWallet.Application/NovaWallet.Application.csproj src/NovaWallet.Application/
COPY src/NovaWallet.Infrastructure/NovaWallet.Infrastructure.csproj src/NovaWallet.Infrastructure/
COPY src/NovaWallet.Api/NovaWallet.Api.csproj src/NovaWallet.Api/

RUN dotnet restore src/NovaWallet.Api/NovaWallet.Api.csproj

COPY src/ src/
RUN dotnet publish src/NovaWallet.Api/NovaWallet.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]
