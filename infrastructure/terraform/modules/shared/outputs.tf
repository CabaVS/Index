output "sql_server_id" { value = azurerm_mssql_server.shared.id }
output "law_id" { value = azurerm_log_analytics_workspace.shared.id }
output "cae_id" { value = azurerm_container_app_environment.shared.id }
output "acr_id" { value = azurerm_container_registry.shared.id }
output "acr_login_server" { value = azurerm_container_registry.shared.login_server }