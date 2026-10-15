FROM node:22-alpine AS frontend
WORKDIR /src
COPY WebClient/package*.json ./WebClient/
RUN npm ci
COPY WebClient ./WebClient
COPY VideoSubtitleTranslator ./VideoSubtitleTranslator
WORKDIR /src/WebClient
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=frontend /src/VideoSubtitleTranslator/Web ./VideoSubtitleTranslator/Web
RUN dotnet publish VideoSubtitleTranslator/VideoSubtitleTranslator.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg python3 python3-pip fonts-noto-cjk && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN pip3 install --no-cache-dir -r /app/PythonScripts/requirements.txt
ENV VST_BIND_ADDRESS=0.0.0.0
EXPOSE 5155
ENTRYPOINT ["dotnet", "VideoSubtitleTranslator.dll", "--web"]
