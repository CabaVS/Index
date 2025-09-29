data "azurerm_resource_group" "rg" {
  name = var.rg_name
}

locals {
  location                       = data.azurerm_resource_group.rg.location
  law_name                       = "law-cvs-idx${var.postfix}"
  app_insights_name              = "appi-cvs-idx${var.postfix}"
  cae_name                       = "cae-cvs-idx${var.postfix}"
  acr_name                       = "acrcvsidx${replace(var.postfix, "-", "")}"
  sql_server_name                = "sql-cvs-idx${var.postfix}"
  sql_database_name_for_keycloak = "sqldb-cvs-idx-keycloak${var.postfix}"
}

module "shared" {
  source             = "./modules/shared"
  rg_name            = var.rg_name
  location           = local.location
  law_name           = local.law_name
  app_insights_name  = local.app_insights_name
  cae_name           = local.cae_name
  acr_name           = local.acr_name
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
