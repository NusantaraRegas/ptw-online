FROM node:22.23-alpine AS build
WORKDIR /source
COPY src/web/package*.json ./
RUN npm ci
COPY src/web/ .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
COPY deploy/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --chmod=755 deploy/nginx/40-upload-limit.sh /docker-entrypoint.d/40-upload-limit.sh
COPY --from=build /source/dist/web/browser /usr/share/nginx/html
EXPOSE 8080
