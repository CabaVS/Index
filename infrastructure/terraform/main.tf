data "azurerm_resource_group" "rg" {
  name = var.rg_name
}

locals {
  location                       = data.azurerm_resource_group.rg.location
  law_name                       = "law-cvs-idx${var.postfix}"
  app_insights_name              = "appi-cvs-idx${var.postfix}"
  cae_name                       = "cae-cvs-idx${var.postfix}"
  ca_name_for_keycloak           = "ca-cvs-idx-keycloak${var.postfix}"
  ca_name_for_workerly_web       = "ca-cvs-idx-workerly-web${var.postfix}"
  acr_name                       = "acrcvsidx${replace(var.postfix, "-", "")}"
  sql_server_name                = "sql-cvs-idx${var.postfix}"
  sql_database_name_for_keycloak = "sqldb-cvs-idx-keycloak${var.postfix}"
  cosmos_account_name            = "cosmos-cvs-idx${var.postfix}"
}

module "shared" {
  source              = "./modules/shared"
  rg_name             = var.rg_name
  location            = local.location
  law_name            = local.law_name
  app_insights_name   = local.app_insights_name
  cae_name            = local.cae_name
  acr_name            = local.acr_name
  sql_server_name     = local.sql_server_name
  sql_admin_login     = var.sql_admin_login
  sql_admin_password  = var.sql_admin_password
  cosmos_account_name = local.cosmos_account_name
}

module "proj_identityserver" {
  source                 = "./modules/proj_identityserver"
  rg_name                = var.rg_name
  location               = local.location
  appi_connection_string = module.shared.appi_connection_string
  acr_id                 = module.shared.acr_id
  acr_login_server       = module.shared.acr_login_server
  cae_id                 = module.shared.cae_id
  ca_name_for_keycloak   = local.ca_name_for_keycloak
  sql_server_id          = module.shared.sql_server_id
  sql_database_name      = local.sql_database_name_for_keycloak
}

module "proj_workerly" {
  source                   = "./modules/proj_workerly"
  rg_name                  = var.rg_name
  location                 = local.location
  cosmos_account_name      = module.shared.cosmos_account_name
  acr_id                   = module.shared.acr_id
  acr_login_server         = module.shared.acr_login_server
  cae_id                   = module.shared.cae_id
  ca_name_for_workerly_web = local.ca_name_for_workerly_web
}
