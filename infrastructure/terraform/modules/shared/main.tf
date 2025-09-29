resource "azurerm_log_analytics_workspace" "shared" {
  name                = var.law_name
  resource_group_name = var.rg_name
  location            = var.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = 2
}

resource "azurerm_application_insights" "shared" {
  name                 = var.app_insights_name
  resource_group_name  = var.rg_name
  location             = var.location
  application_type     = "web"
  workspace_id         = azurerm_log_analytics_workspace.shared.id
  sampling_percentage  = 100
  retention_in_days    = 30
  daily_data_cap_in_gb = 2
}

resource "azurerm_container_app_environment" "shared" {
  name                       = var.cae_name
  resource_group_name        = var.rg_name
  location                   = var.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.shared.id
}

resource "azurerm_container_registry" "shared" {
  name                = var.acr_name
  resource_group_name = var.rg_name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = false
}

resource "azurerm_container_registry_task" "purge_keep10" {
  name                  = "purge-keep10"
  container_registry_id = azurerm_container_registry.shared.id

  platform { os = "Linux" }

  encoded_step {
    task_content = file("${path.module}/../../../assets/acr_purge.yml")
  }

  timer_trigger {
    name     = "nightly"
    schedule = "0 0 * * *"
    enabled  = true
  }
}

resource "azurerm_mssql_server" "shared" {
  name                          = var.sql_server_name
  resource_group_name           = var.rg_name
  location                      = var.location
  version                       = "12.0"
  administrator_login           = var.sql_admin_login
  administrator_login_password  = var.sql_admin_password
  public_network_access_enabled = true
  minimum_tls_version           = "1.2"
}