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

    container {
      name   = "keycloak"
      image  = "mcr.microsoft.com/dotnet/samples:aspnetapp"
      cpu    = 0.25
      memory = "0.5Gi"
    }

    container {
      name   = "otel-collector"
      image  = "otel/opentelemetry-collector-contrib:0.136.0"
      cpu    = 0.25
      memory = "0.5Gi"

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