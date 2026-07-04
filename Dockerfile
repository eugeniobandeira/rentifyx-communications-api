FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["01-aspire/02-ServiceDefaults/RentifyxCommunications.ServiceDefaults/RentifyxCommunications.ServiceDefaults.csproj", "01-aspire/02-ServiceDefaults/RentifyxCommunications.ServiceDefaults/"]
COPY ["02-src/03-Domain/RentifyxCommunications.Domain/RentifyxCommunications.Domain.csproj", "02-src/03-Domain/RentifyxCommunications.Domain/"]
COPY ["02-src/02-Application/RentifyxCommunications.Application/RentifyxCommunications.Application.csproj", "02-src/02-Application/RentifyxCommunications.Application/"]
COPY ["02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/RentifyxCommunications.Infrastructure.csproj", "02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/"]
COPY ["02-src/04-IoC/RentifyxCommunications.IoC/RentifyxCommunications.IoC.csproj", "02-src/04-IoC/RentifyxCommunications.IoC/"]
COPY ["02-src/01-Api/RentifyxCommunications.Api/RentifyxCommunications.Api.csproj", "02-src/01-Api/RentifyxCommunications.Api/"]

RUN dotnet restore "02-src/01-Api/RentifyxCommunications.Api/RentifyxCommunications.Api.csproj"

COPY . .

RUN dotnet publish "02-src/01-Api/RentifyxCommunications.Api/RentifyxCommunications.Api.csproj" \
    --no-restore \
    --configuration Release \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "RentifyxCommunications.Api.dll"]
