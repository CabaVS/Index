variable "rg_name" {
  type = string
}

variable "location" {
  type = string
}

variable "appi_connection_string" {
  type      = string
  sensitive = true
}

variable "acr_id" {
  type = string
}

variable "acr_login_server" {
  type = string
}

variable "cae_id" {
  type = string
}

variable "ca_name_for_keycloak" {
  type = string
}

variable "sql_server_id" {
  type = string
}

variable "sql_database_name" {
  type = string
}