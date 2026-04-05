module.exports = {
  apps: [
    {
      name: "hubs-backend-rule",
      script: "/usr/bin/dotnet",
      args: "RuleService/RuleService.dll",
      cwd: "/var/www/hubs-backend",
      env: {
        ASPNETCORE_ENVIRONMENT: "Production",
        ASPNETCORE_URLS: "http://+:5002",
        CORS_ORIGINS: "http://localhost:3000,https://yourdomain.com",
      },
      instances: 1,
      exec_mode: "fork",
      max_restarts: 5,
      min_uptime: "10s",
    },
    {
      name: "hubs-backend-pricing",
      script: "/usr/bin/dotnet",
      args: "PricingService/PricingService.dll",
      cwd: "/var/www/hubs-backend",
      env: {
        ASPNETCORE_ENVIRONMENT: "Production",
        ASPNETCORE_URLS: "http://+:5001",
        RuleServiceUrl: "http://localhost:5002",
        CORS_ORIGINS:
          "http://localhost:3000,https://epr-front-end.project-hub.it.com",
      },
      instances: 1,
      exec_mode: "fork",
      max_restarts: 5,
      min_uptime: "10s",
    },
  ],
};
