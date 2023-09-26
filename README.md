# localhost-app-launcher

A system utility for Streamlined Application Launching from Workspace ONE Hub and the Workspace ONE Access App Catalog.

## Table of Contents

- [Introduction](#introduction)
- [Features](#features)
- [Usage](#usage)
- [Deployment](#deployment)
  - [Dependencies](#dependencies)
  - [Certificates](#certificates)
  - [Configuration Sensor](#configuration-sensor)
  - [Installing LHLauncher](#installing-lhlauncher)
- [Contact](#contact)

## Introduction

Workspace ONE Hub centralizes enterprise applications. LHLauncher expands its capabilities by allowing you to launch local applications from a URL, similar to SaaS applications. Integrated with Workspace ONE Hub, LHLauncher offers a secure and efficient way to manage and launch locally installed applications.

## Features

- **Dynamic Configuration**: Reads configuration from the Windows Registry.
- **Process Verification**: Ensures the application's process is active.
- **Retries**: Automatically retries if the application doesn't start.
- **Logging**: Includes a logging feature for easier troubleshooting.
- **Security**: Uses a certificate published by you, which you configure for trust, so that all interactions are via SSL using your trusted certificates. It is hardcoded to listen only on 127.0.0.1.

## Usage

### A Practical Example with Notepad.exe

To configure LHLauncher for Notepad.exe:

1. Create a registry entry for Notepad.
2. Add a Web clip in Workspace ONE UEM or Access to point to `https://localhost.com/notepad`.

## Documentation

For more information, see the [documentation](Docs/Docs.md).

## Deployment

### Dependencies

LHLauncher is built with .NET runtime 6.0.21. Download it from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.21-windows-x64-installer).

### Certificates

Deploy your root CA certificate as a device profile and the Web server Certificate as a user profile.
The Web server certificate should have `localhost.com` as the subject and localhost in the SAN for ease of deploying.

### Configuration Sensor

Deploy the configuration Sensor to add the required registry entries.
This will also modify the windows hosts file to ensure `localhost.com` points to `localhost` at `127.0.0.1`.

### Installing LHLauncher

Deploy the LHLauncher.msi as a standard MSI application. Add the .NET Windows Desktop Runtime as a dependency.

## Contact

For questions or requests, contact Leon Letto at VMware – [lettol@vmware.com](mailto:lettol@vmware.com).

## License

This project is licensed under the MIT License – see the [LICENSE](LICENSE) file for details.

## Contributing

To contribute to this project, please open a pull request. We welcome contributions from the community.
