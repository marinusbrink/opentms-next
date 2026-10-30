# The SPA on Cloud Run (docs/designs/gcp-infrastructure.md). VITE_API_URL is fixed at build time, so the
# deploy workflow builds one image per environment.
FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
ARG VITE_API_URL
ARG VITE_AG_GRID_LICENSE_KEY=""
ENV VITE_API_URL=$VITE_API_URL VITE_AG_GRID_LICENSE_KEY=$VITE_AG_GRID_LICENSE_KEY
RUN test -n "$VITE_API_URL" && npx vite build

FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080
