# 배포용 (이 PC에서는 실행하지 않음)
FROM node:22-slim AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY tsconfig.json tsconfig.build.json ./
COPY src ./src
RUN npm run build

FROM node:22-slim
ENV NODE_ENV=production
WORKDIR /app
COPY package*.json ./
RUN npm ci --omit=dev
COPY --from=build /app/dist ./dist
COPY migrations ./migrations
COPY data ./data
EXPOSE 3000
# 마이그레이션을 적용한 뒤 서버를 시작한다
CMD ["sh", "-c", "node dist/db/migrate.js up && node dist/server.js"]
