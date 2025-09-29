data "azurerm_resource_group" "rg" {
  name = var.rg_name
}

locals {
  location                       = data.azurerm_resource_group.rg.location
  sql_server_name                = "sql-cvs-idx${var.postfix}"
  sql_database_name_for_keycloak = "sqldb-cvs-idx-keycloak${var.postfix}"
}

module "shared" {
  source             = "./modules/shared"
  rg_name            = var.rg_name
  location           = local.location
  sql_server_name    = local.sql_server_name
  sql_admin_login    = var.sql_admin_login
  sql_admin_password = var.sql_admin_password
}

module "proj_identityserver" {
  source            = "./modules/proj_identityserver"
  rg_name           = var.rg_name
  location          = local.location
  sql_server_id     = module.shared.sql_server_id
  sql_database_name = local.sql_database_name_for_keycloak
}
