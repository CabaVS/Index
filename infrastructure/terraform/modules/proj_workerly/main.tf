resource "azurerm_cosmosdb_sql_database" "db" {
  name                = "workerly"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
}

resource "azurerm_cosmosdb_sql_container" "users" {
  name                = "users"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/id"]

  unique_key {
    paths = ["/email"]
  }
}

resource "azurerm_cosmosdb_sql_container" "workspaces" {
  name                = "workspaces"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/id"]
}

resource "azurerm_cosmosdb_sql_container" "workspace_configs" {
  name                = "workspaceConfigs"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/workspaceId"]
}

resource "azurerm_cosmosdb_sql_container" "memberships" {
  name                = "memberships"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/workspaceId"]
}

resource "azurerm_cosmosdb_sql_container" "remaining_work_snapshots" {
  name                = "remainingWorkSnapshots"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/workspaceId"]
}

resource "azurerm_container_app" "ca_workerly_web" {
  name                         = var.ca_name_for_workerly_web
  container_app_environment_id = var.cae_id
  resource_group_name          = var.rg_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.uami_ca_workerly_web.id]
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
    identity = azurerm_user_assigned_identity.uami_ca_workerly_web.id
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "api"
      image  = "mcr.microsoft.com/dotnet/samples:aspnetapp"
      cpu    = 0.25
      memory = "0.5Gi"
    }
  }
}

resource "azurerm_user_assigned_identity" "uami_ca_workerly_web" {
  name                = "uami-${var.ca_name_for_workerly_web}"
  resource_group_name = var.rg_name
  location            = var.location
}

resource "azurerm_role_assignment" "acr_pull_for_aca_workerly_web" {
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.uami_ca_workerly_web.principal_id
}

resource "azurerm_role_assignment" "role_blob_reader" {
  scope                = var.configs_container_scope
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.uami_ca_workerly_web.principal_id
}

resource "azurerm_cosmosdb_sql_role_assignment" "cosmosdb_data_contributor_for_aca_workerly_web" {
  name                = "dd6178e6-61ee-9e37-532c-150176a03012"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name

  role_definition_id = "${var.cosmos_account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"

  scope        = "${var.cosmos_account_id}/dbs/${azurerm_cosmosdb_sql_database.db.name}"
  principal_id = azurerm_user_assigned_identity.uami_ca_workerly_web.principal_id
}

resource "azurerm_container_app_job" "caj_workerly_job_burndown" {
  name                         = var.caj_name_for_workerly_burndown
  container_app_environment_id = var.cae_id
  resource_group_name          = var.rg_name
  location                     = var.location

  replica_timeout_in_seconds = 300
  replica_retry_limit        = 0

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.uami_caj_workerly_burndown.id]
  }

  schedule_trigger_config {
    cron_expression          = "0 0 * * 2-6"
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "job"
      image  = "mcr.microsoft.com/k8se/quickstart-jobs:latest"
      cpu    = 0.25
      memory = "0.5Gi"
    }
  }

  registry {
    server   = var.acr_login_server
    identity = azurerm_user_assigned_identity.uami_caj_workerly_burndown.id
  }

  lifecycle {
    ignore_changes = [
      template[0].container[0].env,
      template[0].container[0].image
    ]
  }
}

resource "azurerm_user_assigned_identity" "uami_caj_workerly_burndown" {
  name                = "uami-${var.caj_name_for_workerly_burndown}"
  resource_group_name = var.rg_name
  location            = var.location
}

resource "azurerm_role_assignment" "acr_pull_for_caj_workerly_burndown" {
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.uami_caj_workerly_burndown.principal_id
}

resource "azurerm_role_assignment" "role_blob_reader_for_caj_workerly_burndown" {
  scope                = var.configs_container_scope
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.uami_caj_workerly_burndown.principal_id
}

resource "azurerm_cosmosdb_sql_role_assignment" "cosmosdb_data_contributor_for_caj_workerly_burndown" {
  name                = "dd6178e6-61ee-9e37-532c-150176a03013"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name

  role_definition_id = "${var.cosmos_account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"

  scope        = "${var.cosmos_account_id}/dbs/${azurerm_cosmosdb_sql_database.db.name}"
  principal_id = azurerm_user_assigned_identity.uami_caj_workerly_burndown.principal_id
}