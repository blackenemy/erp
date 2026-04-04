FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Shared/Shared.csproj Shared/
COPY RuleService/RuleService.csproj RuleService/
RUN dotnet restore RuleService/RuleService.csproj
COPY Shared/ Shared/
COPY RuleService/ RuleService/
RUN dotnet publish RuleService/RuleService.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5002
EXPOSE 5002
ENTRYPOINT ["dotnet", "RuleService.dll"]
