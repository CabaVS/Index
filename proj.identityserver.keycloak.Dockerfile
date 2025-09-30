# Stage 1: Build Keycloak runtime
FROM quay.io/keycloak/keycloak:26.3.5 AS builder

ENV KC_DB=mssql

RUN /opt/keycloak/bin/kc.sh build

# Stage 2: Runtime container
FROM quay.io/keycloak/keycloak:26.3.5

COPY --from=builder /opt/keycloak/ /opt/keycloak/

ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start", "--optimized"]