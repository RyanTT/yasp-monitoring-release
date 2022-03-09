#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["src/YASP.Server/YASP.Server.csproj", "src/YASP.Server/"]
COPY ["src/YASP.Client/YASP.Client.csproj", "src/YASP.Client/"]
COPY ["src/YASP.Shared/YASP.Shared.csproj", "src/YASP.Shared/"]
COPY ["src/YASP.Client.Application/YASP.Client.Application.csproj", "src/YASP.Client.Application/"]
COPY ["src/YASP.Server.Application/YASP.Server.Application.csproj", "src/YASP.Server.Application/"]
RUN dotnet restore "src/YASP.Server/YASP.Server.csproj"
COPY . .
WORKDIR "/src/src/YASP.Server"
RUN dotnet build "YASP.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "YASP.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YASP.Server.dll"]