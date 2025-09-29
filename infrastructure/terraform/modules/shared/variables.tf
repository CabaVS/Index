variable "rg_name" {
  type = string
}

variable "location" {
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