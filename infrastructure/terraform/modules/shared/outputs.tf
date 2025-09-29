output "sql_server_id" { value = azurerm_mssql_server.shared.id }
output "law_id" { value = azurerm_log_analytics_workspace.shared.id }
output "cae_id" { value = azurerm_container_app_environment.shared.id }