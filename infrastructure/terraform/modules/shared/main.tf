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