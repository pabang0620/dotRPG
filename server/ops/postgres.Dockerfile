# WAL 아카이브(PITR)용: 공식 postgres:16 이미지에는 pgbackrest가 없어 위에 설치한다(phase7_ops.md 7.2 B).
# 설정(/etc/pgbackrest/pgbackrest.conf)과 저장소 자격 증명은 서버 .env/볼륨에서 주입한다. 이 저장소에는 넣지 않는다.
FROM postgres:16
RUN apt-get update \
 && apt-get install -y --no-install-recommends pgbackrest \
 && rm -rf /var/lib/apt/lists/*
