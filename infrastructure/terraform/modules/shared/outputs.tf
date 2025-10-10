output "sql_server_id" { value = azurerm_mssql_server.shared.id }
output "law_id" { value = azurerm_log_analytics_workspace.shared.id }
output "appi_connection_string" { value = azurerm_application_insights.shared.connection_string }
output "cae_id" { value = azurerm_container_app_environment.shared.id }
output "acr_id" { value = azurerm_container_registry.shared.id }
output "acr_login_server" { value = azurerm_container_registry.shared.login_server }
output "cosmos_account_id" { value = azurerm_cosmosdb_account.cosmos.id }
output "cosmos_account_name" { value = azurerm_cosmosdb_account.cosmos.name }