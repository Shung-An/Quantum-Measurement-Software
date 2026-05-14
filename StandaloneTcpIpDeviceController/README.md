# Standalone TCP/IP Device Controller

Small dependency-free Python tool for quick TCP/IP control of the Levante IR OPO software.

## GUI

```powershell
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py
```

Use `Connect`, then use the OPO tabs or type a raw command manually.

The GUI includes:

- `Status`: live readback dashboard for `state?`, powers, wavelengths, motor positions, plus manual refresh and timed polling.
- `Tuning`: set/query signal, idler, pump wavelength, repetition rate, automatic mode, optimization, fast tuning, and a conservative signal tune workflow.
- `Actuators`: crystal, cavity, and Lyot position controls.
- `Routines`: search, maximize, stabilization routines, and one-click routine stop.
- `Shutters/Log`: shutter open/close/query, close-all-shutters, and logging controls. Opening a shutter asks for confirmation.

Operational notes:

- `Start Poll` repeatedly queries the readback dashboard without filling the log.
- `Refresh Status` logs every status query and response.
- `Tune Signal Workflow` sends `set_signal_wavelength=<target>` and `automatic=TRUE`, then watches the state and wavelength readbacks for up to 120 seconds.
- `Stop Workflow`/`Stop OPO Routines` send the known routine-disable commands, including `automatic=FALSE`.
- Shutters are never opened by an automated workflow.

Raw command tools are still available:

- `Send`: sends a command without reading.
- `Query`: sends and reads one response.
- `Read`: reads a pending response.
- `Run Script`: runs a text file of commands. Lines ending in `?` are treated as queries.

Default device address is `10.128.1.176:51123`.
Default terminator is `CRLF`, matching typical Telnet Enter behavior.
If the app connects but commands return no response, click `Probe Terminators`.

## CLI

```powershell
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --query "*IDN?"
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --command "OUTP ON"
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --interactive
```

Safe first tests:

```powershell
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --query "state?"
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --query "temperature?"
python .\StandaloneTcpIpDeviceController\tcpip_device_controller.py --host 10.128.1.176 --port 51123 --query "interlock?"
```

Terminator options:

```powershell
--terminator LF
--terminator CR
--terminator CRLF
--terminator None
```
