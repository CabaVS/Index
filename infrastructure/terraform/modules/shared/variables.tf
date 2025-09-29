variable "rg_name" {
  type = string
}

variable "location" {
  type = string
}

variable "law_name" {
  type = string
}

variable "app_insights_name" {
  type = string
}

variable "cae_name" {
  type = string
}

variable "acr_name" {
  type = string
}

variable "sql_server_name" {
  type = string
}

variable "sql_admin_login" {
  type = string
}

variable "sql_admin_password" {
  type      = string
  sensitive = true
}