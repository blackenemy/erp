FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Shared/Shared.csproj Shared/
COPY PricingService/PricingService.csproj PricingService/
RUN dotnet restore PricingService/PricingService.csproj
COPY Shared/ Shared/
COPY PricingService/ PricingService/
RUN dotnet publish PricingService/PricingService.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
ENTRYPOINT ["dotnet", "PricingService.dll"]
