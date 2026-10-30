FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS final
# Loads nginx's scripting module, and lets it read the secret from the environment so it is never written to disk.
RUN sed -i -e '1i load_module modules/ngx_http_js_module.so;' -e '1i env HMAC_WEB_SECRET;' /etc/nginx/nginx.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY sign.js /etc/nginx/njs/sign.js
COPY --from=build /app/dist /usr/share/nginx/html
