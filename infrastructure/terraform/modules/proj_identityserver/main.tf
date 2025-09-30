resource "azurerm_container_app" "ca_keycloak" {
  name                         = var.ca_name_for_keycloak
  container_app_environment_id = var.cae_id
  resource_group_name          = var.rg_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.uami_ca_keycloak.id]
  }

  ingress {
    allow_insecure_connections = false
    external_enabled           = true
    target_port                = 8080
    transport                  = "auto"

    traffic_weight {
      percentage      = 100
      label           = "primary"
      latest_revision = true
    }
  }

  lifecycle {
    ignore_changes = [
      template[0].container[0].env,
      template[0].container[0].image
    ]
  }

  registry {
    server   = var.acr_login_server
    identity = azurerm_user_assigned_identity.uami_ca_keycloak.id
  }

  secret {
    name  = "ai-connstr"
    value = var.appi_connection_string
  }

  template {
    min_replicas = 0
    max_replicas = 1

    volume {
      name         = "kc-logs"
      storage_type = "EmptyDir"
    }

    container {
      name   = "keycloak"
      image  = "mcr.microsoft.com/dotnet/samples:aspnetapp"
      cpu    = 0.25
      memory = "0.5Gi"

      volume_mounts {
        name = "kc-logs"
        path = "/var/log/keycloak"
      }
    }

    container {
      name   = "otel-collector"
      image  = "otel/opentelemetry-collector-contrib:0.136.0"
      cpu    = 0.25
      memory = "0.5Gi"

      volume_mounts {
        name = "kc-logs"
        path = "/var/log/keycloak"
      }

      env {
        name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        secret_name = "ai-connstr"
      }

      env {
        name  = "OTEL_CONFIG"
        value = <<-EOT
          receivers:
            otlp:
              protocols:
                grpc:
                  endpoint: 0.0.0.0:4317
                http:
                  endpoint: 0.0.0.0:4318
            prometheus:
              config:
                scrape_configs:
                  - job_name: keycloak-metrics
                    scrape_interval: 15s
                    static_configs:
                      - targets: ["localhost:9000"]
                    metrics_path: /metrics
            filelog:
              include: [ "/var/log/keycloak/keycloak.log" ]
              start_at: end
              operators:
                - id: parse_json
                  type: json_parser
                  parse_from: body
                  parse_to: body
                  on_error: drop
                - id: time_from_body
                  type: time_parser
                  parse_from: body.timestamp
                  layout_type: gotime
                  layout: '2006-01-02T15:04:05.999999999Z07:00'
                  on_error: send
                - id: severity_from_level
                  type: severity_parser
                  parse_from: body.level
                  mapping:
                    debug: [DEBUG]
                    info:  [INFO]
                    warn:  [WARN, WARNING]
                    error: [ERROR]
                    fatal: [FATAL]
                - id: attr_logger
                  type: copy
                  from: body.loggerName
                  to: attributes["logger.name"]
                - id: attr_thread
                  type: copy
                  from: body.threadName
                  to: attributes["thread.name"]
                - id: attr_pid
                  type: copy
                  from: body.processId
                  to: attributes["process.pid"]
                - id: attr_procname
                  type: copy
                  from: body.processName
                  to: attributes["process.command_line"]
                - id: msg_to_body
                  type: move
                  from: body.message
                  to: body
          processors:
            batch: {}
          exporters:
            azuremonitor: {}
          service:
            pipelines:
              traces:
                receivers: [otlp]
                processors: [batch]
                exporters: [azuremonitor]
              metrics:
                receivers: [prometheus]
                processors: [batch]
                exporters: [azuremonitor]
              logs:
                receivers: [filelog]
                processors: [batch]
                exporters: [azuremonitor]
        EOT
      }

      args = ["--config=env:OTEL_CONFIG"]
    }
  }
}

resource "azurerm_mssql_database" "db" {
  name                 = var.sql_database_name
  server_id            = var.sql_server_id
  sku_name             = "GP_S_Gen5_1"
  storage_account_type = "Local"

  auto_pause_delay_in_minutes = 15
  max_size_gb                 = 2
  min_capacity                = 0.5
  read_replica_count          = 0
  read_scale                  = false
  zone_redundant              = false

  collation = "SQL_Latin1_General_CP1_CI_AS"

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_user_assigned_identity" "uami_ca_keycloak" {
  name                = "uami-${var.ca_name_for_keycloak}"
  resource_group_name = var.rg_name
  location            = var.location
}

resource "azurerm_role_assignment" "acr_pull_for_aca_keycloak" {
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.uami_ca_keycloak.principal_id
}