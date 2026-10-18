# syntax=docker/dockerfile:1.7

FROM node:26-alpine AS build
WORKDIR /web

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci

COPY src/frontend/ ./
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
ARG VITE_PUBLIC_APP_ORIGIN
ENV VITE_PUBLIC_APP_ORIGIN=${VITE_PUBLIC_APP_ORIGIN}
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
COPY deploy/nginx/frontend.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/ /usr/share/nginx/html/
EXPOSE 8080
