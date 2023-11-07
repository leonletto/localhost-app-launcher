# Release Notes

## [v1.1.2] - 2023-11-07

### Added
- Support for the `MisconfiguredAppMessage` registry key to allow custom message to be displayed when the application is not configured correctly.
- Support for the `FailedLaunchMessage` registry key to allow custom message to be displayed when the application fails to launch.
- Support for the `GeneralFailureMessage` registry key to allow custom message to be displayed when an unknown error occurs.
- Support for the `SuccessfulLaunchMessage` registry key to allow custom message to be displayed when the application launches successfully and the window does not close.
- Added additional checks to verify certificates are valid.


## [v1.1.0] - 2023-10-03

### Added
- Support for Command line options to launch applications with arguments
- added LogLevel option to be able to set the log level and see Debug Logs
- enhanced parsing of commands to support spaces in the command line and options
- spaces in the command line and options must be surrounded by ""
- Dynamic reloading of changes in the registry without restarting the application - including LogLevel
- Added verification of the application path to ensure it exists before adding it to the configured programs


## [v1.0.8] - 2023-09-27

### Added
- Support for running applications from the .lnk Start Menu Items
- additional logging for troubleshooting
- Code Cleanup


## [v1.0.7] - 2023-09-25

### Added
- Added Docs folder with documentation.
- Added a timer which will check the registry for changes every 5 seconds.

### Changed
- Modified the "View Config" menu item to display the configuration dynamically in the browser rather than saving to a file and displaying it.
- Modified the config page to include a new field to test the application url.

### Fixed
- Resolved some uninstall bugs where the program folder was not removed.

## [v1.0.6] - 2023-09-19

### Added
- Initial release with basic features.
---

For more details, see the [commit log](https://github.com/leonletto/localhost-app-launcher/commits) or the [full documentation](https://github.com/leonletto/localhost-app-launcher/blob/main/Docs/Docs.md).
