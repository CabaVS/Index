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
