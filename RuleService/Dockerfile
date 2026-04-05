FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Shared/Shared.csproj Shared/
COPY RuleService/RuleService.csproj RuleService/
RUN dotnet restore RuleService/RuleService.csproj
COPY Shared/ Shared/
COPY RuleService/ RuleService/
RUN dotnet publish RuleService/RuleService.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
COPY data/rules.json /app/seed-rules.json
COPY RuleService/init-rules.sh /app/init-rules.sh
RUN chmod +x /app/init-rules.sh
ENV ASPNETCORE_URLS=http://+:5002
EXPOSE 5002
ENTRYPOINT ["/bin/sh", "-c", "/app/init-rules.sh && dotnet RuleService.dll"]
