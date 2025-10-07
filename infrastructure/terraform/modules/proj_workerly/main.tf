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
    paths = ["/emailLower"]
  }
}

resource "azurerm_cosmosdb_sql_container" "workspaces" {
  name                = "workspaces"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/id"]

  unique_key {
    paths = ["/nameLower"]
  }
}

resource "azurerm_cosmosdb_sql_container" "memberships" {
  name                = "memberships"
  resource_group_name = var.rg_name
  account_name        = var.cosmos_account_name
  database_name       = azurerm_cosmosdb_sql_database.db.name

  partition_key_paths = ["/workspaceId"]
}