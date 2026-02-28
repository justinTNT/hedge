ONBOARD WORKFLOW

Install extension.
Extension:
  asks for domain
  verifies DNS automatically.
  sends provisioning request to server
  verifies provisioning
  configures itself for that domain.

backend:
  receives provisioning request from extension
  Creates tenant record.
  Creates D1 database.
  Creates R2 bucket namespace.
  Registers hostname mapping.

now has link to admin page

Users join by web via domain
