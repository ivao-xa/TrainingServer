# IVAO Extensible Training Server
> A plugin-based tool for IVAO ATC training

This repository distributes the new _IVAO Extensible Training Server_ as a replacement for HAL, presents a curated library of plugins, and provides documentation and libraries for the creation of new plugins.

## IMPORTANT NOTE
**This tool is for use in IVAO trainings administered by IVAO trainers using official IVAO software only. Usage by other means, other people, or for other purposes is expressly _forbidden_.**

## Downloading the Training Server
The latest copy of the Training Server can be obtained from the [Training Server binary folder](https://github.com/ivao-xa/TrainingServer/tree/main/TrainingServer) by selecting the binaries for your operating system and architecture.

## Downloading Plugins
Each plugin is distributed as a single `.dll` file. For help with a plugin, contact the plugin maintainer listed in the information displayed when loading the server.

## Problems & Feature Requests
For all problems and feature requests, [file an issue](https://github.com/ivao-xa/TrainingServer/issues/new/choose) using the appropriate Template. Be sure to fill in all fields, especially the affected plugin so we know where to route your report. Please _do not_ reach out to the curators of this repository directly as we do not have any responsibility for maintaining any of the plugins and the likelihood of an issue in the core server which cannot go through a GitHub issue is low. For help finding the name, look at the server log when starting the server.

## Creating Plugins
When creating plugins, reference the most recent `TrainingServer.Extensibility.dll`from the [extensibility folder](https://github.com/ivao-xa/TrainingServer/tree/main/TrainingServer.Extensibility) in your own project. Implement the `IPlugin` interface for aircraft message plugins and implement `IServerPlugin` for server message plugins.

## Contributing
To include your changes, fork this repository, create your plugin following the pattern of this repository, then submit a pull request. Contributors are required to check their own code for licensing issues and necessary approvals before filing a pull request.

## Troubleshooting
Windows <7 and some older versions of Windows 10 may not have the necessary capabilities to run the self-contained version of the application. If this issue affects you ("This app can't run on your PC"), download the latest .NET 7.0 or higher runtime and use the Framework version of the application.
