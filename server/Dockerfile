# 배포용 (이 PC에서는 실행하지 않음). 마이그레이션은 CMD에서 하지 않는다: ops/README.md 의 배포 절차를 따른다.
FROM node:22-slim AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY tsconfig.json tsconfig.build.json ./
COPY src ./src
RUN npm run build

FROM node:22-slim
ENV NODE_ENV=production
# 컨테이너 메모리 제한(compose의 mem_limit 1g)의 약 70%. 제한을 바꾸면 함께 바꾼다
ENV NODE_OPTIONS=--max-old-space-size=700
WORKDIR /app
COPY --chown=node:node package*.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY --from=build --chown=node:node /app/dist ./dist
COPY --chown=node:node migrations ./migrations
COPY --chown=node:node data ./data
USER node
EXPOSE 3000
# slim 이미지에는 curl이 없다. node로 /health/ready 를 확인한다
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:'+(process.env.PORT||3000)+'/health/ready').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
# PID 1이 node여야 SIGTERM이 전달돼 정상 종료(bye, 채팅 큐 비우기, 풀 닫기)가 실행된다(F1). compose의 init: true 가 신호 전달·좀비 정리를 맡는다
CMD ["node", "dist/server.js"]
