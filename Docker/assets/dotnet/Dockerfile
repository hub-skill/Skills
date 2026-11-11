# Imagem de produção para APIs ASP.NET Core geradas pela skill criar-api (template assets/dotnet).
# Build (na raiz da solução):  docker build -t projeto-api:0.1.0 .
# Rodar:                       docker run --rm -p 8080:8080 --env-file .env projeto-api:0.1.0
# Troque ProjetoApi pelo nome do projeto. Tags fixas (nunca latest).

# SDK: as imagens oficiais trazem a faixa 10.0.1xx. O global.json do template (10.0.100 + rollForward
# latestFeature) aceita essa faixa; se fixar 10.0.4xx sem rollForward, o build falha dentro do container.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore primeiro: a camada só é refeita quando projetos/pacotes mudam.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ProjetoApi/ProjetoApi.csproj src/ProjetoApi/
RUN dotnet restore src/ProjetoApi/ProjetoApi.csproj
COPY src/ src/
RUN dotnet publish src/ProjetoApi/ProjetoApi.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled: sem shell, sem gerenciador de pacotes, usuário não root (app, UID 1654) por padrão.
# "-extra" traz ICU e tzdata: culturas pt-BR, fusos horários e drivers de banco que dependem deles.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
USER $APP_UID
EXPOSE 8080
# Sem HEALTHCHECK: a imagem chiseled não tem curl; quem verifica a saúde são as probes do Kubernetes.
ENTRYPOINT ["dotnet", "ProjetoApi.dll"]
