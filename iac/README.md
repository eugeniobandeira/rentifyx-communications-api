# Infrastructure as Code

This folder contains IaC definitions for provisioning the infrastructure
required to run applications generated from this template.

## Structure

```
iac/
└── README.md    ← you are here
```

Add subfolders per tool or environment as needed, for example:

```
iac/
├── terraform/
│   ├── main.tf
│   ├── variables.tf
│   └── outputs.tf
├── bicep/
│   └── main.bicep
└── scripts/
    └── deploy.sh
```
